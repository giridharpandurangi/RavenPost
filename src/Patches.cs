using HarmonyLib;

namespace RavenPost
{
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class ContainerHoverTextPatch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            var perch = __instance.GetComponent<RavenPerch>();
            if (perch)
            {
                __result += perch.GetExtraHoverText();
            }
        }
    }

    /// <summary>Registers Raven Post's network messages each time a world starts (host, client or dedicated server).</summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartPatch
    {
        private static void Postfix() => RavenNet.Register();
    }

    /// <summary>Using a letter (hotbar key or right-click in the inventory) opens it for writing.</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class LetterUsePatch
    {
        private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (!LetterItem.IsLetter(item) || !(__instance is Player player) || player != Player.m_localPlayer)
            {
                return true;
            }
            // Only letters the player is carrying; a letter in an open chest should be taken out first.
            if (inventory != null && inventory != player.GetInventory())
            {
                return true;
            }
            LetterItem.OpenWriter(player, item);
            return false;
        }
    }

    /// <summary>Shows a letter's author, day and text in its tooltip.</summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    internal static class LetterTooltipPatch
    {
        private static void Postfix(ItemDrop.ItemData item, ref string __result)
        {
            if (LetterItem.IsLetter(item))
            {
                __result += LetterItem.Tooltip(item);
            }
        }
    }

    /// <summary>Sizes each perch's cargo for its own tier before the container creates its inventory.</summary>
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Prefix(Container __instance)
        {
            if (!__instance.GetComponent<RavenPerch>())
            {
                return;
            }
            var nview = __instance.GetComponent<ZNetView>();
            var zdo = nview ? nview.GetZDO() : null;
            int tier = zdo != null ? zdo.GetInt(PerchTiers.TierKey) : 0;
            RavenPerch.ApplyTier(__instance, PerchTiers.Get(tier));
        }
    }

    /// <summary>Using a food item from the hotbar while looking at a perch feeds the raven.</summary>
    [HarmonyPatch(typeof(Container), nameof(Container.UseItem))]
    internal static class ContainerUseItemPatch
    {
        private static bool Prefix(Container __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            var perch = __instance.GetComponent<RavenPerch>();
            if (!perch || !(user is Player player))
            {
                return true;
            }
            __result = perch.Feed(player, item);
            return false;
        }
    }

    /// <summary>Don't let the player deconstruct a perch that still has deliveries waiting or in flight.</summary>
    [HarmonyPatch(typeof(Container), nameof(Container.CanBeRemoved))]
    internal static class ContainerCanBeRemovedPatch
    {
        private static void Postfix(Container __instance, ref bool __result)
        {
            if (!__result || !__instance.GetComponent<RavenPerch>())
            {
                return;
            }
            var nview = __instance.GetComponent<ZNetView>();
            if (nview && nview.IsValid() && PostOffice.ReadInbox(nview.GetZDO()).Count > 0)
            {
                __result = false;
            }
        }
    }
}
