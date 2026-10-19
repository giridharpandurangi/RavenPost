using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace RavenPost
{
    /// <summary>One upgrade level of a perch, following the biome progression.</summary>
    internal class PerchTier
    {
        public int Index;
        public string Name;
        public string Biome;
        public ConfigEntry<string> Cost;
        public ConfigEntry<int> Width;
        public ConfigEntry<int> Height;
        public ConfigEntry<float> Speed;

        public float SpeedMultiplier => Mathf.Max(0.1f, Speed.Value);
        public int CargoWidth => Mathf.Clamp(Width.Value, 1, 8);
        public int CargoHeight => Mathf.Clamp(Height.Value, 1, 10);

        /// <summary>Upgrade cost as (item prefab name, amount). Empty for the base tier.</summary>
        public List<KeyValuePair<string, int>> Requirements()
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var part in (Cost.Value ?? "").Split(','))
            {
                var bits = part.Split(':');
                if (bits.Length == 2 && int.TryParse(bits[1].Trim(), out int amount) && amount > 0 && bits[0].Trim().Length > 0)
                {
                    list.Add(new KeyValuePair<string, int>(bits[0].Trim(), amount));
                }
            }
            return list;
        }

        /// <summary>Requirement prefab names that don't exist in this game version.</summary>
        public List<string> MissingItems() =>
            Requirements().Select(r => r.Key).Where(name => PerchTiers.ItemData(name) == null).ToList();
    }

    internal static class PerchTiers
    {
        public const string TierKey = "ravenpost_tier";

        private static readonly List<PerchTier> s_tiers = new List<PerchTier>();

        public static IReadOnlyList<PerchTier> All => s_tiers;

        public static PerchTier Get(int index) => s_tiers[Mathf.Clamp(index, 0, s_tiers.Count - 1)];

        public static int Max => s_tiers.Count - 1;

        public static void Bind(ConfigFile config)
        {
            Add(config, "Wooden Perch", "Meadows", "", 2, 2, 1.0f);
            Add(config, "Bronze Perch", "Black Forest", "Bronze:5,BronzeNails:10", 3, 2, 1.2f);
            Add(config, "Iron Perch", "Swamp", "Iron:10,Guck:5", 4, 2, 1.4f);
            Add(config, "Silver Perch", "Mountains", "Silver:10,WolfPelt:4", 5, 3, 1.7f);
            Add(config, "Black Metal Perch", "Plains", "BlackMetal:10,LinenThread:10", 6, 3, 2.0f);
            Add(config, "Eitr Perch", "Mistlands", "BlackCore:2,Eitr:10,Carapace:10", 7, 4, 2.5f);
            Add(config, "Flametal Perch", "Ashlands", "FlametalNew:10,CelestialFeather:3", 8, 4, 3.0f);
            // Deep North materials aren't in the game yet; Ashlands end-game items stand in. Change in the config later.
            Add(config, "Deep North Perch", "Deep North", "FlametalNew:30,MoltenCore:2", 8, 6, 4.0f);
        }

        private static void Add(ConfigFile config, string name, string biome, string cost, int w, int h, float speed)
        {
            int index = s_tiers.Count;
            string section = $"Tier {index} - {name}";
            s_tiers.Add(new PerchTier
            {
                Index = index,
                Name = name,
                Biome = biome,
                Cost = config.Bind(section, "UpgradeCost", cost,
                    Plugin.Synced(index == 0 ? "Not used: this is the tier you build." : "Items to upgrade to this tier, as PrefabName:Amount, comma-separated.")),
                Width = config.Bind(section, "CargoWidth", w, Plugin.Synced("Cargo slots per row (max 8).")),
                Height = config.Bind(section, "CargoHeight", h, Plugin.Synced("Cargo rows (scrolls when tall).")),
                Speed = config.Bind(section, "Speed", speed, Plugin.Synced("Flight speed multiplier; multiplies with the meal's speed.")),
            });
        }

        /// <summary>Logs any upgrade materials this game version doesn't have, so the config can be fixed.</summary>
        public static void ValidateItems()
        {
            foreach (var tier in s_tiers.Skip(1))
            {
                var missing = tier.MissingItems();
                if (missing.Count > 0)
                {
                    Plugin.Log.LogWarning($"{tier.Name}: unknown upgrade item(s) {string.Join(", ", missing)} - " +
                                          $"this tier can't be reached until its UpgradeCost is changed in the config.");
                }
            }
        }

        public static ItemDrop.ItemData ItemData(string prefabName)
        {
            if (!ObjectDB.instance)
            {
                return null;
            }
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            return drop ? drop.m_itemData : null;
        }

        public static string DisplayName(string prefabName)
        {
            var data = ItemData(prefabName);
            return data != null ? Localization.instance.Localize(data.m_shared.m_name) : prefabName;
        }
    }
}
