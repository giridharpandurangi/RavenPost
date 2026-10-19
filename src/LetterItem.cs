using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace RavenPost
{
    /// <summary>
    /// A craftable letter that keeps its own text: write in it from the inventory, read it in the tooltip,
    /// keep it as a journal, store it anywhere, or send it as raven cargo. The text lives in the item's
    /// custom data, which Valheim saves with the item wherever it is.
    /// </summary>
    internal static class LetterItem
    {
        public const string PrefabName = "RavenLetter";
        public const int MaxLength = 500;

        private const string TextKey = "ravenpost_text";
        private const string AuthorKey = "ravenpost_author";
        private const string DayKey = "ravenpost_day";
        private const string FromKey = "ravenpost_from";

        public static void Register()
        {
            var config = new ItemConfig
            {
                Name = "Letter",
                Description = "Write on it, keep it as a journal, or send it with a raven.",
                CraftingStation = "piece_workbench",
                Amount = 2,
                Icon = CreateIcon(),
                Requirements = new[]
                {
                    new RequirementConfig("LeatherScraps", 1),
                    new RequirementConfig("Coal", 1),
                },
            };

            var item = new CustomItem(PrefabName, "LeatherScraps", config);
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxStackSize = 1; // each letter holds its own text
            shared.m_weight = 0.1f;
            shared.m_teleportable = true;
            s_shared = shared;
            ItemManager.Instance.AddItem(item);
        }

        private static ItemDrop.ItemData.SharedData s_shared;

        public static bool IsLetter(ItemDrop.ItemData item) =>
            item != null && ((item.m_dropPrefab && item.m_dropPrefab.name == PrefabName) ||
                             (s_shared != null && item.m_shared == s_shared));

        public static string GetText(ItemDrop.ItemData item) =>
            item.m_customData.TryGetValue(TextKey, out var text) ? text : "";

        public static void SetText(ItemDrop.ItemData item, string text, string author)
        {
            text = (text ?? "").Trim();
            if (text.Length > MaxLength)
            {
                text = text.Substring(0, MaxLength);
            }
            item.m_customData[TextKey] = text;
            item.m_customData[AuthorKey] = author ?? "";
            item.m_customData[DayKey] = RavenMail.Day(PostOffice.Now).ToString();
        }

        /// <summary>Lines added to the item tooltip.</summary>
        public static string Tooltip(ItemDrop.ItemData item)
        {
            string text = GetText(item);
            if (string.IsNullOrEmpty(text))
            {
                return "\n\n<color=grey>Blank - use it to write.</color>";
            }
            item.m_customData.TryGetValue(AuthorKey, out var author);
            item.m_customData.TryGetValue(DayKey, out var day);
            item.m_customData.TryGetValue(FromKey, out var from);
            string who = string.IsNullOrEmpty(author) ? "" : $"From {author}";
            if (!string.IsNullOrEmpty(from))
            {
                who += $" @ {from}";
            }
            string when = string.IsNullOrEmpty(day) ? "" : $"Day {day}";
            string header = string.Join(", ", new[] { who, when }.Where(s => s.Length > 0));
            return $"\n\n<color=orange>{header}</color>\n<color=#e8dcc0>{Escape(text)}</color>\n\n<color=grey>Use it to edit.</color>";
        }

        // Stop players' text from opening rich-text tags in the tooltip.
        private static string Escape(string text) => text.Replace("<", "<​");

        /// <summary>Turns a received raven letter into a Letter item in the player's bag.</summary>
        public static bool GiveCopy(Player player, Letter letter)
        {
            var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(PrefabName) : null;
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!player || !drop)
            {
                return false;
            }
            var item = drop.m_itemData.Clone();
            item.m_stack = 1;
            item.m_dropPrefab = prefab;
            item.m_customData[TextKey] = letter.Text ?? "";
            item.m_customData[AuthorKey] = letter.FromPlayer ?? "";
            item.m_customData[FromKey] = letter.From ?? "";
            item.m_customData[DayKey] = RavenMail.Day(letter.SentAt).ToString();

            var inventory = player.GetInventory();
            if (!inventory.CanAddItem(item) || !inventory.AddItem(item))
            {
                player.Message(MessageHud.MessageType.Center, "Your bag is full");
                return false;
            }
            player.Message(MessageHud.MessageType.Center, "Letter added to your bag");
            return true;
        }

        /// <summary>Opens the text box for a letter in the player's inventory.</summary>
        public static void OpenWriter(Player player, ItemDrop.ItemData item)
        {
            if (!TextInput.instance)
            {
                return;
            }
            // The inventory window closes on E/Tab, which would happen while typing; reopen it afterwards.
            bool reopen = InventoryGui.IsVisible();
            if (reopen)
            {
                InventoryGui.instance.Hide();
            }
            TextInput.instance.RequestText(new LetterWriter(player, item, reopen),
                "Write your letter  (\\n = new line)", MaxLength);
        }

        private class LetterWriter : TextReceiver
        {
            private readonly Player _player;
            private readonly ItemDrop.ItemData _item;
            private readonly bool _reopen;

            public LetterWriter(Player player, ItemDrop.ItemData item, bool reopen)
            {
                _player = player;
                _item = item;
                _reopen = reopen;
            }

            public string GetText() => LetterItem.GetText(_item).Replace("\n", "\\n");

            public void SetText(string text)
            {
                if (!_player || !_player.GetInventory().ContainsItem(_item))
                {
                    return; // dropped or moved while typing
                }
                LetterItem.SetText(_item, text, _player.GetPlayerName());
                _player.Message(MessageHud.MessageType.Center, string.IsNullOrEmpty(text) ? "Letter cleared" : "Letter written");
                if (_reopen && InventoryGui.instance)
                {
                    InventoryGui.instance.Show(null);
                }
            }
        }

        /// <summary>A small parchment scroll with ink lines and a wax seal, drawn in code.</summary>
        private static Sprite CreateIcon()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            var paper = new Color(0.87f, 0.78f, 0.58f);
            var paperEdge = new Color(0.62f, 0.50f, 0.32f);
            var roll = new Color(0.55f, 0.41f, 0.24f);
            var rollLight = new Color(0.72f, 0.57f, 0.36f);
            var ink = new Color(0.25f, 0.17f, 0.10f);
            var wax = new Color(0.66f, 0.10f, 0.08f);
            var waxLight = new Color(0.85f, 0.25f, 0.18f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = clear;
                    bool inPaper = x >= 12 && x <= 51 && y >= 9 && y <= 54;
                    if (inPaper)
                    {
                        c = (x == 12 || x == 51) ? paperEdge : paper;
                        // ink lines
                        if (x >= 17 && x <= 46 && (y == 42 || y == 36 || y == 30 || y == 24) && !(y == 24 && x > 36))
                        {
                            c = ink;
                        }
                    }
                    // rolled ends at top and bottom
                    if (x >= 8 && x <= 55 && ((y >= 52 && y <= 58) || (y >= 5 && y <= 11)))
                    {
                        bool top = y >= 52;
                        int mid = top ? 55 : 8;
                        c = Mathf.Abs(y - mid) <= 1 ? rollLight : roll;
                    }
                    // wax seal
                    float dx = x - 44, dy = y - 17;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= 6.5f)
                    {
                        c = d <= 3f ? waxLight : wax;
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
