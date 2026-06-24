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
                if (WheelbarrowPush.IsPushing(__instance))
                {
                    WheelbarrowPush.Release();
                }

                return true; // storage / take / repair / lock etc. behave normally
            }

            if (WheelbarrowPush.IsPushing(__instance))
            {
                WheelbarrowPush.Release();
                return false;
            }

            // Interacting starts pushing. Dropping is handled by the interact key in the
            // push behaviour (the cart is awkward to look-focus once it's in front of you),
            // so this handler is start-only and just consumes the activation otherwise.
            if (!WheelbarrowPush.IsActive && !WheelbarrowPush.JustReleased)
            {
                WheelbarrowPush.Begin(__instance);
            }

            return false; // never mount
        }
    }

    [Preserve]
    [HarmonyPatch(typeof(EntityVehicle), nameof(EntityVehicle.GetActivationText))]
    internal static class EntityVehicleActivationTextPatch
    {
        [Preserve]
        private static void Postfix(EntityVehicle __instance, ref string __result)
        {
            if (__instance == null)
            {
                return;
            }

            int wheelbarrowId = EntityClass.GetId(WheelbarrowVisuals.EntityName);
            if (!WheelbarrowVisuals.IsWheelbarrowEntityClass(__instance.entityClass, wheelbarrowId))
            {
                return;
            }

            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                return;
            }

            string binding = player.playerInput.Activate.GetBindingXuiMarkupString() +
                player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();
            string entityName = GetWheelbarrowDisplayName();
            string template = Localization.Get("wheelbarrowTooltipPush");
            if (string.IsNullOrEmpty(template) || template == "wheelbarrowTooltipPush")
            {
                template = "{0} to Push {1}";
            }

            string text = string.Format(template, binding, entityName);
            if (__instance.IsLockedForLocalPlayer(player))
            {
                text = Localization.Get("ttLocked") + "\n" + text;
            }

            __result = text;
        }

        private static string GetWheelbarrowDisplayName()
        {
            string entityName = Localization.Get(WheelbarrowVisuals.EntityName);
            if (string.IsNullOrEmpty(entityName) || entityName == WheelbarrowVisuals.EntityName)
            {
                return "Wheelbarrow";
            }

            return entityName;
        }
    }
}
