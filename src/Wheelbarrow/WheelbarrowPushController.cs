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
        internal const float DefaultFrontOffset = 1.25f;   // cart root offset; tilted grip ends sit close enough for bent elbows
        internal const float DefaultHeightLift = 0.2f;     // raise the cart so handles reach hand height
        internal const float DefaultTiltDegrees = 15f;     // forward tilt onto the wheel (rear legs lift)
        internal const float GroundClearance = 0.06f;
        internal const float WheelRadius = 0.235f;         // artist wheel ~0.47m diameter
        internal const float YawOffset = 0f;               // cart-forward vs entity-forward (model faces +Z)
        internal const float TurnSmoothTime = 0.22f;       // higher = lazier turning
        internal const float TurnMaxRate = 120f;           // deg/sec cap on how fast the cart can swing
        internal const float MaxTurnOffset = 50f;          // nose may lag at most this far from your facing
        internal const float ToggleGuard = 0.25f;          // debounce so one keypress can't grab+drop
        internal const float ReleaseRollVelocity = 0.9f;   // rad/sec roll nudge after releasing physics
        internal const string BurdenBuffName = "buffWheelbarrowBurden";
        internal const string BurdenPenaltyCVar = ".wheelbarrowMovePenaltyDisplay";
        internal const int FreeFilledSlots = 5;
        internal const float BaseMovePenalty = 0.05f;
        internal const float PerFilledSlotPenalty = 0.01f;
        internal const float MaxMovePenalty = 0.75f;

        // Hand IK rotation/offset in grip-local space — tune live with `wb hands x y z`
        // and `wb handpos x y z`. The left hand is mirrored across the cart centreline.
        internal static Vector3 HandEuler = new Vector3(8f, 170f, 14f);
        internal static Vector3 HandOffset = new Vector3(0.05f, 0.03f, -0.08f);

        internal static EntityVehicle Current { get; private set; }
        internal static float FrontOffset = DefaultFrontOffset;
        internal static float HeightLift = DefaultHeightLift;
        internal static float TiltDegrees = DefaultTiltDegrees;
        internal static float CurrentMovePenalty { get; private set; }
        internal static int CurrentFilledSlots { get; private set; }

        private static Vector3 lastCartPos;
        private static bool hasLastPos;
        private static Transform handTargetL;
        private static Transform handTargetR;
        private static bool ikApplied;
        private static bool pendingIKSetup;
        private static float smoothedYaw;
        private static float yawVelocity;
        private static bool hasYaw;
        private static float lastPushYaw;
        private static bool hasLastPushYaw;
        private static float lastBeginTime = -10f;
        private static float lastReleaseTime = -10f;

        internal static bool IsActive => Current != null;

        internal static bool IsPushing(EntityVehicle vehicle)
        {
            return Current != null && vehicle != null && Current.entityId == vehicle.entityId;
        }

        // True briefly after a drop, so the same keypress that dropped the cart can't
        // immediately re-grab it via the interact handler.
        internal static bool JustReleased => Time.unscaledTime - lastReleaseTime < ToggleGuard;

        internal static bool ShouldLockLocalPlayer(EntityPlayerLocal player)
        {
            return ValidateActiveState(player, true);
        }

        internal static void ApplyMovementPenalty(EntityPlayerLocal player)
        {
            if (player == null || player.movementInput == null)
            {
                return;
            }

            float scale = Mathf.Clamp01(1f - CurrentMovePenalty);
            player.movementInput.moveForward *= scale;
            player.movementInput.moveStrafe *= scale;
        }

        // Start pushing using the current (possibly console-tuned) stance values.
        internal static void Begin(EntityVehicle vehicle)
        {
            Begin(vehicle, FrontOffset, HeightLift, TiltDegrees);
        }

        internal static void Toggle(EntityVehicle vehicle)
        {
            if (IsActive && Current == vehicle)
            {
                Release();
            }
            else
            {
                Begin(vehicle);
            }
        }

        // While pushing, dropping is driven by the interact key directly (the cart sits
        // low/in front and is awkward to look-focus), not by entity activation.
        internal static void HandleReleaseInput()
        {
            if (!IsActive || Time.unscaledTime - lastBeginTime < ToggleGuard)
            {
                return;
            }

            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
            if (!ValidateActiveState(player, true))
            {
                return;
            }

            PlayerActionsLocal input = player != null ? player.playerInput : null;
            if (input == null)
            {
                return;
            }

            bool pressed = (input.Activate != null && input.Activate.WasPressed) ||
                (input.PermanentActions != null && input.PermanentActions.Activate != null && input.PermanentActions.Activate.WasPressed);
            if (pressed)
            {
                Release();
            }
        }

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
            hasYaw = false;
            lastBeginTime = Time.unscaledTime;

            FreezePhysics(vehicle, true);
            UpdateBurden(GetPrimaryPlayer(), vehicle);

            // Defer hand IK to the first Tick, once the cart is glued in front of the
            // player, so we can read the grips' real world positions to pick sides.
            pendingIKSetup = true;
        }

        internal static void Release()
        {
            EntityVehicle vehicle = Current;
            float releaseYaw = hasLastPushYaw ? lastPushYaw : GetVehicleYaw(vehicle);
            ClearBurden(GetPrimaryPlayer());
            Current = null;
            hasLastPos = false;
            pendingIKSetup = false;
            hasLastPushYaw = false;
            lastReleaseTime = Time.unscaledTime;

            ClearHandIK();
            RestorePlayerHands();

            if (vehicle != null)
            {
                PrepareReleasedVehicle(vehicle, releaseYaw);
                FreezePhysics(vehicle, false);
                ReactivateReleasedPhysics(vehicle);
                NudgeReleasedVehicle(vehicle);
            }
        }

        /// <summary>Called every frame (LateUpdate) by the behaviour below.</summary>
        internal static void Tick()
        {
            EntityVehicle vehicle = Current;
            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
            if (!ValidateActiveState(player, true))
            {
                return;
            }

            vehicle = Current;
            if (vehicle == null)
            {
                return;
            }

            UpdateBurden(player, vehicle);

            // Smoothly chase the player's heading with a capped turn rate, so the cart
            // swings/trails into corners instead of snapping rigidly to face them.
            float targetYaw = player.rotation.y;
            if (!hasYaw)
            {
                smoothedYaw = targetYaw;
                yawVelocity = 0f;
                hasYaw = true;
            }
            else
            {
                smoothedYaw = Mathf.SmoothDampAngle(smoothedYaw, targetYaw, ref yawVelocity, TurnSmoothTime, TurnMaxRate, Time.deltaTime);
            }

            // Hard cap how far the nose may lag from your facing, so it can't be whipped
            // around — it stays within MaxTurnOffset degrees and drags along past that.
            float offset = Mathf.DeltaAngle(targetYaw, smoothedYaw);
            if (Mathf.Abs(offset) > MaxTurnOffset)
            {
                smoothedYaw = targetYaw + Mathf.Sign(offset) * MaxTurnOffset;
                yawVelocity = 0f;
            }

            float yaw = smoothedYaw;
            lastPushYaw = yaw;
            hasLastPushYaw = true;

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

            if (pendingIKSetup)
            {
                SetupHandIK(vehicle, player);
                pendingIKSetup = false;
            }

            lastCartPos = target;
            hasLastPos = true;
        }

        private static void SetupHandIK(EntityVehicle vehicle, EntityPlayerLocal player)
        {
            ClearHandIK();

            Transform model = vehicle != null ? vehicle.ModelTransform : null;
            if (player == null || model == null)
            {
                return;
            }

            Transform gripA = FindContains(model, "Grip_Left");
            Transform gripB = FindContains(model, "Grip_Right");
            if (gripA == null || gripB == null)
            {
                Log.Warning("[Wheelbarrow] Could not find handle grips for hand IK; pushing without hands attached.");
                return;
            }

            // Pick sides from each grip's real position relative to the player's right
            // vector, so it stays correct even though the FBX export mirrors X (which
            // makes the "Left"/"Right" mesh names unreliable).
            Vector3 right = Quaternion.Euler(0f, player.rotation.y, 0f) * Vector3.right;
            float sideA = Vector3.Dot(gripA.position - player.position, right);
            float sideB = Vector3.Dot(gripB.position - player.position, right);
            Transform leftGrip = sideA <= sideB ? gripA : gripB;
            Transform rightGrip = sideA <= sideB ? gripB : gripA;

            handTargetL = MakeHandTarget("WB_HandTargetL", leftGrip, isLeft: true);
            handTargetR = MakeHandTarget("WB_HandTargetR", rightGrip, isLeft: false);

            List<IKController.Target> targets = new List<IKController.Target>
            {
                new IKController.Target { avatarGoal = AvatarIKGoal.LeftHand, transform = handTargetL },
                new IKController.Target { avatarGoal = AvatarIKGoal.RightHand, transform = handTargetR }
            };

            player.SetIKTargets(targets);
            ikApplied = true;
        }

        private static Transform MakeHandTarget(string name, Transform grip, bool isLeft)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(grip, worldPositionStays: false);

            Vector3 offset = HandOffset;
            Vector3 euler = HandEuler;
            if (isLeft)
            {
                offset.x = -offset.x; // mirror across the cart centreline
                euler = new Vector3(euler.x, -euler.y, -euler.z);
            }

            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.Euler(euler);
            return go.transform;
        }

        // Re-pin the hands using the current HandEuler/HandOffset (for live console tuning).
        internal static void RefreshHandIK()
        {
            if (!IsActive)
            {
                return;
            }

            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
            if (player != null)
            {
                SetupHandIK(Current, player);
            }
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
            if (frozen)
            {
                vehicle.RBActive = false;
                vehicle.RBNoDriverGndTime = 0f;
                vehicle.RBNoDriverSleepTime = 0f;
                vehicle.isTryToFall = false;
            }

            Rigidbody rb = vehicle.vehicleRB;
            if (rb != null)
            {
                rb.isKinematic = frozen;
                if (frozen)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.WakeUp();
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

        private static void ReactivateReleasedPhysics(EntityVehicle vehicle)
        {
            Rigidbody rb = vehicle != null ? vehicle.vehicleRB : null;
            if (rb == null || vehicle.isEntityRemote)
            {
                return;
            }

            vehicle.RBActive = true;
            vehicle.RBNoDriverGndTime = 0f;
            vehicle.RBNoDriverSleepTime = 0f;
            vehicle.isTryToFall = false;
            rb.isKinematic = false;
            rb.WakeUp();

            // Match the vanilla wake path used by VehicleManager.PhysicsWakeNear.
            vehicle.AddForce(Vector3.zero);
        }

        private static void PrepareReleasedVehicle(EntityVehicle vehicle, float yaw)
        {
            if (vehicle == null || vehicle.IsDead())
            {
                return;
            }

            Vector3 parkedPosition = vehicle.position;
            SnapToGround(ref parkedPosition);

            Quaternion parkedRotation = Quaternion.Euler(0f, yaw + YawOffset, 0f) *
                Quaternion.Euler(TiltDegrees, 0f, 0f);
            Vector3 unityPosition = parkedPosition - Origin.position;

            vehicle.SetPosition(parkedPosition, false);
            vehicle.SetRotation(parkedRotation.eulerAngles);

            Rigidbody rb = vehicle.vehicleRB;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = unityPosition;
                rb.rotation = parkedRotation;
            }

            Transform model = vehicle.ModelTransform;
            if (model != null)
            {
                model.SetPositionAndRotation(unityPosition, parkedRotation);
            }

            Transform physics = vehicle.PhysicsTransform;
            if (physics != null)
            {
                physics.SetPositionAndRotation(unityPosition, parkedRotation);
            }
        }

        private static void NudgeReleasedVehicle(EntityVehicle vehicle)
        {
            Rigidbody rb = vehicle != null ? vehicle.vehicleRB : null;
            if (rb == null)
            {
                return;
            }

            float rollSign = GetReleaseRollSign(vehicle);
            Vector3 rollAxis = rb.rotation * Vector3.forward;
            rb.angularVelocity = rollAxis * rollSign * ReleaseRollVelocity;
            rb.WakeUp();
        }

        private static void RestorePlayerHands()
        {
            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
            if (player == null)
            {
                return;
            }

            player.HolsterWeapon(false);
            if (player.bFirstPersonView)
            {
                player.ShowHoldingItemLayer(true);
            }

            if (player.inventory != null)
            {
                player.inventory.ShowRightHand(true);
                player.inventory.SetIsFinishedSwitchingHeldItem();
            }
        }

        private static float GetVehicleYaw(EntityVehicle vehicle)
        {
            if (vehicle == null)
            {
                return 0f;
            }

            if (hasLastPushYaw)
            {
                return lastPushYaw;
            }

            return vehicle.rotation.y;
        }

        private static float GetReleaseRollSign(EntityVehicle vehicle)
        {
            return (vehicle.entityId & 1) == 0 ? 1f : -1f;
        }

        private static EntityPlayerLocal GetPrimaryPlayer()
        {
            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            return world != null ? world.GetPrimaryPlayer() : null;
        }

        private static void UpdateBurden(EntityPlayerLocal player, EntityVehicle vehicle)
        {
            if (player == null || vehicle == null)
            {
                ClearBurden(player);
                return;
            }

            CurrentFilledSlots = CountFilledStorageSlots(vehicle);
            CurrentMovePenalty = CalculateMovePenalty(CurrentFilledSlots);
            player.SetCVar(BurdenPenaltyCVar, Mathf.Round(CurrentMovePenalty * 100f));

            if (player.Buffs != null && !player.Buffs.HasBuff(BurdenBuffName))
            {
                player.Buffs.AddBuff(BurdenBuffName);
            }
        }

        private static void ClearBurden(EntityPlayerLocal player)
        {
            CurrentFilledSlots = 0;
            CurrentMovePenalty = 0f;

            if (player == null)
            {
                return;
            }

            player.SetCVar(BurdenPenaltyCVar, 0f);
            if (player.Buffs != null && player.Buffs.HasBuff(BurdenBuffName))
            {
                player.Buffs.RemoveBuff(BurdenBuffName);
            }
        }

        private static float CalculateMovePenalty(int filledSlots)
        {
            int loadedSlots = Mathf.Max(filledSlots, FreeFilledSlots);
            return Mathf.Clamp(loadedSlots * PerFilledSlotPenalty, BaseMovePenalty, MaxMovePenalty);
        }

        private static int CountFilledStorageSlots(EntityVehicle vehicle)
        {
            if (vehicle == null || vehicle.bag == null)
            {
                return 0;
            }

            return vehicle.bag.GetUsedSlotCount();
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

        private static bool ValidateActiveState(EntityPlayerLocal player, bool releaseIfInvalid)
        {
            EntityVehicle vehicle = Current;
            if (vehicle == null)
            {
                return false;
            }

            string reason = GetInvalidActiveStateReason(vehicle, player);
            if (reason == null)
            {
                return true;
            }

            if (releaseIfInvalid)
            {
                Log.Out("[Wheelbarrow] Releasing stale push lock: " + reason);
                Release();
            }

            return false;
        }

        private static string GetInvalidActiveStateReason(EntityVehicle vehicle, EntityPlayerLocal player)
        {
            if (vehicle == null)
            {
                return "missing vehicle";
            }

            if (vehicle.IsDead())
            {
                return "vehicle is dead";
            }

            if (vehicle.RootTransform == null || vehicle.ModelTransform == null || vehicle.PhysicsTransform == null)
            {
                return "vehicle transforms are missing";
            }

            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return "world is unavailable";
            }

            if (player == null)
            {
                return "local player is unavailable";
            }

            if (player.IsDead())
            {
                return "local player is dead";
            }

            EntityPlayerLocal primaryPlayer = world.GetPrimaryPlayer();
            if (primaryPlayer == null || primaryPlayer.entityId != player.entityId)
            {
                return "input player is not the primary local player";
            }

            VehicleManager manager = VehicleManager.Instance;
            if (manager == null)
            {
                return "vehicle manager is unavailable";
            }

            bool activeVehicleFound = false;
            for (int i = 0; i < manager.vehiclesActive.Count; i++)
            {
                EntityVehicle activeVehicle = manager.vehiclesActive[i];
                if (activeVehicle != null && activeVehicle.entityId == vehicle.entityId)
                {
                    activeVehicleFound = true;
                    break;
                }
            }

            if (!activeVehicleFound)
            {
                return "vehicle is no longer active";
            }

            float maxDistance = Mathf.Max(FrontOffset + 4f, 8f);
            Vector3 delta = vehicle.position - player.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > maxDistance * maxDistance)
            {
                return "player moved too far from vehicle";
            }

            return null;
        }
    }

    [Preserve]
    internal sealed class WheelbarrowPushBehaviour : MonoBehaviour
    {
        private void Update()
        {
            // Interact key drops the cart while pushing.
            WheelbarrowPush.HandleReleaseInput();
        }

        private void LateUpdate()
        {
            if (WheelbarrowPush.IsActive)
            {
                WheelbarrowPush.Tick();
            }
        }
    }
}
