using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    /// <summary>
    /// Prototype of the "walk behind and push" mechanic.
    ///
    /// Instead of mounting the wheelbarrow like a vehicle, the player keeps their
    /// normal on-foot locomotion and the cart is rigidly glued a short distance in
    /// front of them, ground-snapped and facing the player's heading. The vehicle
    /// rigidbody is frozen (kinematic) so the engine's vehicle physics stop fighting
    /// us, the front wheel is spun by distance travelled, the cart is tilted forward
    /// onto its wheel (the natural pushing stance, lifting the rear legs), and the
    /// player's hands are IK-pinned to the handle grips.
    ///
    /// Driven by <c>wb push [offset] [lift] [tilt]</c> / <c>wb drop</c> for now, so the
    /// stance can be dialled in before the mount interaction is replaced (Phase 2).
    /// </summary>
    internal static class WheelbarrowPush
    {
        // Tunables — defaults are starting points; dial in-game via `wb push`.
        internal const float DefaultFrontOffset = 1.6f;    // metres in front of the player (user-confirmed)
        internal const float DefaultHeightLift = 0.2f;     // raise the cart so handles reach hand height
        internal const float DefaultTiltDegrees = 15f;     // forward tilt onto the wheel (rear legs lift)
        internal const float GroundClearance = 0.06f;
        internal const float WheelRadius = 0.32f;          // matches generate_wheelbarrow_model.py
        internal const float YawOffset = 0f;               // cart-forward vs entity-forward (model faces +Z)

        // Hand IK rotation offset applied on top of each grip's orientation (tune if palms look twisted).
        private static readonly Vector3 HandEuler = new Vector3(0f, 0f, 0f);

        internal static EntityVehicle Current { get; private set; }
        internal static float FrontOffset = DefaultFrontOffset;
        internal static float HeightLift = DefaultHeightLift;
        internal static float TiltDegrees = DefaultTiltDegrees;

        private static Vector3 lastCartPos;
        private static bool hasLastPos;
        private static Transform handTargetL;
        private static Transform handTargetR;
        private static bool ikApplied;

        internal static bool IsActive => Current != null;

        internal static void Begin(EntityVehicle vehicle, float frontOffset, float heightLift, float tiltDegrees)
        {
            if (vehicle == null)
            {
                return;
            }

            if (Current != null && Current != vehicle)
            {
                Release();
            }

            Current = vehicle;
            FrontOffset = frontOffset;
            HeightLift = heightLift;
            TiltDegrees = tiltDegrees;
            hasLastPos = false;

            FreezePhysics(vehicle, true);
            SetupHandIK(vehicle);
        }

        internal static void Release()
        {
            EntityVehicle vehicle = Current;
            Current = null;
            hasLastPos = false;

            ClearHandIK();

            if (vehicle != null)
            {
                FreezePhysics(vehicle, false);
            }
        }

        /// <summary>Called every frame (LateUpdate) by the behaviour below.</summary>
        internal static void Tick()
        {
            EntityVehicle vehicle = Current;
            if (vehicle == null || vehicle.IsDead())
            {
                Release();
                return;
            }

            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
            if (player == null)
            {
                return;
            }

            float yaw = player.rotation.y;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            Vector3 target = player.position + forward * FrontOffset;
            SnapToGround(ref target);
            target.y += HeightLift;

            // Yaw to face the player's heading, then tilt forward onto the wheel.
            Quaternion rotation = Quaternion.Euler(0f, yaw + YawOffset, 0f) * Quaternion.Euler(TiltDegrees, 0f, 0f);

            vehicle.SetPosition(target, false);
            vehicle.SetRotation(rotation.eulerAngles);

            // Pin physics + model transforms so nothing visually lags or drifts.
            Vector3 unityPos = target - Origin.position;
            Rigidbody rb = vehicle.vehicleRB;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = unityPos;
                rb.rotation = rotation;
            }

            Transform model = vehicle.ModelTransform;
            if (model != null)
            {
                model.SetPositionAndRotation(unityPos, rotation);
            }

            Transform physics = vehicle.PhysicsTransform;
            if (physics != null)
            {
                physics.SetPositionAndRotation(unityPos, rotation);
            }

            SpinWheel(vehicle, target, forward);

            lastCartPos = target;
            hasLastPos = true;
        }

        private static void SetupHandIK(EntityVehicle vehicle)
        {
            ClearHandIK();

            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
            Transform model = vehicle.ModelTransform;
            if (player == null || model == null)
            {
                return;
            }

            Transform gripL = FindContains(model, "Grip_Left");
            Transform gripR = FindContains(model, "Grip_Right");
            if (gripL == null || gripR == null)
            {
                Log.Warning("[Wheelbarrow] Could not find handle grips for hand IK; pushing without hands attached.");
                return;
            }

            handTargetL = MakeHandTarget("WB_HandTargetL", gripL);
            handTargetR = MakeHandTarget("WB_HandTargetR", gripR);

            List<IKController.Target> targets = new List<IKController.Target>
            {
                new IKController.Target { avatarGoal = AvatarIKGoal.LeftHand, transform = handTargetL },
                new IKController.Target { avatarGoal = AvatarIKGoal.RightHand, transform = handTargetR }
            };

            player.SetIKTargets(targets);
            ikApplied = true;
        }

        private static Transform MakeHandTarget(string name, Transform grip)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(grip, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(HandEuler);
            return go.transform;
        }

        private static void ClearHandIK()
        {
            if (ikApplied)
            {
                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
                if (player != null)
                {
                    player.RemoveIKTargets();
                }

                ikApplied = false;
            }

            if (handTargetL != null)
            {
                Object.Destroy(handTargetL.gameObject);
                handTargetL = null;
            }

            if (handTargetR != null)
            {
                Object.Destroy(handTargetR.gameObject);
                handTargetR = null;
            }
        }

        private static void SpinWheel(EntityVehicle vehicle, Vector3 cartPos, Vector3 forward)
        {
            if (!hasLastPos)
            {
                return;
            }

            Transform model = vehicle.ModelTransform;
            Transform wheel = model != null ? model.Find("Mesh/M/Forks/Wheel0") : null;
            if (wheel == null)
            {
                return;
            }

            Vector3 delta = cartPos - lastCartPos;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.0005f)
            {
                return;
            }

            float direction = Vector3.Dot(delta, forward) >= 0f ? 1f : -1f;
            float circumference = 2f * Mathf.PI * WheelRadius;
            float degrees = distance / circumference * 360f * direction;
            wheel.Rotate(degrees, 0f, 0f, Space.Self);
        }

        private static void FreezePhysics(EntityVehicle vehicle, bool frozen)
        {
            Rigidbody rb = vehicle.vehicleRB;
            if (rb != null)
            {
                rb.isKinematic = frozen;
                if (frozen)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            // While pushing, drop the solid body collider so the cart can't shove or
            // trap the player; wheel colliders are inert on a kinematic body.
            Transform physics = vehicle.PhysicsTransform;
            if (physics != null)
            {
                BoxCollider box = physics.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.enabled = !frozen;
                }
            }
        }

        private static void SnapToGround(ref Vector3 position)
        {
            Vector3 rayOrigin = position - Origin.position + Vector3.up * 6f;
            RaycastHit hit;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 18f, ~0, QueryTriggerInteraction.Ignore))
            {
                position.y = hit.point.y + Origin.position.y + GroundClearance;
            }
        }

        private static Transform FindContains(Transform root, string namePart)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.IndexOf(namePart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            return null;
        }
    }

    [Preserve]
    internal sealed class WheelbarrowPushBehaviour : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (WheelbarrowPush.IsActive)
            {
                WheelbarrowPush.Tick();
            }
        }
    }
}
