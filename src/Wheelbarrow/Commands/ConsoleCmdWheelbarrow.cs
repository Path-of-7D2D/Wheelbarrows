using System;
using System.Collections.Generic;
using Platform;
using UnityEngine;
using UnityEngine.Scripting;

namespace Wheelbarrow.Commands
{
    [Preserve]
    public class ConsoleCmdWheelbarrow : ConsoleCmdAbstract
    {
        private const string EntityName = "vehicleWheelbarrow";
        private const string ItemName = "vehicleWheelbarrowPlaceable";
        private const float DefaultDistance = 4.5f;

        public override bool IsExecuteOnClient => true;

        public override DeviceFlag AllowedDeviceTypesClient =>
            DeviceFlag.StandaloneWindows | DeviceFlag.StandaloneLinux | DeviceFlag.StandaloneOSX;

        public override string[] getCommands()
        {
            return new[] { "wheelbarrow", "spawnwheelbarrow", "wbspawn", "wb" };
        }

        public override string getDescription()
        {
            return "Spawns or removes test wheelbarrows.";
        }

        public override string getHelp()
        {
            return "Usage:\n" +
                "  wb [distance]      spawn a test cart\n" +
                "  wb push [offset] [lift] [tilt]   push nearest cart (walk-behind)\n" +
                "  wb hands x y z     tune grip rotation while pushing\n" +
                "  wb handpos x y z   tune grip-local hand offset while pushing\n" +
                "  wb drop            release the pushed cart\n" +
                "  wb cleanup\n" +
                "  wb debug\n" +
                "  wheelbarrow [distance]\n" +
                "\n" +
                "Examples:\n" +
                "  wb\n" +
                "  wb push\n" +
                "  wb push 1.25 0.2 15\n" +
                "  wb hands 8 170 14\n" +
                "  wb handpos 0.05 0.03 -0.08\n" +
                "  wb drop\n" +
                "  wb cleanup";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params.Count > 0 && IsSubcommand(_params[0], "help"))
            {
                Output(getHelp());
                return;
            }

            if (_params.Count > 0 && IsCleanupSubcommand(_params[0]))
            {
                CleanupWheelbarrows();
                return;
            }

            if (_params.Count > 0 && IsSubcommand(_params[0], "debug"))
            {
                DebugWheelbarrows();
                return;
            }

            if (_params.Count > 0 && (IsSubcommand(_params[0], "push") || IsSubcommand(_params[0], "grab")))
            {
                PushNearest(_senderInfo, _params);
                return;
            }

            if (_params.Count > 0 && (IsSubcommand(_params[0], "drop") || IsSubcommand(_params[0], "release") || IsSubcommand(_params[0], "park")))
            {
                if (WheelbarrowPush.IsActive)
                {
                    WheelbarrowPush.Release();
                    Output("Dropped the wheelbarrow.");
                }
                else
                {
                    Output("No wheelbarrow is being pushed.");
                }

                return;
            }

            if (_params.Count > 0 && (IsSubcommand(_params[0], "hands") || IsSubcommand(_params[0], "handpos")))
            {
                bool pos = IsSubcommand(_params[0], "handpos");
                if (_params.Count >= 4 &&
                    float.TryParse(_params[1], out float hx) &&
                    float.TryParse(_params[2], out float hy) &&
                    float.TryParse(_params[3], out float hz))
                {
                    if (pos)
                    {
                        WheelbarrowPush.HandOffset = new Vector3(hx, hy, hz);
                    }
                    else
                    {
                        WheelbarrowPush.HandEuler = new Vector3(hx, hy, hz);
                    }

                    WheelbarrowPush.RefreshHandIK();
                }

                Output("Hand rot=" + Format(WheelbarrowPush.HandEuler) + " offset=" + Format(WheelbarrowPush.HandOffset) +
                    (WheelbarrowPush.IsActive ? "" : " (push a cart to see it update)"));
                return;
            }

            float distance = DefaultDistance;
            if (_params.Count > 0 && !float.TryParse(_params[0], out distance))
            {
                Output("Invalid distance: " + _params[0]);
                Output(getHelp());
                return;
            }

            distance = Mathf.Clamp(distance, 2.5f, 12f);

            EntityPlayer player = GetSenderPlayer(_senderInfo);
            if (player == null)
            {
                Output("No player entity is available. Run this from an in-game client console.");
                return;
            }

            int entityId = EntityClass.GetId(EntityName);
            if (entityId <= 0)
            {
                Output("Could not find entity class '" + EntityName + "'. Check that the wheelbarrow XML loaded.");
                return;
            }

            ItemValue itemValue = ItemClass.GetItem(ItemName, _caseInsensitive: true);
            if (itemValue == null || itemValue.type == 0)
            {
                Output("Could not find item '" + ItemName + "'. Check that the wheelbarrow item XML loaded.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(player, distance);
            Vector3 spawnRotation = new Vector3(0f, player.rotation.y + 90f, 0f);

            if (!VehicleManager.CanAddMoreVehicles())
            {
                Output("The world is at the vehicle limit. Pick up or remove a vehicle and try again.");
                return;
            }

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageVehicleSpawn>().Setup(
                        entityId,
                        spawnPosition,
                        spawnRotation,
                        itemValue.Clone(),
                        player.entityId),
                    _flush: true);

                Output("Requested wheelbarrow spawn at " + Format(spawnPosition) + ".");
                return;
            }

            Entity entity = EntityFactory.CreateEntity(entityId, spawnPosition, spawnRotation);
            if (entity == null)
            {
                Output("EntityFactory returned null for '" + EntityName + "'.");
                return;
            }

            entity.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);

            EntityVehicle vehicle = entity as EntityVehicle;
            if (vehicle != null)
            {
                Vehicle vehicleData = vehicle.GetVehicle();
                if (vehicleData != null)
                {
                    vehicleData.SetItemValue(itemValue);
                }

                vehicle.SetOwner(GetOwner(player));
            }

            GameManager.Instance.World.SpawnEntityInWorld(entity);
            if (vehicle != null)
            {
                WheelbarrowVisuals.RepairVehicle(vehicle, true);
            }

            SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
                NetPackageManager.GetPackage<NetPackageVehicleCount>().Setup());

            Output("Spawned wheelbarrow at " + Format(spawnPosition) + ".");
        }

        private static void CleanupWheelbarrows()
        {
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                Output("Cleanup must run on the server. In single-player, run it from the in-game console after loading the world.");
                return;
            }

            World world = GameManager.Instance?.World;
            VehicleManager manager = VehicleManager.Instance;
            if (world == null || manager == null)
            {
                Output("World or VehicleManager is not available yet.");
                return;
            }

            int entityId = EntityClass.GetId(EntityName);
            if (entityId <= 0)
            {
                Output("Could not find entity class '" + EntityName + "'. Check that the wheelbarrow XML loaded.");
                return;
            }

            List<int> activeEntityIds = new List<int>();
            for (int i = 0; i < manager.vehiclesActive.Count; i++)
            {
                EntityVehicle vehicle = manager.vehiclesActive[i];
                if (vehicle != null && WheelbarrowVisuals.IsWheelbarrowEntityClass(vehicle.entityClass, entityId))
                {
                    activeEntityIds.Add(vehicle.entityId);
                }
            }

            int removedActive = 0;
            for (int i = 0; i < activeEntityIds.Count; i++)
            {
                Entity entity = world.GetEntity(activeEntityIds[i]);
                if (entity != null)
                {
                    world.RemoveEntity(entity.entityId, EnumRemoveEntityReason.Killed);
                    removedActive++;
                }
            }

            int removedUnloaded = 0;
            for (int i = manager.vehiclesUnloaded.Count - 1; i >= 0; i--)
            {
                EntityCreationData vehicleData = manager.vehiclesUnloaded[i];
                if (vehicleData != null && WheelbarrowVisuals.IsWheelbarrowEntityClass(vehicleData.entityClass, entityId))
                {
                    manager.vehiclesUnloaded.RemoveAt(i);
                    removedUnloaded++;
                }
            }

            if (removedActive > 0 || removedUnloaded > 0)
            {
                manager.TriggerSave();
                manager.UpdateVehicleWaypoints();
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
                    NetPackageManager.GetPackage<NetPackageVehicleCount>().Setup());
                manager.Save();
                manager.WaitOnSave();
            }

            Output("Removed " + removedActive + " active and " + removedUnloaded + " unloaded wheelbarrow record(s).");
        }

        private static void DebugWheelbarrows()
        {
            List<EntityVehicle> wheelbarrows = WheelbarrowVisuals.GetActiveWheelbarrows();
            if (wheelbarrows.Count == 0)
            {
                Output("No active wheelbarrows found.");
                return;
            }

            int repaired = 0;
            for (int i = 0; i < wheelbarrows.Count; i++)
            {
                if (WheelbarrowVisuals.RepairVehicle(wheelbarrows[i], true))
                {
                    repaired++;
                }
            }

            Output("Debugged " + wheelbarrows.Count + " active wheelbarrow(s); renderers found on " + repaired + ".");
        }

        private void PushNearest(CommandSenderInfo senderInfo, List<string> _params)
        {
            EntityPlayer player = GetSenderPlayer(senderInfo);
            if (player == null)
            {
                Output("No player entity is available. Run this from an in-game client console.");
                return;
            }

            float offset = WheelbarrowPush.DefaultFrontOffset;
            if (_params.Count > 1 && !float.TryParse(_params[1], out offset))
            {
                Output("Invalid offset: " + _params[1]);
                return;
            }

            float lift = WheelbarrowPush.DefaultHeightLift;
            if (_params.Count > 2 && !float.TryParse(_params[2], out lift))
            {
                Output("Invalid lift: " + _params[2]);
                return;
            }

            float tilt = WheelbarrowPush.DefaultTiltDegrees;
            if (_params.Count > 3 && !float.TryParse(_params[3], out tilt))
            {
                Output("Invalid tilt: " + _params[3]);
                return;
            }

            offset = Mathf.Clamp(offset, 0.6f, 3f);
            lift = Mathf.Clamp(lift, -0.2f, 1.2f);
            tilt = Mathf.Clamp(tilt, 0f, 45f);

            List<EntityVehicle> wheelbarrows = WheelbarrowVisuals.GetActiveWheelbarrows();
            EntityVehicle nearest = null;
            float nearestSq = float.MaxValue;
            for (int i = 0; i < wheelbarrows.Count; i++)
            {
                EntityVehicle candidate = wheelbarrows[i];
                if (candidate == null)
                {
                    continue;
                }

                float distSq = (candidate.position - player.position).sqrMagnitude;
                if (distSq < nearestSq)
                {
                    nearestSq = distSq;
                    nearest = candidate;
                }
            }

            if (nearest == null)
            {
                Output("No wheelbarrow found nearby. Spawn one with 'wb' first.");
                return;
            }

            WheelbarrowPush.Begin(nearest, offset, lift, tilt);
            Output("Now pushing (offset " + offset.ToString("0.0") + "m, lift " + lift.ToString("0.00") +
                "m, tilt " + tilt.ToString("0") + "°). Walk around; 'wb drop' to release.");
        }

        private static EntityPlayer GetSenderPlayer(CommandSenderInfo senderInfo)
        {
            World world = GameManager.Instance?.World;
            if (world == null)
            {
                return null;
            }

            if (senderInfo.RemoteClientInfo != null)
            {
                return world.GetEntity(senderInfo.RemoteClientInfo.entityId) as EntityPlayer;
            }

            if (!GameManager.IsDedicatedServer)
            {
                return world.GetPrimaryPlayer();
            }

            return null;
        }

        private static Vector3 GetSpawnPosition(EntityPlayer player, float distance)
        {
            Vector3 forward = Quaternion.Euler(0f, player.rotation.y, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            Vector3 position = player.position + forward.normalized * distance;
            position.y = player.position.y;
            SnapToGround(ref position);
            return position;
        }

        private static void SnapToGround(ref Vector3 position)
        {
            Vector3 rayOrigin = position - Origin.position + Vector3.up * 6f;
            RaycastHit hit;
            if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out hit,
                18f,
                ~0,
                QueryTriggerInteraction.Ignore))
            {
                position.y = hit.point.y + Origin.position.y + 0.08f;
            }
        }

        private static PlatformUserIdentifierAbs GetOwner(EntityPlayer player)
        {
            ClientInfo clientInfo = SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForEntityId(player.entityId);
            return clientInfo != null ? clientInfo.InternalId : PlatformManager.InternalLocalUserIdentifier;
        }

        private static bool IsSubcommand(string value, string expected)
        {
            return value.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCleanupSubcommand(string value)
        {
            return IsSubcommand(value, "cleanup") ||
                IsSubcommand(value, "clean") ||
                IsSubcommand(value, "clear") ||
                IsSubcommand(value, "remove");
        }

        private static string Format(Vector3 position)
        {
            return position.x.ToString("0.0") + ", " + position.y.ToString("0.0") + ", " + position.z.ToString("0.0");
        }

        private static void Output(string message)
        {
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output("[Wheelbarrow] " + message);
        }
    }
}
