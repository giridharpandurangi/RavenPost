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

            if (!s_validatedTiers)
            {
                s_validatedTiers = true;
                PerchTiers.ValidateItems();
            }

            PostOffice.InvalidatePerches();
            InvokeRepeating(nameof(CollectArrivals), 1f, 2f);
        }

        private static bool s_validatedTiers;
        private static readonly System.Reflection.FieldInfo s_invWidth = HarmonyLib.AccessTools.Field(typeof(Inventory), "m_width");
        private static readonly System.Reflection.FieldInfo s_invHeight = HarmonyLib.AccessTools.Field(typeof(Inventory), "m_height");
        private static readonly System.Reflection.FieldInfo s_invName = HarmonyLib.AccessTools.Field(typeof(Inventory), "m_name");

        // ------------------------------------------------------------ tiers

        public int TierIndex => Zdo != null ? Mathf.Clamp(Zdo.GetInt(PerchTiers.TierKey), 0, PerchTiers.Max) : 0;

        internal PerchTier Tier => PerchTiers.Get(TierIndex);

        internal PerchTier NextTier => TierIndex < PerchTiers.Max ? PerchTiers.Get(TierIndex + 1) : null;

        /// <summary>
        /// Sizes the container for a tier. Called by ContainerAwakePatch before the inventory is created,
        /// and after an upgrade. Never shrinks below the items already stored.
        /// </summary>
        internal static void ApplyTier(Container container, PerchTier tier)
        {
            container.m_name = tier.Name;
            int width = tier.CargoWidth;
            int height = tier.CargoHeight;

            var inventory = container.GetInventory();
            if (inventory == null)
            {
                container.m_width = width;
                container.m_height = height;
                return;
            }
            foreach (var item in inventory.GetAllItems())
            {
                width = Mathf.Max(width, item.m_gridPos.x + 1);
                height = Mathf.Max(height, item.m_gridPos.y + 1);
            }
            container.m_width = width;
            container.m_height = height;
            s_invWidth?.SetValue(inventory, width);
            s_invHeight?.SetValue(inventory, height);
            s_invName?.SetValue(inventory, tier.Name);
        }

        /// <summary>Upgrades to the next tier, taking the materials from the player's inventory.</summary>
        public void Upgrade(Player player)
        {
            var next = NextTier;
            if (Zdo == null || !player || next == null)
            {
                return;
            }
            if (!_nview.IsOwner())
            {
                player.Message(MessageHud.MessageType.Center, "Someone else is using this perch");
                return;
            }
            var missing = next.MissingItems();
            if (missing.Count > 0)
            {
                player.Message(MessageHud.MessageType.Center, $"{next.Name} needs items this game doesn't have yet (see config)");
                return;
            }

            var inventory = player.GetInventory();
            foreach (var req in next.Requirements())
            {
                var data = PerchTiers.ItemData(req.Key);
                if (inventory.CountItems(data.m_shared.m_name) < req.Value)
                {
                    player.Message(MessageHud.MessageType.Center,
                        Localization.instance.Localize($"Not enough {data.m_shared.m_name} ({req.Value} needed)"));
                    return;
                }
            }
            foreach (var req in next.Requirements())
            {
                inventory.RemoveItem(PerchTiers.ItemData(req.Key).m_shared.m_name, req.Value);
            }

            Zdo.Set(PerchTiers.TierKey, next.Index);
            ApplyTier(_container, next);
            player.Message(MessageHud.MessageType.Center,
                $"Upgraded to {next.Name}: {next.CargoWidth}x{next.CargoHeight} cargo, {RavenFood.FormatSpeed(next.SpeedMultiplier)} speed");
            Plugin.Log.LogInfo($"'{PerchName}' upgraded to {next.Name}");
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

        public List<ZDO> Destinations() =>
            PostOffice.GetAllPerches().Where(z => z.m_uid != Zdo.m_uid).ToList();

        public ZDO SelectedDestination(List<ZDO> destinations)
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

        public void Select(ZDOID destination) => _selectedDestination = destination;

        public float DistanceTo(ZDO destination) => Vector3.Distance(transform.position, destination.GetPosition());

        public float CargoWeight => _container.GetInventory()?.GetTotalWeight() ?? 0f;

        public List<float> Meals => Zdo != null ? RavenFood.ReadMeals(Zdo) : new List<float>();

        /// <summary>Speed multiplier for the next flight: best stored meal, hungry speed, or 1 with food off.</summary>
        public float NextMealSpeed
        {
            get
            {
                if (!Plugin.UseFood.Value)
                {
                    return 1f;
                }
                var meals = Meals;
                return meals.Count > 0 ? meals[0] : Mathf.Max(0.05f, Plugin.HungrySpeed.Value);
            }
        }

        public bool IsHungry => Plugin.UseFood.Value && Meals.Count == 0;

        /// <summary>A hungry raven only refuses to fly when configured to.</summary>
        public bool RefusesToFly => IsHungry && Plugin.Hungry.Value == Plugin.HungryBehaviour.Refuse;

        /// <summary>Short status for hover text and the send window.</summary>
        public string MealsStatus
        {
            get
            {
                if (!Plugin.UseFood.Value)
                {
                    return "";
                }
                var meals = Meals;
                if (meals.Count > 0)
                {
                    return $"Meals: {meals.Count}/{Mathf.Max(1, Plugin.MaxMeals.Value)} (next {RavenFood.FormatSpeed(meals[0])} speed)";
                }
                return RefusesToFly
                    ? "<color=#ff6060>Hungry - won't fly until fed</color>"
                    : $"<color=#ff9a40>Hungry - flies slowly ({RavenFood.FormatSpeed(NextMealSpeed)})</color>";
            }
        }

        /// <summary>Flight time to <paramref name="destination"/> with the current cargo and next meal.</summary>
        public float FlightSecondsTo(ZDO destination) =>
            PostOffice.FlightSeconds(DistanceTo(destination), CargoWeight) / (NextMealSpeed * Tier.SpeedMultiplier);

        /// <summary>A letter alone: no cargo weight, no meal, just the perch's speed.</summary>
        public float LetterSecondsTo(ZDO destination) =>
            PostOffice.FlightSeconds(DistanceTo(destination), 0f) / Tier.SpeedMultiplier;

        /// <summary>Feeds one of <paramref name="item"/> from the player's inventory. False if ravens won't eat it.</summary>
        public bool Feed(Player player, ItemDrop.ItemData item)
        {
            if (Zdo == null || !player)
            {
                return false;
            }
            float speed = RavenFood.SpeedFor(item);
            if (speed <= 0f)
            {
                return false;
            }
            if (!_nview.IsOwner())
            {
                player.Message(MessageHud.MessageType.Center, "Someone else is using this perch");
                return true;
            }

            var meals = Meals;
            int max = Mathf.Max(1, Plugin.MaxMeals.Value);
            if (meals.Count >= max)
            {
                player.Message(MessageHud.MessageType.Center, "The raven is full");
                return true;
            }
            if (!player.GetInventory().RemoveOneItem(item))
            {
                return true;
            }

            meals.Add(speed);
            RavenFood.WriteMeals(Zdo, meals);
            player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(
                $"The raven ate {item.m_shared.m_name} ({RavenFood.FormatSpeed(speed)} speed) - {meals.Count}/{max} meals"));
            return true;
        }

        /// <summary>Feeds the fastest food the player is carrying.</summary>
        public void FeedBest(Player player)
        {
            var food = RavenFood.BestFood(player.GetInventory());
            if (food == null)
            {
                player.Message(MessageHud.MessageType.Center, "You have nothing the raven will eat");
                return;
            }
            Feed(player, food);
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
            if (!_nview.IsOwner())
            {
                player.Message(MessageHud.MessageType.Center, "Someone else is using this perch");
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
            string note = Draft;
            bool hasNote = !string.IsNullOrEmpty(note);
            if (items.Count == 0 && !hasNote)
            {
                player.Message(MessageHud.MessageType.Center, "Put some cargo in the perch or write a note first");
                return;
            }
            // A note on its own is light: no meal needed and it flies at normal speed.
            bool noteOnly = items.Count == 0;

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

            if (!noteOnly && RefusesToFly)
            {
                player.Message(MessageHud.MessageType.Center, "Your raven is hungry - feed it first");
                return;
            }
            var meals = Meals;
            float mealSpeed = noteOnly ? 1f : NextMealSpeed;

            float distance = Vector3.Distance(transform.position, destination.GetPosition());
            float weight = inventory.GetTotalWeight();
            float seconds = PostOffice.FlightSeconds(distance, weight) / (mealSpeed * Tier.SpeedMultiplier);

            var delivery = new Delivery
            {
                ArriveAt = PostOffice.Now + seconds,
                From = PerchName,
                Notified = false,
                Items = PostOffice.PackItems(items),
                FromPos = transform.position,
                Note = note ?? "",
                FromPlayer = player.GetPlayerName(),
                SentAt = PostOffice.Now,
            };

            var inbox = PostOffice.ReadInbox(destination);
            inbox.Add(delivery);
            PostOffice.WriteInbox(destination, inbox);

            // Only empty the cargo once the delivery is safely stored on the destination.
            inventory.RemoveAll();
            Draft = "";
            if (!noteOnly && Plugin.UseFood.Value && meals.Count > 0)
            {
                meals.RemoveAt(0);
                RavenFood.WriteMeals(Zdo, meals);
            }

            float shown = RavenFlight.Launch(SeatPosition, destination.GetPosition(), seconds, mealSpeed * Tier.SpeedMultiplier);
            HideSeatedRaven(shown + 1f);
            string what = noteOnly ? "Letter" : hasNote ? "Raven and letter" : "Raven";
            player.Message(MessageHud.MessageType.Center,
                $"{what} sent to {PostOffice.GetName(destination)} - arrives in {PostOffice.FormatDuration(seconds)}");
            Plugin.Log.LogInfo($"Sent {items.Count} stack(s) from '{PerchName}' to '{PostOffice.GetName(destination)}', " +
                               $"{distance:0} m, {weight:0.#} weight, note {(hasNote ? "yes" : "no")}, meal {RavenFood.FormatSpeed(mealSpeed)}, {seconds:0} s");
        }

        /// <summary>The note waiting to be sent with the next raven.</summary>
        public string Draft
        {
            get => Zdo?.GetString(RavenMail.DraftKey) ?? "";
            set
            {
                if (Zdo != null && _nview.IsOwner())
                {
                    string text = (value ?? "").Trim();
                    if (text.Length > RavenMail.MaxNoteLength)
                    {
                        text = text.Substring(0, RavenMail.MaxNoteLength);
                    }
                    Zdo.Set(RavenMail.DraftKey, text);
                }
            }
        }

        internal List<Letter> Mailbox => Zdo != null ? RavenMail.Read(Zdo) : new List<Letter>();

        public int UnreadLetters => Zdo != null ? RavenMail.UnreadCount(Zdo) : 0;

        /// <summary>Marks letter <paramref name="index"/> (in saved order) as read and returns it.</summary>
        internal Letter OpenLetter(int index)
        {
            var letters = Mailbox;
            if (index < 0 || index >= letters.Count)
            {
                return null;
            }
            if (!letters[index].Read && _nview.IsOwner())
            {
                letters[index].Read = true;
                RavenMail.Write(Zdo, letters);
            }
            return letters[index];
        }

        public void DeleteLetter(int index)
        {
            var letters = Mailbox;
            if (index >= 0 && index < letters.Count && _nview.IsOwner())
            {
                letters.RemoveAt(index);
                RavenMail.Write(Zdo, letters);
            }
        }

        private Transform SeatedRaven => transform.Find("RavenPost_PerchedRaven");

        /// <summary>Where the perched raven sits (top of the post), or above the box if there is none.</summary>
        public Vector3 SeatPosition
        {
            get
            {
                var seated = SeatedRaven;
                return seated ? seated.position : transform.position + Vector3.up * 1.2f;
            }
        }

        /// <summary>Hides the sitting raven while it is away flying, so there's only ever one bird.</summary>
        public void HideSeatedRaven(float seconds)
        {
            var seated = SeatedRaven;
            if (!seated)
            {
                return;
            }
            seated.gameObject.SetActive(false);
            CancelInvoke(nameof(ShowSeatedRaven));
            Invoke(nameof(ShowSeatedRaven), seconds);
        }

        private void ShowSeatedRaven()
        {
            var seated = SeatedRaven;
            if (seated)
            {
                seated.gameObject.SetActive(true);
            }
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

                // The letter goes straight into the mailbox, even if the cargo has to wait for room.
                if (d.HasNote)
                {
                    RavenMail.Deliver(Zdo, d);
                    d.Note = "";
                    changed = true;
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
            sb.Append($"\n<color=orange>{PerchName}</color>  <color=grey>{Tier.Name}, {RavenFood.FormatSpeed(Tier.SpeedMultiplier)}</color>");

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
                float seconds = FlightSecondsTo(destination);
                sb.Append($"\n[<color=yellow><b>{Plugin.NextDestinationKey.Value}</b></color>] To: {PostOffice.GetName(destination)} " +
                          $"({PostOffice.FormatDistance(distance)}, {PostOffice.FormatDuration(seconds)})" +
                          (destinations.Count > 1 ? $"  <color=grey>{destinations.FindIndex(z => z.m_uid == destination.m_uid) + 1}/{destinations.Count}</color>" : ""));
                sb.Append($"\n[<color=yellow><b>{Plugin.SendKey.Value}</b></color>] Send cargo");
            }
            sb.Append($"\n[<color=yellow><b>{Plugin.RenameKey.Value}</b></color>] Rename");
            if (Plugin.UseFood.Value)
            {
                sb.Append($"\n{MealsStatus}");
                sb.Append("\n<color=grey>Use food from your hotbar on the perch to feed</color>");
            }

            int unread = UnreadLetters;
            if (unread > 0)
            {
                sb.Append($"\n<color=yellow>{unread} unread letter{(unread == 1 ? "" : "s")} - open the perch to read</color>");
            }
            if (!string.IsNullOrEmpty(Draft))
            {
                sb.Append($"\n<color=grey>Note ready: \"{RavenMail.Preview(Draft)}\"</color>");
            }

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
