using System.Collections.Generic;
using UnityEngine;

namespace RavenPost
{
    /// <summary>
    /// Multiplayer plumbing. The server is the post office: it knows every perch, receives deliveries and
    /// hands them to whoever owns the destination perch (only an object's owner should change its data),
    /// and tells players when ravens land. In single player the game is its own server, so the same
    /// messages are simply handled locally.
    /// </summary>
    internal static class RavenNet
    {
        private const string RpcRequestPerches = "RavenPost_RequestPerches";
        private const string RpcPerches = "RavenPost_Perches";
        private const string RpcDeliver = "RavenPost_Deliver";
        private const string RpcAddToInbox = "RavenPost_AddToInbox";
        private const string RpcServerWrite = "RavenPost_ServerWrite";
        private const string RpcNotify = "RavenPost_Notify";
        private const string RpcTakeOff = "RavenPost_TakeOff";

        private const float TakeOffViewDistance = 150f;

        /// <summary>A "raven has landed" message the server still has to send.</summary>
        private class PendingNotice
        {
            public double At;
            public string Recipient; // player name; null = everyone
            public string Message;
        }

        private static readonly List<PendingNotice> s_pending = new List<PendingNotice>();
        private static readonly HashSet<string> s_scheduled = new HashSet<string>();
        private static bool s_scannedWorld;
        private static float s_nextTick;

        /// <summary>Called when a world starts (Game.Start), once ZRoutedRpc exists.</summary>
        public static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                return;
            }
            s_pending.Clear();
            s_scheduled.Clear();
            s_scannedWorld = false;
            PostOffice.ResetForNewWorld();

            rpc.Register(RpcRequestPerches, new System.Action<long>(OnRequestPerches));
            rpc.Register<ZPackage>(RpcPerches, OnPerches);
            rpc.Register<ZDOID, ZDOID, ZPackage>(RpcDeliver, OnDeliver);
            rpc.Register<ZDOID, ZPackage>(RpcAddToInbox, OnAddToInbox);
            rpc.Register<ZDOID, ZPackage>(RpcServerWrite, OnServerWrite);
            rpc.Register<string>(RpcNotify, OnNotify);
            rpc.Register<Vector3, Vector3, ZPackage>(RpcTakeOff, OnTakeOff);
        }

        // ------------------------------------------------------------ perch list

        public static void RequestPerches() => ZRoutedRpc.instance?.InvokeRoutedRPC(RpcRequestPerches);

        private static void OnRequestPerches(long sender)
        {
            if (!PostOffice.IsServer)
            {
                return;
            }
            var perches = PostOffice.GetPerches();
            var pkg = new ZPackage();
            pkg.Write(perches.Count);
            foreach (var p in perches)
            {
                pkg.Write(p.Id);
                pkg.Write(p.Name);
                pkg.Write(p.Position);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcPerches, pkg);
        }

        private static void OnPerches(long sender, ZPackage pkg)
        {
            if (PostOffice.IsServer)
            {
                return;
            }
            var list = new List<PerchInfo>();
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                list.Add(new PerchInfo { Id = pkg.ReadZDOID(), Name = pkg.ReadString(), Position = pkg.ReadVector3() });
            }
            PostOffice.SetClientPerches(list);
        }

        // ------------------------------------------------------------ deliveries

        /// <summary>Sends a delivery to <paramref name="destination"/>; it goes back to <paramref name="source"/> if the destination is gone.</summary>
        public static void SendDelivery(ZDOID destination, ZDOID source, Delivery d) =>
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcDeliver, destination, source, PostOffice.Serialize(d));

        private static void OnDeliver(long sender, ZDOID destination, ZDOID source, ZPackage pkg)
        {
            if (!PostOffice.IsServer)
            {
                return;
            }
            var d = PostOffice.Deserialize(pkg);
            var zdo = ZDOMan.instance.GetZDO(destination);
            string destName;
            if (zdo == null || !zdo.IsValid())
            {
                // The destination perch was destroyed while the raven was being sent: return to sender.
                zdo = ZDOMan.instance.GetZDO(source);
                if (zdo == null || !zdo.IsValid())
                {
                    Plugin.Log.LogWarning($"Delivery from '{d.From}' has nowhere to go; both perches are gone.");
                    return;
                }
                destName = PostOffice.GetName(zdo);
                Notify(d.FromPlayer, $"The perch was gone - your raven flew back to {destName}");
            }
            else
            {
                destName = PostOffice.GetName(zdo);
            }

            WriteToOwner(zdo, d);
            Schedule(zdo.m_uid, destName, d);
        }

        /// <summary>
        /// Only the owner of an object should change it, or one of the two changes can be lost. If a player
        /// owns the perch (they're near it), hand them the delivery; otherwise the server writes it.
        /// </summary>
        private static void WriteToOwner(ZDO zdo, Delivery d)
        {
            long owner = zdo.GetOwner();
            long me = ZDOMan.GetSessionID();
            if (owner != 0 && owner != me && ZNet.instance.GetPeer(owner) != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcAddToInbox, zdo.m_uid, PostOffice.Serialize(d));
                return;
            }
            PostOffice.AppendToInbox(zdo, d);
        }

        private static void OnAddToInbox(long sender, ZDOID id, ZPackage pkg)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null && zdo.IsOwner())
            {
                PostOffice.AppendToInbox(zdo, PostOffice.Deserialize(pkg));
                return;
            }
            // Ownership moved on in the meantime: let the server store it.
            pkg.SetPos(0);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcServerWrite, id, pkg);
        }

        private static void OnServerWrite(long sender, ZDOID id, ZPackage pkg)
        {
            if (!PostOffice.IsServer)
            {
                return;
            }
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null && zdo.IsValid())
            {
                PostOffice.AppendToInbox(zdo, PostOffice.Deserialize(pkg));
            }
        }

        // ------------------------------------------------------------ notifications (server)

        private static void Schedule(ZDOID perch, string perchName, Delivery d)
        {
            if (!s_scheduled.Add(d.Key(perch)))
            {
                return;
            }
            string message = PostOffice.ArrivalMessage(d, perchName);
            // The sender hears about their raven; a letter is news for everyone.
            s_pending.Add(new PendingNotice { At = d.ArriveAt, Recipient = d.HasNote ? null : d.FromPlayer, Message = message });
        }

        /// <summary>Server housekeeping, every second: send due notifications.</summary>
        public static void ServerTick()
        {
            if (!PostOffice.IsServer || !PostOffice.IsActive || Time.time < s_nextTick)
            {
                return;
            }
            s_nextTick = Time.time + 1f;

            if (!s_scannedWorld)
            {
                // After a restart, pick up ravens that were still in the air.
                s_scannedWorld = true;
                double t = PostOffice.Now;
                foreach (var zdo in PostOffice.GetAllPerchZdos())
                {
                    foreach (var d in PostOffice.ReadInbox(zdo))
                    {
                        if (!d.HasArrived(t))
                        {
                            Schedule(zdo.m_uid, PostOffice.GetName(zdo), d);
                        }
                    }
                }
            }

            double now = PostOffice.Now;
            for (int i = s_pending.Count - 1; i >= 0; i--)
            {
                var notice = s_pending[i];
                if (notice.At <= now)
                {
                    s_pending.RemoveAt(i);
                    Notify(notice.Recipient, notice.Message);
                }
            }
        }

        /// <summary>Shows a top-left message to the named player (or everyone if null), if they're online.</summary>
        private static void Notify(string playerName, string message)
        {
            if (playerName == null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcNotify, message);
                return;
            }
            // The host's own player isn't in the peer list.
            if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerName() == playerName)
            {
                OnNotify(0, message);
            }
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_playerName == playerName)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcNotify, message);
                }
            }
        }

        private static void OnNotify(long sender, string message)
        {
            if (MessageHud.instance)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, message);
            }
        }

        // ------------------------------------------------------------ visuals

        /// <summary>Lets other players near the perch see the raven take off too.</summary>
        public static void BroadcastTakeOff(Vector3 from, Vector3 to, float flightSeconds, float speed)
        {
            var pkg = new ZPackage();
            pkg.Write(flightSeconds);
            pkg.Write(speed);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcTakeOff, from, to, pkg);
        }

        private static void OnTakeOff(long sender, Vector3 from, Vector3 to, ZPackage pkg)
        {
            if (sender == ZDOMan.GetSessionID() || !Player.m_localPlayer)
            {
                return; // the sender already shows its own
            }
            if (Vector3.Distance(Player.m_localPlayer.transform.position, from) > TakeOffViewDistance)
            {
                return;
            }
            float seconds = pkg.ReadSingle();
            float speed = pkg.ReadSingle();
            float shown = RavenFlight.Launch(from, to, seconds, speed);

            // Hide that perch's sitting raven while it's away.
            foreach (var perch in Object.FindObjectsByType<RavenPerch>(FindObjectsSortMode.None))
            {
                if (Vector3.Distance(perch.SeatPosition, from) < 0.5f)
                {
                    perch.HideSeatedRaven(shown + 1f);
                }
            }
        }
    }
}
