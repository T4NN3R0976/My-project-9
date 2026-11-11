// =====================================================================================
//  PlayerAnimationController.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  One-way bridge: reads PlayerController's state each frame and writes Animator
//  parameters. It never moves the player, so you can swap the character model or the
//  Animator Controller without touching movement code.
//
//  UNITY SETUP
//  1. Put this on the Player root (next to PlayerController) or on the character
//     model. It finds PlayerController in its parents and the Animator in its children.
//     You can also drag both in yourself.
//  2. On the model's Animator component:
//       • Controller = your Animator Controller (see the parameter table below)
//       • Avatar     = the asset pack's Humanoid avatar
//       • Apply Root Motion: this script turns it OFF at runtime, because the
//         CharacterController does the moving. Untick "Disable Root Motion" to keep it.
//  3. In the Animator window ▸ Parameters, create these. Names must match exactly, or
//     be renamed in the "Animator Parameter Names" fields. Missing parameters are
//     skipped and listed once in the Console, so you can add them gradually.
//
//     ┌────────────────────┬─────────┬──────────────────────────────────────────────┐
//     │ Name               │ Type    │ Meaning                                      │
//     ├────────────────────┼─────────┼──────────────────────────────────────────────┤
//     │ Speed              │ Float   │ Horizontal speed in m/s (0 idle, ~6 walk,     │
//     │                    │         │ ~9 sprint), or 0..1 if "Normalize Speed" is on │
//     │ IsGrounded         │ Bool    │ Feet on the ground                            │
//     │ IsJumping          │ Bool    │ Rising after a jump                           │
//     │ VerticalVelocity   │ Float   │ (optional) + rising / - falling               │
//     │ IsWallRunning      │ Bool    │ Wallrunning (Level 3)                         │
//     │ WallSide           │ Float   │ -1 wall on the LEFT, +1 wall on the RIGHT     │
//     │ IsHanging          │ Bool    │ On the monkey bars (Level 5)                  │
//     │ HangSpeed          │ Float   │ -1 back · 0 idle hang · +1 forward            │
//     │ Respawn            │ Trigger │ Fired once when the player respawns           │
//     │ DoubleJump         │ Trigger │ Fired on each mid-air jump (e.g. a flip)      │
//     └────────────────────┴─────────┴──────────────────────────────────────────────┘
//
//  4. Suggested state machine (untick "Has Exit Time" and set Transition Duration to
//     about 0.1 on every transition):
//       Locomotion (Blend Tree, Speed: 0 Idle / 6 Run / 9 Sprint)  ← default state
//         → Jump        when IsJumping == true
//         → Fall        when IsGrounded == false AND IsJumping == false
//       Jump → Fall     when IsJumping == false
//       Fall → Locomotion when IsGrounded == true
//       Any State → WallRun (Blend Tree, WallSide: -1 WallRunLeft / +1 WallRunRight)
//                       when IsWallRunning == true
//                       (untick "Can Transition To Self")
//         WallRun → Fall when IsWallRunning == false
//       Any State → Hang (Blend Tree, HangSpeed: -1 Back / 0 Idle / +1 Forward)
//                       when IsHanging == true   (untick "Can Transition To Self")
//         Hang → Fall    when IsHanging == false
//       Any State → Locomotion when Respawn (trigger)
//     If the pack only has one wallrun clip, use it for both sides and tick "Mirror"
//     on the motion for the opposite side.
// =====================================================================================

using System.Collections.Generic;
using UnityEngine;

namespace ObbyKit
{
    [DisallowMultipleComponent]
    public class PlayerAnimationController : MonoBehaviour
    {
        // ---------------------------------------------------------------- Inspector
        [Header("References (auto-found if empty)")]
        [SerializeField] private PlayerController player;
        [SerializeField] private Animator animator;

        [Header("Animator Parameter Names")]
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string isGroundedParam = "IsGrounded";
        [SerializeField] private string isJumpingParam = "IsJumping";
        [SerializeField] private string verticalVelocityParam = "VerticalVelocity";
        [SerializeField] private string isWallRunningParam = "IsWallRunning";
        [SerializeField] private string wallSideParam = "WallSide";
        [SerializeField] private string isHangingParam = "IsHanging";
        [SerializeField] private string hangSpeedParam = "HangSpeed";
        [SerializeField] private string respawnTrigger = "Respawn";
        [Tooltip("Trigger fired on each mid-air (double) jump, e.g. to play a flip.")]
        [SerializeField] private string doubleJumpTrigger = "DoubleJump";

        [Header("Tuning")]
        [Tooltip("ON: Speed is sent as 0..1 (fraction of sprint speed). OFF: Speed is in m/s.")]
        [SerializeField] private bool normalizeSpeed = false;
        [Tooltip("Smoothing for Speed, so the blend tree doesn't jitter.")]
        [SerializeField] private float speedDampTime = 0.1f;
        [SerializeField] private float hangSpeedDampTime = 0.1f;
        [Tooltip("Force Animator.applyRootMotion off, since the CharacterController moves the player.")]
        [SerializeField] private bool disableRootMotion = true;
        [Tooltip("On respawn, also Rebind() the Animator: an instant reset of every state, " +
                 "for when Any State → Respawn isn't enough.")]
        [SerializeField] private bool hardResetOnRespawn = false;

        // ---------------------------------------------------------------- Hashes
        private int speedHash, isGroundedHash, isJumpingHash, verticalVelocityHash;
        private int isWallRunningHash, wallSideHash, isHangingHash, hangSpeedHash, respawnHash, doubleJumpHash;
        private readonly HashSet<int> existingParams = new HashSet<int>();

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (player == null || animator == null)
            {
                Debug.LogError("[ObbyKit] PlayerAnimationController needs a PlayerController (in this object or a parent) " +
                               "and an Animator (in this object or a child). Disabling.", this);
                enabled = false;
                return;
            }

            if (disableRootMotion) animator.applyRootMotion = false;
            CacheParameters();
        }

        private void OnEnable()
        {
            if (player == null) return;
            player.Respawned += HandleRespawn;
            player.DoubleJumped += HandleDoubleJump;
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.Respawned -= HandleRespawn;
            player.DoubleJumped -= HandleDoubleJump;
        }

        private void HandleDoubleJump()
        {
            if (existingParams.Contains(doubleJumpHash)) animator.SetTrigger(doubleJumpHash);
        }

        private void CacheParameters()
        {
            speedHash            = Animator.StringToHash(speedParam);
            isGroundedHash       = Animator.StringToHash(isGroundedParam);
            isJumpingHash        = Animator.StringToHash(isJumpingParam);
            verticalVelocityHash = Animator.StringToHash(verticalVelocityParam);
            isWallRunningHash    = Animator.StringToHash(isWallRunningParam);
            wallSideHash         = Animator.StringToHash(wallSideParam);
            isHangingHash        = Animator.StringToHash(isHangingParam);
            hangSpeedHash        = Animator.StringToHash(hangSpeedParam);
            respawnHash          = Animator.StringToHash(respawnTrigger);
            doubleJumpHash       = Animator.StringToHash(doubleJumpTrigger);

            existingParams.Clear();
            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[ObbyKit] The Animator has no Controller assigned, so no animation parameters will be set.", animator);
                return;
            }
            foreach (var p in animator.parameters) existingParams.Add(p.nameHash);

            // List missing parameters once, so the setup is easy to check.
            var missing = new List<string>();
            void Check(string n) { if (!string.IsNullOrEmpty(n) && !existingParams.Contains(Animator.StringToHash(n))) missing.Add(n); }
            Check(speedParam); Check(isGroundedParam); Check(isJumpingParam); Check(verticalVelocityParam);
            Check(isWallRunningParam); Check(wallSideParam); Check(isHangingParam); Check(hangSpeedParam); Check(respawnTrigger);
            Check(doubleJumpTrigger);
            if (missing.Count > 0)
                Debug.Log($"[ObbyKit] Animator '{animator.runtimeAnimatorController.name}' is missing these parameters " +
                          $"(they'll be skipped): {string.Join(", ", missing)}", animator);
        }

        private void Update()
        {
            float speed = player.HorizontalSpeed;
            if (normalizeSpeed) speed /= Mathf.Max(0.01f, player.SprintSpeed);

            SetFloat(speedHash, speed, speedDampTime);
            SetBool(isGroundedHash, player.IsGrounded);
            SetBool(isJumpingHash, player.IsJumping);
            SetFloat(verticalVelocityHash, player.Velocity.y, 0f);

            SetBool(isWallRunningHash, player.IsWallRunning);
            // Only update WallSide while running, so the exit blend doesn't snap to the middle.
            if (player.IsWallRunning) SetFloat(wallSideHash, player.WallSide, 0f);

            SetBool(isHangingHash, player.IsHanging);
            SetFloat(hangSpeedHash, player.HangSpeedNormalized, hangSpeedDampTime);
        }

        private void HandleRespawn()
        {
            if (hardResetOnRespawn)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            // Snap everything to a clean standing state, with no damping.
            SetFloat(speedHash, 0f, 0f);
            SetFloat(verticalVelocityHash, 0f, 0f);
            SetFloat(hangSpeedHash, 0f, 0f);
            SetBool(isJumpingHash, false);
            SetBool(isWallRunningHash, false);
            SetBool(isHangingHash, false);
            SetBool(isGroundedHash, true);

            if (existingParams.Contains(doubleJumpHash)) animator.ResetTrigger(doubleJumpHash);
            if (existingParams.Contains(respawnHash)) animator.SetTrigger(respawnHash);
        }

        // ---------------------------------------------------------------- Safe setters
        private void SetFloat(int hash, float value, float damp)
        {
            if (!existingParams.Contains(hash)) return;
            if (damp > 0f) animator.SetFloat(hash, value, damp, Time.deltaTime);
            else animator.SetFloat(hash, value);
        }

        private void SetBool(int hash, bool value)
        {
            if (existingParams.Contains(hash)) animator.SetBool(hash, value);
        }
    }
}
