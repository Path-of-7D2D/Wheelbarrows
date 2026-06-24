using HarmonyLib;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    /// <summary>
    /// Replaces the wheelbarrow's "ride" interaction with "push": pressing the
    /// interact key on it starts push mode (and pressing it again drops the cart)
    /// instead of mounting it like a vehicle. All other vehicle commands (storage,
    /// take, repair, lock...) and all other vehicles are left untouched.
    /// </summary>
    [Preserve]
    [HarmonyPatch(typeof(EntityVehicle), nameof(EntityVehicle.OnEntityActivated))]
    internal static class EntityVehicleActivatePatch
    {
        [Preserve]
        private static bool Prefix(EntityVehicle __instance, EntityActivationCommand _command, EntityPlayerLocal _playerFocusing)
        {
            if (__instance == null || _playerFocusing == null)
            {
                return true;
            }

            int wheelbarrowId = EntityClass.GetId(WheelbarrowVisuals.EntityName);
            if (!WheelbarrowVisuals.IsWheelbarrowEntityClass(__instance.entityClass, wheelbarrowId))
            {
                return true; // not our cart — run the normal vehicle handler
            }

            string commandId = _command.commandId;
            if (commandId != "ride" && commandId != "drive")
            {
                return true; // storage / take / repair / lock etc. behave normally
            }

            WheelbarrowPush.Toggle(__instance);
            return false; // skip mounting
        }
    }
}
