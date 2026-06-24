using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    internal static class WheelbarrowVisuals
    {
        internal const string EntityName = "vehicleWheelbarrow";

        internal static int RepairActiveWheelbarrows(bool logDetails)
        {
            VehicleManager manager = VehicleManager.Instance;
            if (manager == null)
            {
                return 0;
            }

            int entityClassId = EntityClass.GetId(EntityName);
            if (entityClassId <= 0)
            {
                return 0;
            }

            int repaired = 0;
            for (int i = 0; i < manager.vehiclesActive.Count; i++)
            {
                EntityVehicle vehicle = manager.vehiclesActive[i];
                if (vehicle != null && IsWheelbarrowEntityClass(vehicle.entityClass, entityClassId))
                {
                    if (RepairVehicle(vehicle, logDetails))
                    {
                        repaired++;
                    }
                }
            }

            return repaired;
        }

        internal static bool RepairVehicle(EntityVehicle vehicle, bool logDetails)
        {
            if (vehicle == null)
            {
                return false;
            }

            Transform root = vehicle.RootTransform;
            Transform model = vehicle.ModelTransform;
            Transform physics = vehicle.PhysicsTransform;
            if (root == null || model == null)
            {
                if (logDetails)
                {
                    Log.Out("[Wheelbarrow] Visual repair skipped for id {0}: root={1}, model={2}, physics={3}",
                        vehicle.entityId,
                        FormatTransform(root),
                        FormatTransform(model),
                        FormatTransform(physics));
                }

                return false;
            }

            root.gameObject.SetActive(true);
            model.gameObject.SetActive(true);

            Transform mesh = model.Find("Mesh");
            if (mesh != null)
            {
                mesh.gameObject.SetActive(true);
            }

            if (vehicle.emodel != null)
            {
                vehicle.emodel.SetVisible(true, true);
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            int missingMaterialCount = 0;
            Bounds? bounds = null;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                EnableParents(renderer.transform, model);
                renderer.gameObject.layer = 0;
                renderer.enabled = true;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    missingMaterialCount++;
                }
                else
                {
                    for (int j = 0; j < materials.Length; j++)
                    {
                        if (materials[j] == null)
                        {
                            missingMaterialCount++;
                        }
                    }
                }

                if (!bounds.HasValue)
                {
                    bounds = renderer.bounds;
                }
                else
                {
                    Bounds combined = bounds.Value;
                    combined.Encapsulate(renderer.bounds);
                    bounds = combined;
                }

                enabledRenderers++;
            }

            if (logDetails)
            {
                string meshPath = mesh != null ? GetPath(mesh, root) : "<missing>";
                string boundsText = bounds.HasValue
                    ? "center=" + Format(bounds.Value.center + Origin.position) + ", size=" + Format(bounds.Value.size)
                    : "<none>";

                Log.Out("[Wheelbarrow] Visual state id {0}: root={1}, model={2}, mesh={3}, physics={4}, renderers={5}, missingMaterials={6}, bounds={7}",
                    vehicle.entityId,
                    FormatTransform(root),
                    FormatTransform(model),
                    meshPath,
                    FormatTransform(physics),
                    enabledRenderers,
                    missingMaterialCount,
                    boundsText);
            }

            return enabledRenderers > 0;
        }

        internal static List<EntityVehicle> GetActiveWheelbarrows()
        {
            List<EntityVehicle> results = new List<EntityVehicle>();
            VehicleManager manager = VehicleManager.Instance;
            if (manager == null)
            {
                return results;
            }

            int entityClassId = EntityClass.GetId(EntityName);
            if (entityClassId <= 0)
            {
                return results;
            }

            for (int i = 0; i < manager.vehiclesActive.Count; i++)
            {
                EntityVehicle vehicle = manager.vehiclesActive[i];
                if (vehicle != null && IsWheelbarrowEntityClass(vehicle.entityClass, entityClassId))
                {
                    results.Add(vehicle);
                }
            }

            return results;
        }

        internal static bool IsWheelbarrowEntityClass(int entityClassId, int currentWheelbarrowEntityId)
        {
            if (entityClassId == currentWheelbarrowEntityId)
            {
                return true;
            }

            string entityClassName = EntityClass.GetEntityClassName(entityClassId);
            return EntityName.Equals(entityClassName, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnableParents(Transform transform, Transform stopAt)
        {
            Transform current = transform;
            while (current != null)
            {
                current.gameObject.SetActive(true);
                if (current == stopAt)
                {
                    break;
                }

                current = current.parent;
            }
        }

        private static string FormatTransform(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            return GetPath(transform, null) + " pos=" + Format(transform.position + Origin.position) +
                " local=" + Format(transform.localPosition) +
                " active=" + transform.gameObject.activeInHierarchy +
                " layer=" + transform.gameObject.layer;
        }

        private static string GetPath(Transform transform, Transform relativeRoot)
        {
            if (transform == null)
            {
                return "<null>";
            }

            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null && current != relativeRoot)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static string Format(Vector3 position)
        {
            return position.x.ToString("0.00") + "," +
                position.y.ToString("0.00") + "," +
                position.z.ToString("0.00");
        }
    }

    [Preserve]
    internal sealed class WheelbarrowVisualRepairBehaviour : MonoBehaviour
    {
        private float nextRepairTime;

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextRepairTime)
            {
                return;
            }

            nextRepairTime = Time.realtimeSinceStartup + 1.5f;
            World world = GameManager.Instance?.World;
            if (world == null)
            {
                return;
            }

            WheelbarrowVisuals.RepairActiveWheelbarrows(false);
        }
    }
}
