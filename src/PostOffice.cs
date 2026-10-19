using System.Collections.Generic;
using UnityEngine;

namespace RavenPost
{
    /// <summary>A bundle of items in flight to (or waiting at) a perch.</summary>
    internal class Delivery
    {
        public double ArriveAt;
        public string From;
        public bool Notified;
        public byte[] Items;
        public Vector3 FromPos; // zero for deliveries saved by v0.1.0

        // v3: an optional letter riding with the raven
        public string Note = "";
        public string FromPlayer = "";
        public double SentAt;

        public bool HasNote => !string.IsNullOrEmpty(Note);

        public bool HasArrived(double now) => now >= ArriveAt;
    }

    /// <summary>
    /// Finds perches anywhere in the world and stores deliveries on the destination perch's saved data,
    /// so nothing depends on the destination area being loaded.
    /// </summary>
    internal static class PostOffice
    {
        public const string NameKey = "ravenpost_name";
        public const string InboxKey = "ravenpost_inbox";
        private const int InboxVersion = 3;

        /// <summary>Seconds before arrival that the landing animation starts, so it touches down on time.</summary>
        private const float LandingLead = RavenFlight.LandingSeconds;
        private const float LandingViewDistance = 80f;
        private static readonly HashSet<string> s_landingsShown = new HashSet<string>();

        private static readonly List<ZDO> s_perches = new List<ZDO>();
        private static float s_perchesRefreshedAt = -999f;

        /// <summary>v0.1 needs the full world data, which only the host/single-player game has.</summary>
        public static bool IsActive => ZNet.instance && ZNet.instance.IsServer() && ZDOMan.instance != null;

        public static double Now => ZNet.instance.GetTimeSeconds();

        public static List<ZDO> GetAllPerches()
        {
            if (Time.time - s_perchesRefreshedAt > 3f)
            {
                s_perches.Clear();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Plugin.PerchPrefabName, s_perches, ref index))
                {
                }
                s_perches.Sort((a, b) => string.Compare(GetName(a), GetName(b), System.StringComparison.OrdinalIgnoreCase));
                s_perchesRefreshedAt = Time.time;
            }
            s_perches.RemoveAll(z => z == null || !z.IsValid());
            return s_perches;
        }

        public static void InvalidatePerches() => s_perchesRefreshedAt = -999f;

        public static string GetName(ZDO zdo)
        {
            string name = zdo.GetString(NameKey);
            return string.IsNullOrEmpty(name) ? "Unnamed Perch" : name;
        }

        public static List<Delivery> ReadInbox(ZDO zdo)
        {
            var list = new List<Delivery>();
            byte[] data = zdo.GetByteArray(InboxKey);
            if (data == null || data.Length == 0)
            {
                return list;
            }

            var pkg = new ZPackage(data);
            int version = pkg.ReadInt();
            if (version < 1 || version > InboxVersion)
            {
                Plugin.Log.LogWarning($"Unknown inbox version {version}; ignoring.");
                return list;
            }
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var d = new Delivery
                {
                    ArriveAt = pkg.ReadDouble(),
                    From = pkg.ReadString(),
                    Notified = pkg.ReadBool(),
                    Items = pkg.ReadByteArray(),
                };
                if (version >= 2)
                {
                    d.FromPos = pkg.ReadVector3();
                }
                if (version >= 3)
                {
                    d.Note = pkg.ReadString();
                    d.FromPlayer = pkg.ReadString();
                    d.SentAt = pkg.ReadDouble();
                }
                list.Add(d);
            }
            return list;
        }

        public static void WriteInbox(ZDO zdo, List<Delivery> inbox)
        {
            var pkg = new ZPackage();
            pkg.Write(InboxVersion);
            pkg.Write(inbox.Count);
            foreach (var d in inbox)
            {
                pkg.Write(d.ArriveAt);
                pkg.Write(d.From ?? "");
                pkg.Write(d.Notified);
                pkg.Write(d.Items);
                pkg.Write(d.FromPos);
                pkg.Write(d.Note ?? "");
                pkg.Write(d.FromPlayer ?? "");
                pkg.Write(d.SentAt);
            }
            zdo.Set(InboxKey, pkg.GetArray());
        }

        public static byte[] PackItems(List<ItemDrop.ItemData> items)
        {
            var cargo = NewCargoInventory();
            foreach (var item in items)
            {
                cargo.AddItem(item.Clone());
            }
            var pkg = new ZPackage();
            cargo.Save(pkg);
            return pkg.GetArray();
        }

        public static Inventory UnpackItems(byte[] data)
        {
            var cargo = NewCargoInventory();
            if (data != null && data.Length > 0)
            {
                cargo.Load(new ZPackage(data));
            }
            return cargo;
        }

        private static Inventory NewCargoInventory() => new Inventory("RavenCargo", null, 8, 8);

        public static float FlightSeconds(float distance, float weight)
        {
            float seconds = distance / 100f * Plugin.SecondsPer100m.Value;
            seconds *= 1f + weight / 100f * Plugin.WeightSlowdown.Value;
            return Mathf.Max(Plugin.MinFlightSeconds.Value, seconds);
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            int total = Mathf.CeilToInt((float)seconds);
            return total >= 3600
                ? $"{total / 3600}h {total % 3600 / 60:00}m"
                : $"{total / 60}:{total % 60:00}";
        }

        public static string FormatDistance(float metres) =>
            metres >= 1000f ? $"{metres / 1000f:0.0} km" : $"{Mathf.RoundToInt(metres)} m";

        private static string ArrivalMessage(Delivery d, string at)
        {
            bool cargo = UnpackItems(d.Items).NrOfItems() > 0;
            if (d.HasNote && !cargo)
            {
                return $"Letter from {d.From} has landed at {at}";
            }
            return d.HasNote
                ? $"Raven from {d.From} has landed at {at} with a letter"
                : $"Raven from {d.From} has landed at {at}";
        }

        /// <summary>Tells the player when a raven lands, wherever they are.</summary>
        public static void NotifyArrivals()
        {
            if (!Player.m_localPlayer || !MessageHud.instance)
            {
                return;
            }

            double now = Now;
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            foreach (var zdo in GetAllPerches())
            {
                var inbox = ReadInbox(zdo);
                bool changed = false;
                bool nearby = Vector3.Distance(playerPos, zdo.GetPosition()) < LandingViewDistance;
                foreach (var d in inbox)
                {
                    // Start the landing a few seconds early so the bird touches down as the delivery arrives.
                    if (nearby && !d.Notified && d.ArriveAt - now <= LandingLead)
                    {
                        string key = $"{zdo.m_uid}:{d.ArriveAt}";
                        if (s_landingsShown.Add(key))
                        {
                            float seconds = (float)System.Math.Max(0.5, d.ArriveAt - now);
                            var instance = ZNetScene.instance ? ZNetScene.instance.FindInstance(zdo) : null;
                            var perch = instance ? instance.GetComponent<RavenPerch>() : null;
                            Vector3 seat = perch ? perch.SeatPosition : zdo.GetPosition() + Vector3.up * 1.2f;
                            // The landing bird takes the seat; the sitting raven reappears as it vanishes.
                            if (perch)
                            {
                                perch.HideSeatedRaven(seconds);
                            }
                            RavenFlight.Land(seat, d.FromPos, seconds);
                        }
                    }

                    if (!d.Notified && d.HasArrived(now))
                    {
                        d.Notified = true;
                        changed = true;
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
                            ArrivalMessage(d, GetName(zdo)));
                    }
                }
                if (changed)
                {
                    WriteInbox(zdo, inbox);
                }
            }
        }
    }
}
