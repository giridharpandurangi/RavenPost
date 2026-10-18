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
