using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RavenPost
{
    /// <summary>Added to the perch prefab next to its Container. The Container holds the cargo.</summary>
    public class RavenPerch : MonoBehaviour, TextReceiver
    {
        private ZNetView _nview;
        private Container _container;
        private ZDOID _selectedDestination = ZDOID.None;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _container = GetComponent<Container>();
            if (!_nview || _nview.GetZDO() == null)
            {
                return; // placement ghost
            }

            var wearNTear = GetComponent<WearNTear>();
            if (wearNTear)
            {
                wearNTear.m_onDestroyed += OnDestroyed;
            }

            PostOffice.InvalidatePerches();
            InvokeRepeating(nameof(CollectArrivals), 1f, 2f);
        }

        private ZDO Zdo => _nview && _nview.IsValid() ? _nview.GetZDO() : null;

        public string PerchName => Zdo != null ? PostOffice.GetName(Zdo) : "Raven Perch";

        // TextReceiver (rename dialog)
        public string GetText() => Zdo?.GetString(PostOffice.NameKey) ?? "";

        public void SetText(string text)
        {
            if (Zdo == null || !_nview.IsOwner())
            {
                return;
            }
            text = (text ?? "").Trim();
            Zdo.Set(PostOffice.NameKey, text);
            PostOffice.InvalidatePerches();
        }

        private List<ZDO> Destinations() =>
            PostOffice.GetAllPerches().Where(z => z.m_uid != Zdo.m_uid).ToList();

        private ZDO SelectedDestination(List<ZDO> destinations)
        {
            if (destinations.Count == 0)
            {
                return null;
            }
            var found = destinations.FirstOrDefault(z => z.m_uid == _selectedDestination);
            if (found == null)
            {
                found = destinations[0];
                _selectedDestination = found.m_uid;
            }
            return found;
        }

        public void NextDestination()
        {
            if (Zdo == null)
            {
                return;
            }
            var destinations = Destinations();
            if (destinations.Count == 0)
            {
                return;
            }
            int index = destinations.FindIndex(z => z.m_uid == _selectedDestination);
            _selectedDestination = destinations[(index + 1) % destinations.Count].m_uid;
        }

        public void Send(Player player)
        {
            if (Zdo == null || !PostOffice.IsActive)
            {
                return;
            }
            if (!_nview.IsOwner() || _container.IsInUse())
            {
                player.Message(MessageHud.MessageType.Center, "Close the perch before sending");
                return;
            }

            var destination = SelectedDestination(Destinations());
            if (destination == null)
            {
                player.Message(MessageHud.MessageType.Center, "Build another Raven Perch to send to");
                return;
            }

            var inventory = _container.GetInventory();
            var items = inventory.GetAllItems().ToList();
            if (items.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "Put some cargo in the perch first");
                return;
            }

            if (!Plugin.AllowNonTeleportable.Value && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll))
            {
                var blocked = items.FirstOrDefault(i => !i.m_shared.m_teleportable);
                if (blocked != null)
                {
                    player.Message(MessageHud.MessageType.Center,
                        Localization.instance.Localize($"Ravens refuse to carry {blocked.m_shared.m_name}"));
                    return;
                }
            }

            float distance = Vector3.Distance(transform.position, destination.GetPosition());
            float weight = inventory.GetTotalWeight();
            float seconds = PostOffice.FlightSeconds(distance, weight);

            var delivery = new Delivery
            {
                ArriveAt = PostOffice.Now + seconds,
                From = PerchName,
                Notified = false,
                Items = PostOffice.PackItems(items),
                FromPos = transform.position,
            };

            var inbox = PostOffice.ReadInbox(destination);
            inbox.Add(delivery);
            PostOffice.WriteInbox(destination, inbox);

            // Only empty the cargo once the delivery is safely stored on the destination.
            inventory.RemoveAll();

            RavenFlight.Launch(transform.position + Vector3.up * 1.2f, destination.GetPosition());
            player.Message(MessageHud.MessageType.Center,
                $"Raven sent to {PostOffice.GetName(destination)} - arrives in {PostOffice.FormatDuration(seconds)}");
            Plugin.Log.LogInfo($"Sent {items.Count} stack(s) from '{PerchName}' to '{PostOffice.GetName(destination)}', " +
                               $"{distance:0} m, {weight:0.#} weight, {seconds:0} s");
        }

        /// <summary>Moves arrived deliveries into the cargo slots. Runs whenever the perch is loaded.</summary>
        private void CollectArrivals()
        {
            if (Zdo == null || !_nview.IsOwner() || !PostOffice.IsActive || _container.GetInventory() == null)
            {
                return;
            }

            var inbox = PostOffice.ReadInbox(Zdo);
            if (inbox.Count == 0)
            {
                return;
            }

            double now = PostOffice.Now;
            var target = _container.GetInventory();
            bool changed = false;
            var remaining = new List<Delivery>();

            foreach (var d in inbox)
            {
                if (!d.HasArrived(now))
                {
                    remaining.Add(d);
                    continue;
                }

                var cargo = PostOffice.UnpackItems(d.Items);
                var leftovers = new List<ItemDrop.ItemData>();
                foreach (var item in cargo.GetAllItems().ToList())
                {
                    if (target.CanAddItem(item) && target.AddItem(item))
                    {
                        changed = true;
                    }
                    else
                    {
                        leftovers.Add(item);
                    }
                }

                if (leftovers.Count > 0)
                {
                    // Perch is full: keep the rest waiting until there is room.
                    if (leftovers.Count != cargo.NrOfItems())
                    {
                        changed = true;
                    }
                    d.Items = PostOffice.PackItems(leftovers);
                    remaining.Add(d);
                }
                else
                {
                    changed = true;
                }
            }

            if (changed)
            {
                PostOffice.WriteInbox(Zdo, remaining);
            }
        }

        private void OnDestroyed()
        {
            if (Zdo == null || !_nview.IsOwner())
            {
                return;
            }

            // Don't lose anything in flight to a perch that no longer exists: drop it here.
            foreach (var d in PostOffice.ReadInbox(Zdo))
            {
                foreach (var item in PostOffice.UnpackItems(d.Items).GetAllItems())
                {
                    var pos = transform.position + Vector3.up * 0.5f + UnityEngine.Random.insideUnitSphere * 0.3f;
                    ItemDrop.DropItem(item, 0, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));
                }
            }
            PostOffice.WriteInbox(Zdo, new List<Delivery>());
            PostOffice.InvalidatePerches();
        }

        /// <summary>Lines added under the chest's normal hover text.</summary>
        public string GetExtraHoverText()
        {
            if (Zdo == null)
            {
                return "";
            }

            var sb = new StringBuilder();
            sb.Append($"\n<color=orange>{PerchName}</color>");

            if (!PostOffice.IsActive)
            {
                sb.Append("\n<color=grey>Raven Post v0.1 works in single player only</color>");
                return sb.ToString();
            }

            var destinations = Destinations();
            var destination = SelectedDestination(destinations);
            if (destination == null)
            {
                sb.Append("\n<color=grey>Build another perch to send to</color>");
            }
            else
            {
                float distance = Vector3.Distance(transform.position, destination.GetPosition());
                float seconds = PostOffice.FlightSeconds(distance, _container.GetInventory()?.GetTotalWeight() ?? 0f);
                sb.Append($"\n[<color=yellow><b>{Plugin.NextDestinationKey.Value}</b></color>] To: {PostOffice.GetName(destination)} " +
                          $"({PostOffice.FormatDistance(distance)}, {PostOffice.FormatDuration(seconds)})" +
                          (destinations.Count > 1 ? $"  <color=grey>{destinations.FindIndex(z => z.m_uid == destination.m_uid) + 1}/{destinations.Count}</color>" : ""));
                sb.Append($"\n[<color=yellow><b>{Plugin.SendKey.Value}</b></color>] Send cargo");
            }
            sb.Append($"\n[<color=yellow><b>{Plugin.RenameKey.Value}</b></color>] Rename");

            var inbox = PostOffice.ReadInbox(Zdo);
            if (inbox.Count > 0)
            {
                double now = PostOffice.Now;
                var inFlight = inbox.Where(d => !d.HasArrived(now)).OrderBy(d => d.ArriveAt).ToList();
                int waiting = inbox.Count - inFlight.Count;
                if (inFlight.Count > 0)
                {
                    sb.Append($"\nIncoming: {inFlight.Count} raven(s), next in {PostOffice.FormatDuration(inFlight[0].ArriveAt - now)}");
                }
                if (waiting > 0)
                {
                    sb.Append($"\n<color=yellow>{waiting} delivery(s) waiting - make room in the cargo slots</color>");
                }
            }

            return sb.ToString();
        }
    }
}
