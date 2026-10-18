using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace RavenPost
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.ravenpost.valheim";
        public const string ModName = "Raven Post";
        public const string ModVersion = "0.2.1";

        public const string PerchPrefabName = "piece_ravenperch";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> SecondsPer100m;
        internal static ConfigEntry<float> MinFlightSeconds;
        internal static ConfigEntry<float> WeightSlowdown;
        internal static ConfigEntry<bool> AllowNonTeleportable;
        internal static ConfigEntry<int> CargoWidth;
        internal static ConfigEntry<int> CargoHeight;
        internal static ConfigEntry<KeyCode> NextDestinationKey;
        internal static ConfigEntry<KeyCode> SendKey;
        internal static ConfigEntry<KeyCode> RenameKey;

        private Harmony _harmony;
        private float _nextNotifyCheck;

        private void Awake()
        {
            Log = Logger;

            SecondsPer100m = Config.Bind("Flight", "SecondsPer100m", 10f,
                "Real seconds a raven needs to fly 100 metres (in-game time, so sleeping speeds it up).");
            MinFlightSeconds = Config.Bind("Flight", "MinFlightSeconds", 10f,
                "Shortest possible delivery time, in seconds.");
            WeightSlowdown = Config.Bind("Flight", "WeightSlowdown", 0.5f,
                "Extra flight time per 100 weight of cargo. 0.5 = +50% per 100 weight.");
            AllowNonTeleportable = Config.Bind("Cargo", "AllowNonTeleportable", true,
                "Let ravens carry ore and metals that portals block.");
            CargoWidth = Config.Bind("Cargo", "CargoWidth", 2,
                "Cargo slots per row. Applies to perches after restarting the game.");
            CargoHeight = Config.Bind("Cargo", "CargoHeight", 2,
                "Cargo rows. Applies to perches after restarting the game.");
            NextDestinationKey = Config.Bind("Keys", "NextDestination", KeyCode.J,
                "While looking at a perch: choose the next destination.");
            SendKey = Config.Bind("Keys", "Send", KeyCode.K,
                "While looking at a perch: send its cargo.");
            RenameKey = Config.Bind("Keys", "Rename", KeyCode.N,
                "While looking at a perch: rename it.");

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
            container.m_width = Mathf.Clamp(CargoWidth.Value, 1, 8);
            container.m_height = Mathf.Clamp(CargoHeight.Value, 1, 4);

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
        }

        private void Update()
        {
            SendWindow.Tick();

            if (!PostOffice.IsActive)
            {
                return;
            }

            if (Time.time >= _nextNotifyCheck)
            {
                _nextNotifyCheck = Time.time + 1f;
                PostOffice.NotifyArrivals();
            }

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
