using HarmonyLib;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    /// <summary>
    /// While the local player is pushing a wheelbarrow, restrict their input the way
    /// mounting a vehicle does: no jumping and no attacking. Slot switching drops
    /// the cart first, then lets vanilla inventory code complete normally.
    /// </summary>
    internal static class PushLock
    {
        internal static bool LocksLocalPlayer(EntityPlayerLocal player)
        {
            return player != null && WheelbarrowPush.ShouldLockLocalPlayer(player);
        }
    }

    // Jumping: MoveByInput consumes movementInput.jump — clear it before it's read.
    [Preserve]
    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.MoveByInput))]
    internal static class Push_BlockJump_Patch
    {
        [Preserve]
        private static void Prefix(EntityPlayerLocal __instance)
        {
            if (PushLock.LocksLocalPlayer(__instance) && __instance.movementInput != null)
            {
                __instance.movementInput.jump = false;
                WheelbarrowPush.ApplyMovementPenalty(__instance);
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
            EntityPlayerLocal player = __instance as EntityPlayerLocal;
            if (PushLock.LocksLocalPlayer(player))
            {
                __result = false;
                return false; // skip the attack
            }

            return true;
        }
    }

    // Toolbelt / held-item switching: release the cart and let vanilla complete the
    // slot transition. Blocking this method can strand the holster/unholster state.
    [Preserve]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.SetHoldingItemIdx))]
    internal static class Push_BlockHold_Patch
    {
        [Preserve]
        private static bool Prefix(Inventory __instance)
        {
            EntityPlayerLocal player = __instance != null ? __instance.entity as EntityPlayerLocal : null;
            if (PushLock.LocksLocalPlayer(player))
            {
                WheelbarrowPush.Release();
            }

            return true;
        }
    }

    [Preserve]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.SetHoldingItemIdxNoHolsterTime))]
    internal static class Push_BlockHoldNoHolster_Patch
    {
        [Preserve]
        private static bool Prefix(Inventory __instance)
        {
            EntityPlayerLocal player = __instance != null ? __instance.entity as EntityPlayerLocal : null;
            if (PushLock.LocksLocalPlayer(player))
            {
                WheelbarrowPush.Release();
            }

            return true;
        }
    }
}
