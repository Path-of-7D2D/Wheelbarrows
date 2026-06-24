using HarmonyLib;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    /// <summary>
    /// While the local player is pushing a wheelbarrow, restrict their input the way
    /// mounting a vehicle does: no jumping, no swapping the held item, no attacking.
    /// They keep normal walking/look so they can steer the cart on foot.
    /// </summary>
    internal static class PushLock
    {
        internal static bool LocksLocalPlayer => WheelbarrowPush.IsActive;
    }

    // Jumping: MoveByInput consumes movementInput.jump — clear it before it's read.
    [Preserve]
    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.MoveByInput))]
    internal static class Push_BlockJump_Patch
    {
        [Preserve]
        private static void Prefix(EntityPlayerLocal __instance)
        {
            if (PushLock.LocksLocalPlayer && __instance != null && __instance.movementInput != null)
            {
                __instance.movementInput.jump = false;
            }
        }
    }

    // Attacking (left click): Attack(0) is the primary swing/fire entry.
    [Preserve]
    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.Attack))]
    internal static class Push_BlockAttack_Patch
    {
        [Preserve]
        private static bool Prefix(EntityAlive __instance, ref bool __result)
        {
            if (PushLock.LocksLocalPlayer && __instance is EntityPlayerLocal)
            {
                __result = false;
                return false; // skip the attack
            }

            return true;
        }
    }

    // Toolbelt / held-item switching: SetHoldingItemIdx is the funnel for "what's in
    // your hands", so blocking it stops number-key/scroll equips while pushing.
    [Preserve]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.SetHoldingItemIdx))]
    internal static class Push_BlockHold_Patch
    {
        [Preserve]
        private static bool Prefix(Inventory __instance)
        {
            return !(PushLock.LocksLocalPlayer && __instance != null && __instance.entity is EntityPlayerLocal);
        }
    }

    [Preserve]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.SetHoldingItemIdxNoHolsterTime))]
    internal static class Push_BlockHoldNoHolster_Patch
    {
        [Preserve]
        private static bool Prefix(Inventory __instance)
        {
            return !(PushLock.LocksLocalPlayer && __instance != null && __instance.entity is EntityPlayerLocal);
        }
    }
}
