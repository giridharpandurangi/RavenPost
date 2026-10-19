using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace RavenPost
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.ravenpost.valheim";
        public const string ModName = "Raven Post";
        public const string ModVersion = "0.7.0";

        public const string PerchPrefabName = "piece_ravenperch";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> SecondsPer100m;
        internal static ConfigEntry<float> MinFlightSeconds;
        internal static ConfigEntry<float> WeightSlowdown;
        internal static ConfigEntry<bool> AllowNonTeleportable;
        internal static ConfigEntry<float> MaxFoodSpeed;
        internal enum HungryBehaviour { FlySlowly, Refuse }

        internal static ConfigEntry<bool> UseFood;
        internal static ConfigEntry<HungryBehaviour> Hungry;
        internal static ConfigEntry<float> HungrySpeed;
        internal static ConfigEntry<int> MaxMeals;
        internal static ConfigEntry<float> RawMeatSpeed;
        internal static ConfigEntry<string> RawMeatItems;
        internal static ConfigEntry<KeyCode> NextDestinationKey;
        internal static ConfigEntry<KeyCode> SendKey;
        internal static ConfigEntry<KeyCode> RenameKey;

        private Harmony _harmony;

        /// <summary>Gameplay settings come from the server so everyone plays by the same rules.</summary>
        internal static ConfigDescription Synced(string description) =>
            new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdminOnly = true });

        private void Awake()
        {
            Log = Logger;

            SecondsPer100m = Config.Bind("Flight", "SecondsPer100m", 10f,
                Synced("Real seconds a raven needs to fly 100 metres (in-game time, so sleeping speeds it up)."));
            MinFlightSeconds = Config.Bind("Flight", "MinFlightSeconds", 5f,
                Synced("Shortest possible delivery time, in seconds."));
            WeightSlowdown = Config.Bind("Flight", "WeightSlowdown", 0.5f,
                Synced("Extra flight time per 100 weight of cargo. 0.5 = +50% per 100 weight."));
            AllowNonTeleportable = Config.Bind("Cargo", "AllowNonTeleportable", true,
                Synced("Let ravens carry ore and metals that portals block."));
            UseFood = Config.Bind("Food", "UseFood", true,
                Synced("Ravens eat one stored meal per flight; better food flies faster. Turn off to ignore food entirely."));
            Hungry = Config.Bind("Food", "HungryRaven", HungryBehaviour.FlySlowly,
                Synced("What a raven does with no meals stored: FlySlowly (at HungrySpeed) or Refuse to fly."));
            HungrySpeed = Config.Bind("Food", "HungrySpeed", 0.5f,
                Synced("Speed multiplier for a hungry raven when HungryRaven = FlySlowly. 0.5 = takes twice as long."));
            MaxMeals = Config.Bind("Food", "MaxMeals", 10,
                Synced("How many meals a perch can store."));
            MaxFoodSpeed = Config.Bind("Food", "MaxFoodSpeed", 4f,
                Synced("Fastest a meal can make a raven. Cooked food gives 1 + health / 50, e.g. 30 hp = x1.6, 75 hp = x2.5."));
            RawMeatSpeed = Config.Bind("Food", "RawMeatSpeed", 1.25f,
                Synced("Speed multiplier for raw meat."));
            RawMeatItems = Config.Bind("Food", "RawMeatItems",
                "RawMeat,DeerMeat,NeckTail,WolfMeat,LoxMeat,SerpentMeat,BugMeat,HareMeat,ChickenMeat,AsksvinMeat,VoltureMeat,BoneMawSerpentMeat",
                Synced("Comma-separated item prefab names ravens eat raw."));
            NextDestinationKey = Config.Bind("Keys", "NextDestination", KeyCode.J,
                "While looking at a perch: choose the next destination.");
            SendKey = Config.Bind("Keys", "Send", KeyCode.K,
                "While looking at a perch: send its cargo.");
            RenameKey = Config.Bind("Keys", "Rename", KeyCode.N,
                "While looking at a perch: rename it.");

            PerchTiers.Bind(Config);

            PrefabManager.OnVanillaPrefabsAvailable += AddPerchPiece;

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }

        private void AddPerchPiece()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPerchPiece;

            var config = new PieceConfig
            {
                Name = "Raven Perch",
                Description = "Ravens carry cargo from here to your other perches.",
                PieceTable = "Hammer",
                Category = "Misc",
                CraftingStation = "piece_workbench",
                Requirements = new[]
                {
                    new RequirementConfig("FineWood", 4),
                    new RequirementConfig("Feathers", 4),
                    new RequirementConfig("BronzeNails", 5),
                },
            };

            var piece = new CustomPiece(PerchPrefabName, "piece_chest_wood", config);
            var prefab = piece.PiecePrefab;

            var container = prefab.GetComponent<Container>();
            container.m_name = "Raven Perch";
            // Each perch resizes to its own tier when it loads (see ContainerAwakePatch).
            container.m_width = PerchTiers.Get(0).CargoWidth;
            container.m_height = PerchTiers.Get(0).CargoHeight;

            prefab.AddComponent<RavenPerch>();

            RavenFlight.Prepare();
            try
            {
                PerchModel.Decorate(prefab);
            }
            catch (System.Exception e)
            {
                Log.LogError($"Couldn't build the perch model, using the plain chest look: {e}");
            }

            PieceManager.Instance.AddPiece(piece);
            LetterItem.Register();
        }

        private void Update()
        {
            SendWindow.Tick();

            if (!PostOffice.IsActive)
            {
                return;
            }

            RavenNet.ServerTick();
            HandleKeys();
        }

        private static void HandleKeys()
        {
            var player = Player.m_localPlayer;
            if (!player || InventoryGui.IsVisible() || Menu.IsVisible() || TextInput.IsVisible() ||
                global::Console.IsVisible() || (Chat.instance && Chat.instance.HasFocus()))
            {
                return;
            }

            var hover = player.GetHoverObject();
            var perch = hover ? hover.GetComponentInParent<RavenPerch>() : null;
            if (!perch)
            {
                return;
            }

            if (ZInput.GetKeyDown(NextDestinationKey.Value, false))
            {
                perch.NextDestination();
            }
            else if (ZInput.GetKeyDown(SendKey.Value, false))
            {
                perch.Send(player);
            }
            else if (ZInput.GetKeyDown(RenameKey.Value, false))
            {
                TextInput.instance.RequestText(perch, "Name this perch", 24);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
