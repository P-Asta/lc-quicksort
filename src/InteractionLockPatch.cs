using HarmonyLib;
using GameNetcodeStuff;

namespace QuickSort
{
    /// <summary>
    /// During sorting, block user grab/drop interactions to prevent weight/state desync bugs.
    /// </summary>
    internal static class InteractionLockPatch
    {
        [HarmonyPatch(typeof(PlayerControllerB), "BeginGrabObject")]
        [HarmonyPrefix]
        private static bool BlockBeginGrabObject()
        {
            if (!Sorter.IsInteractionLocked) return true;
            // Allow internal forced interactions (if any) to pass.
            if (Sorter.IsInteractionBypassActive) return true;
            // Block normal user grabbing while sorting.
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
        [HarmonyPrefix]
        private static bool BlockDiscardHeldObject()
        {
            if (!Sorter.IsInteractionLocked) return true;
            if (Sorter.IsInteractionBypassActive) return true;
            // Block dropping while sorting.
            return false;
        }
    }
}

