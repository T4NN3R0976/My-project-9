// =====================================================================================
//  PlayerController.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  CharacterController-based obby movement: walk/sprint/jump (with coyote time and
//  jump buffering), plus WALLRUNNING (Level 3 only) and MONKEY BARS (Level 5 only).
//  Implements IRespawnable so checkpoints and the void can teleport it.
//
//  ── REQUIRED COMPONENTS ─────────────────────────────────────────────────────────────
//   • CharacterController (added automatically). Typical values for a human model:
//     Height 1.8–2, Radius 0.3–0.4, Center Y = Height / 2, so the player's pivot is at
//     the feet. Turn OFF or remove any other movement script on the Player (for example
//     a starter-asset ThirdPersonController) so the two don't fight.
//   • Do NOT add a non-kinematic Rigidbody to the player.
//
//  ── LAYERS (Edit ▸ Project Settings ▸ Tags and Layers) ──────────────────────────────
//   • "Player"    → put the Player root and its children on it.
//   • "Wall"      → walls that can be run on. They need ordinary (non-trigger) colliders.
//   • "MonkeyBar" → monkey bar TRIGGER colliders (see below).
//   Then, on this component:
//     Wall Layer       = Wall
//     Ground Layer     = Everything EXCEPT Player (used for the "high enough to wallrun?" check)
//     Monkey Bar Layer = MonkeyBar  (or leave as Nothing and use the "MonkeyBar" tag instead)
//
//  ── MONKEY BARS ─────────────────────────────────────────────────────────────────────
//   For each run of bars, add ONE long BoxCollider set to Is Trigger, running ALONG the
//   direction of travel at the height of the rungs, on the MonkeyBar layer (or tagged
//   "MonkeyBar"). Its longest horizontal side is treated as the bar axis. The visible
//   rungs/truss can keep their own colliders on another layer. Lines of bars that touch
//   end-to-end are joined up automatically.
//
//  ── CAMERA ──────────────────────────────────────────────────────────────────────────
//   Control Scheme:
//     CameraRelative — the camera is NOT a child of the player (a follow script or
//                      Cinemachine). WASD moves relative to the camera, and the
//                      character turns to face its movement.
//     MouseYaw       — the camera IS a child of the player (as in your current scene).
//                      Mouse X turns the player, WASD moves and strafes relative to the
//                      player. Using CameraRelative with a child camera makes the
//                      player spin in circles.
//   Camera Tilt Pivot: a Transform that is rolled while wallrunning. Best practice is an
//   empty "CameraPivot" parent with the camera inside it, so the tilt doesn't fight a
//   camera script. If you use your own camera rig, leave this empty and read
//   PlayerController.CameraRoll instead.
//
//  ── INPUT ───────────────────────────────────────────────────────────────────────────
//   Uses the legacy Input Manager (Horizontal/Vertical axes, Mouse X, and the KeyCodes
//   below). If your project uses ONLY the new Input System, set
//   Project Settings ▸ Player ▸ Active Input Handling = "Both".
//   Default controls: WASD move · Shift sprint · Space jump / wall-jump / jump off bars ·
//   Left Ctrl or C drop off bars.
// =====================================================================================

using System;
using UnityEngine;

namespace ObbyKit
{
    public enum MovementState { Grounded, Airborne, WallRunning, Hanging }
    public enum ControlScheme { CameraRelative, MouseYaw }

    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour, IRespawnable
    {
        // ============================================================ INSPECTOR
        [Header("Level Rules")]
        [Tooltip("The level the player is on now. Flags with a 'Level Number' update this automatically.")]
        [SerializeField] private int currentLevel = 1;
        [Tooltip("Wallrunning only works when Current Level equals this.")]
        [SerializeField] private int wallRunLevel = 3;
        [Tooltip("Monkey bars only work when Current Level equals this.")]
        [SerializeField] private int monkeyBarLevel = 5;

        [Header("Control")]
        [SerializeField] private ControlScheme controlScheme = ControlScheme.CameraRelative;
        [Tooltip("Camera used for CameraRelative movement. Defaults to Camera.main.")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("MouseYaw only: degrees turned per unit of mouse movement.")]
        [SerializeField] private float mouseSensitivity = 2f;
        [Tooltip("MouseYaw only: lock and hide the cursor (Esc frees it, click to lock again).")]
        [SerializeField] private bool lockCursor = true;
        [Tooltip("MouseYaw only: how far you can look down / up (the camera tilts, the body doesn't).")]
        [SerializeField] private float minLookAngle = -80f;
        [SerializeField] private float maxLookAngle = 80f;
        [SerializeField] private bool invertY = false;

        [Header("Knockback (used by obstacles such as SpinningBeam)")]
        [Tooltip("How quickly a knockback fades while on the ground.")]
        [SerializeField] private float groundKnockbackFade = 6f;
        [Tooltip("How quickly a knockback fades in the air (lower = fly further).")]
        [SerializeField] private float airKnockbackFade = 1f;

        [Header("Input (legacy Input Manager)")]
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode dropKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode altDropKey = KeyCode.C;

        [Header("Ground Movement")]
        [SerializeField] private float walkSpeed = 6f;
        [SerializeField] private float sprintSpeed = 9f;
        [SerializeField] private float groundAcceleration = 60f;
        [Tooltip("How quickly you can steer in the air. Lower = more committed jumps.")]
        [SerializeField] private float airAcceleration = 20f;
        [Tooltip("Degrees per second the character turns toward its movement direction.")]
        [SerializeField] private float turnSpeed = 720f;

        [Header("Jumping & Gravity")]
        [SerializeField] private float jumpHeight = 1.8f;
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float maxFallSpeed = -50f;
        [Tooltip("Seconds after walking off a ledge during which you can still jump.")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("Seconds a jump press is remembered before landing.")]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Double Jump")]
        [Tooltip("Press Jump again in mid-air for an extra jump.")]
        [SerializeField] private bool enableDoubleJump = true;
        [Tooltip("How many extra jumps you get in the air (1 = double jump, 2 = triple jump...).")]
        [SerializeField] private int extraAirJumps = 1;
        [Tooltip("Height of the mid-air jump in metres.")]
        [SerializeField] private float doubleJumpHeight = 1.5f;
        [Tooltip("Optional wait (seconds) after a double jump before it can be used again, even after landing. 0 = ready again as soon as you land.")]
        [SerializeField] private float doubleJumpCooldown = 0f;
        [Tooltip("ON: the double jump also turns you instantly toward the direction you're holding.")]
        [SerializeField] private bool doubleJumpRedirect = true;

        [Header("Wall Running (Level 3)")]
        [Tooltip("Layer(s) that count as runnable walls.")]
        [SerializeField] private LayerMask wallLayer;
        [Tooltip("Layer(s) used to check that you're high enough off the ground. Exclude the Player layer.")]
        [SerializeField] private LayerMask groundLayer = ~0;
        [Tooltip("How far beyond the capsule's edge to look for a wall.")]
        [SerializeField] private float wallCheckDistance = 0.6f;
        [SerializeField] private float wallCheckRadius = 0.25f;
        [Tooltip("You must be at least this far above the ground to start a wallrun.")]
        [SerializeField] private float minHeightAboveGround = 1.0f;
        [Tooltip("Minimum horizontal speed needed to start a wallrun.")]
        [SerializeField] private float minEntrySpeed = 3f;
        [SerializeField] private float wallRunSpeed = 8f;
        [Tooltip("ON: wallrunning keeps you at a constant height, so you run perfectly straight along the wall. " +
                 "OFF: you slowly sink, using Wall Run Gravity / Wall Run Max Fall Speed below.")]
        [SerializeField] private bool flatWallRun = true;
        [Tooltip("ON: running off the end of a wall (while still holding forward) does a wall-jump automatically, " +
                 "launching you toward the next wall.")]
        [SerializeField] private bool autoJumpAtWallEnd = true;
        [Tooltip("Gravity while wallrunning (only used when Flat Wall Run is OFF).")]
        [SerializeField] private float wallRunGravity = -4f;
        [Tooltip("Fastest you can slide down a wall while running on it (only used when Flat Wall Run is OFF).")]
        [SerializeField] private float wallRunMaxFallSpeed = -2f;
        [SerializeField] private float wallRunMaxDuration = 1.6f;
        [Tooltip("Small push into the wall that keeps you attached.")]
        [SerializeField] private float wallStickForce = 2f;
        [Tooltip("Surfaces tilted more than this many degrees from vertical aren't walls.")]
        [SerializeField] private float maxWallSlope = 20f;
        [SerializeField] private float wallJumpUpForce = 7f;
        [SerializeField] private float wallJumpAwayForce = 6f;
        [SerializeField] private float wallJumpForwardBoost = 2f;
        [Tooltip("Seconds before you can grab the SAME wall again after leaving it.")]
        [SerializeField] private float sameWallCooldown = 0.5f;
        [Tooltip("Seconds after a wall-jump during which WASD can't steer, so the push carries you across to the next wall.")]
        [SerializeField] private float wallJumpAirLock = 0.5f;

        [Header("Camera Tilt (wallrun)")]
        [Tooltip("Optional Transform rolled while wallrunning (ideally an empty parent of the camera).")]
        [SerializeField] private Transform cameraTiltPivot;
        [SerializeField] private float wallRunTiltAngle = 12f;
        [Tooltip("ON = lean away from the wall. OFF = lean toward it.")]
        [SerializeField] private bool tiltAwayFromWall = true;
        [SerializeField] private float tiltSpeed = 8f;

        [Header("Monkey Bars (Level 5)")]
        [Tooltip("Layer(s) holding monkey bar triggers. Leave as Nothing to use the tag below instead.")]
        [SerializeField] private LayerMask monkeyBarLayer;
        [Tooltip("Used only when Monkey Bar Layer is Nothing.")]
        [SerializeField] private string monkeyBarTag = "MonkeyBar";
        [Tooltip("Radius of the grab check above the head.")]
        [SerializeField] private float grabRadius = 0.6f;
        [Tooltip("How far above the top of the capsule the hands can reach.")]
        [SerializeField] private float grabReachAboveHead = 0.35f;
        [Tooltip("Distance from the player's pivot (feet) up to the hands while hanging. " +
                 "Tune this until the hands line up with the bar.")]
        [SerializeField] private float hangOffset = 2.1f;
        [SerializeField] private float hangMoveSpeed = 3f;
        [Tooltip("How fast the body snaps onto the bar line.")]
        [SerializeField] private float hangSnapSpeed = 12f;
        [Tooltip("Seconds before you can grab any bar after dropping.")]
        [SerializeField] private float regrabCooldown = 0.4f;
        [SerializeField] private float dropJumpUpForce = 4f;
        [SerializeField] private float dropJumpForwardForce = 3f;

        // ============================================================ PUBLIC READ-ONLY STATE
        // These are read by PlayerAnimationController and any UI or camera scripts.

        /// <summary>Change this from your own scripts (or with a flag's Level Number) to switch level rules.</summary>
        public int CurrentLevel { get => currentLevel; set => currentLevel = value; }
        public bool WallRunUnlocked => currentLevel == wallRunLevel;
        public bool MonkeyBarsUnlocked => currentLevel == monkeyBarLevel;

        public MovementState State { get; private set; } = MovementState.Airborne;
        public Vector3 Velocity => velocity;
        public float HorizontalSpeed => new Vector2(velocity.x, velocity.z).magnitude;
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;

        public bool IsGrounded => State == MovementState.Grounded;
        /// <summary>True from takeoff until you start falling or land.</summary>
        public bool IsJumping { get; private set; }

        /// <summary>Double jump is switched on for this player.</summary>
        public bool DoubleJumpEnabled => enableDoubleJump && extraAirJumps > 0;
        /// <summary>True when a mid-air jump is available right now (refills on landing, wallrunning, grabbing bars, respawning).</summary>
        public bool DoubleJumpReady => DoubleJumpEnabled && airJumpsLeft > 0 && Time.time >= doubleJumpReadyTime;
        public int AirJumpsLeft => airJumpsLeft;
        public float DoubleJumpCooldown => doubleJumpCooldown;
        /// <summary>0..1 cooldown progress (1 = cooldown finished). Without a cooldown this is 1 when ready, else 0.</summary>
        public float DoubleJumpCooldownProgress => doubleJumpCooldown <= 0f
            ? (DoubleJumpReady ? 1f : 0f)
            : Mathf.Clamp01(1f - (doubleJumpReadyTime - Time.time) / doubleJumpCooldown);

        public bool IsWallRunning => State == MovementState.WallRunning;
        /// <summary>-1 = wall on the left, +1 = wall on the right, 0 = not wallrunning.</summary>
        public int WallSide { get; private set; }

        public bool IsHanging => State == MovementState.Hanging;
        /// <summary>Signed speed along the bar in m/s (+ forward, - backward).</summary>
        public float HangSpeed { get; private set; }
        /// <summary>HangSpeed scaled to -1..1, which is handy for blend trees.</summary>
        public float HangSpeedNormalized => hangMoveSpeed > 0f ? HangSpeed / hangMoveSpeed : 0f;

        /// <summary>Current camera roll in degrees (for custom camera rigs).</summary>
        public float CameraRoll { get; private set; }

        public Transform Transform => transform;

        public event Action Jumped;
        /// <summary>Raised on every mid-air (double) jump, after Jumped.</summary>
        public event Action DoubleJumped;
        public event Action Respawned;
        public event Action<MovementState, MovementState> StateChanged; // (old, new)

        // ============================================================ PRIVATE STATE
        private CharacterController controller;
        private Vector3 velocity;
        private Vector3 knockback;
        private float pitch;

        // input
        private Vector2 moveInput;
        private bool sprintHeld;
        private bool dropPressed;
        private float jumpBufferTimer;
        private float lastGroundedTime = float.NegativeInfinity;

        // wallrun
        private Vector3 wallNormal;
        private Collider currentWall;
        private Collider lastWall;
        private float lastWallCooldownEnd;
        private float wallRunTimer;
        private float airControlLockedUntil;

        // double jump
        private int airJumpsLeft;
        private float doubleJumpReadyTime;

        // monkey bars
        private Collider currentBar;
        private Collider lastBar;
        private float lastBarExcludeUntil;
        private Vector3 barStart, barAxis;
        private float barLength, barT;
        private float regrabAllowedTime;
        private readonly Collider[] overlapBuffer = new Collider[16];

        // camera
        private Quaternion tiltBaseRotation = Quaternion.identity;

        private Vector3 BodyCenter => transform.TransformPoint(controller.center);
        private Vector3 FeetPosition => BodyCenter - Vector3.up * (controller.height * 0.5f);
        private Vector3 HandPosition => transform.position + Vector3.up * hangOffset;

        // ============================================================ LIFECYCLE
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            RefillAirJumps();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTiltPivot != null) tiltBaseRotation = cameraTiltPivot.localRotation;

            if (wallLayer.value == 0)
                Debug.LogWarning("[ObbyKit] PlayerController: 'Wall Layer' is set to Nothing, so wallrunning can't detect walls.", this);
        }

        private void Start()
        {
            var manager = CheckpointManager.Instance;
            if (manager != null)
            {
                manager.RegisterPlayer(this);
                manager.CheckpointActivated += OnCheckpointActivated;
            }
            else
            {
                Debug.LogWarning("[ObbyKit] No CheckpointManager in the scene, so respawning won't work.", this);
            }

            if (controlScheme == ControlScheme.MouseYaw)
            {
                if (cameraTransform != null) pitch = Mathf.DeltaAngle(0f, cameraTransform.localEulerAngles.x);
                if (lockCursor) SetCursorLocked(true);
            }
        }

        private void OnDestroy()
        {
            var manager = CheckpointManager.Instance;
            if (manager != null) manager.CheckpointActivated -= OnCheckpointActivated;
        }

        private void OnCheckpointActivated(Checkpoint checkpoint)
        {
            if (checkpoint.LevelNumber > 0) currentLevel = checkpoint.LevelNumber;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            ReadInput(dt);

            switch (State)
            {
                case MovementState.Hanging:     UpdateHanging(dt);    break;
                case MovementState.WallRunning: UpdateWallRun(dt);    break;
                default:                        UpdateLocomotion(dt); break;
            }

            UpdateCameraTilt(dt);
        }

        // ============================================================ INPUT
        private void ReadInput(float dt)
        {
            moveInput = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
            sprintHeld = Input.GetKey(sprintKey);
            dropPressed = Input.GetKeyDown(dropKey) || Input.GetKeyDown(altDropKey);

            if (Input.GetKeyDown(jumpKey)) jumpBufferTimer = jumpBufferTime;
            else jumpBufferTimer -= dt;

            if (controlScheme == ControlScheme.MouseYaw)
            {
                if (lockCursor)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
                    else if (Input.GetMouseButtonDown(0)) SetCursorLocked(true);
                }
                if (!lockCursor || Cursor.lockState == CursorLockMode.Locked)
                {
                    transform.Rotate(0f, Input.GetAxis("Mouse X") * mouseSensitivity, 0f, Space.World);
                    float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
                    pitch = Mathf.Clamp(pitch + (invertY ? mouseY : -mouseY), minLookAngle, maxLookAngle);
                }
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>World-space movement direction from WASD (length 0..1).</summary>
        private Vector3 GetWishDirection()
        {
            Vector3 forward, right;
            if (controlScheme == ControlScheme.CameraRelative && cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            }
            else
            {
                forward = transform.forward;
                right = transform.right;
            }
            return Vector3.ClampMagnitude(forward * moveInput.y + right * moveInput.x, 1f);
        }

        // ============================================================ GROUND / AIR
        private void UpdateLocomotion(float dt)
        {
            bool grounded = controller.isGrounded;
            if (grounded)
            {
                lastGroundedTime = Time.time;
                lastBar = null;                                  // landing clears all grab/run lockouts
                if (lastWallCooldownEnd == float.PositiveInfinity) lastWall = null;
                RefillAirJumps();
                if (velocity.y < 0f)
                {
                    velocity.y = -2f;                            // small downward push keeps isGrounded stable
                    IsJumping = false;
                }
            }
            else if (IsJumping && velocity.y <= 0f)
            {
                IsJumping = false;                               // peak reached, now falling
            }

            Vector3 wish = GetWishDirection();
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float targetSpeed = sprintHeld ? sprintSpeed : walkSpeed;

            // In the air with no input, keep momentum (so wall-jumps and bar-jumps carry).
            bool airLocked = !grounded && Time.time < airControlLockedUntil;
            if (!airLocked && (grounded || wish.sqrMagnitude > 0.0001f))
            {
                float accel = grounded ? groundAcceleration : airAcceleration;
                horizontal = Vector3.MoveTowards(horizontal, wish * targetSpeed, accel * dt);
            }

            // Jump: buffered press + coyote window.
            if (jumpBufferTimer > 0f && Time.time - lastGroundedTime <= coyoteTime)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                jumpBufferTimer = 0f;
                lastGroundedTime = float.NegativeInfinity;       // stop a double jump inside the coyote window
                IsJumping = true;
                Jumped?.Invoke();
            }
            // Double jump: a fresh press while airborne, with an air jump left.
            else if (jumpBufferTimer > 0f && !grounded && DoubleJumpReady)
            {
                velocity.y = Mathf.Sqrt(doubleJumpHeight * -2f * gravity);
                if (doubleJumpRedirect && wish.sqrMagnitude > 0.01f)
                {
                    float speed = Mathf.Max(horizontal.magnitude, targetSpeed * wish.magnitude);
                    horizontal = wish.normalized * speed;
                }
                airJumpsLeft--;
                doubleJumpReadyTime = Time.time + doubleJumpCooldown;
                jumpBufferTimer = 0f;
                airControlLockedUntil = 0f;                      // a double jump hands control back after a wall-jump
                IsJumping = true;
                Jumped?.Invoke();
                DoubleJumped?.Invoke();
            }

            velocity.y = Mathf.Max(velocity.y + gravity * dt, maxFallSpeed);
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;

            if (controlScheme == ControlScheme.CameraRelative) FaceDirection(wish, dt);

            CollisionFlags flags = controller.Move((velocity + knockback) * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;   // bonk

            // Knockback is kept separate from walking so ground friction doesn't cancel it instantly.
            float fade = controller.isGrounded ? groundKnockbackFade : airKnockbackFade;
            knockback = Vector3.Lerp(knockback, Vector3.zero, fade * dt);

            if (controller.isGrounded)
            {
                SetState(MovementState.Grounded);
            }
            else
            {
                SetState(MovementState.Airborne);
                if (!TryGrabBar()) TryStartWallRun(wish);
            }
        }

        private void FaceDirection(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        // ============================================================ WALL RUNNING
        private bool TryStartWallRun(Vector3 wish)
        {
            if (!WallRunUnlocked || wallLayer.value == 0) return false;
            if (Vector3.Dot(wish, transform.forward) < 0.5f) return false;   // must be pushing forward
            if (HorizontalSpeed < minEntrySpeed) return false;
            if (IsNearGround()) return false;
            if (!FindWall(out RaycastHit hit, out int side)) return false;
            if (hit.collider == lastWall && Time.time < lastWallCooldownEnd) return false;

            wallNormal = hit.normal;
            currentWall = hit.collider;
            WallSide = side;
            wallRunTimer = 0f;
            // Flat: lock height on contact. Otherwise catch the fall but keep any upward speed.
            velocity.y = flatWallRun ? 0f : Mathf.Max(velocity.y, 0f);
            IsJumping = false;
            RefillAirJumps();                                    // touching a wall gives the double jump back
            SetState(MovementState.WallRunning);
            return true;
        }

        private void UpdateWallRun(float dt)
        {
            wallRunTimer += dt;

            if (jumpBufferTimer > 0f) { WallJump(); return; }

            Vector3 wish = GetWishDirection();
            bool stillOnWall = CastForWall(BodyCenter, -wallNormal, WallCastDistance, out RaycastHit hit);
            bool pushingForward = Vector3.Dot(wish, transform.forward) > 0.1f;
            bool outOfTime = wallRunTimer >= wallRunMaxDuration;

            if (!stillOnWall || !pushingForward || outOfTime || !WallRunUnlocked || controller.isGrounded)
            {
                // Reached the edge of the wall while still running forward, so jump off it automatically.
                if (autoJumpAtWallEnd && !stillOnWall && pushingForward && !outOfTime && WallRunUnlocked && !controller.isGrounded)
                {
                    WallJump();
                    return;
                }
                StopWallRun(outOfTime);
                return;
            }

            wallNormal = hit.normal;
            currentWall = hit.collider;

            Vector3 along = AlongWall();
            WallSide = Vector3.Dot(-wallNormal, transform.right) >= 0f ? 1 : -1;

            velocity.x = along.x * wallRunSpeed;
            velocity.z = along.z * wallRunSpeed;
            velocity.y = flatWallRun ? 0f : Mathf.Max(velocity.y + wallRunGravity * dt, wallRunMaxFallSpeed);

            FaceDirection(along, dt);
            controller.Move((velocity - wallNormal * wallStickForce) * dt);
        }

        private void WallJump()
        {
            Vector3 along = AlongWall();
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z)
                                 + wallNormal * wallJumpAwayForce
                                 + along * wallJumpForwardBoost;
            velocity = horizontal + Vector3.up * wallJumpUpForce;

            jumpBufferTimer = 0f;
            airControlLockedUntil = Time.time + wallJumpAirLock;
            StopWallRun(false);
            IsJumping = true;
            Jumped?.Invoke();
        }

        private void StopWallRun(bool exhausted)
        {
            lastWall = currentWall;
            // Running out of time locks that wall until you land. Otherwise it's a short cooldown.
            lastWallCooldownEnd = exhausted ? float.PositiveInfinity : Time.time + sameWallCooldown;
            currentWall = null;
            WallSide = 0;
            SetState(MovementState.Airborne);
        }

        /// <summary>Horizontal direction along the wall that matches the way the player is facing.</summary>
        private Vector3 AlongWall()
        {
            Vector3 along = Vector3.Cross(wallNormal, Vector3.up).normalized;
            return Vector3.Dot(along, transform.forward) < 0f ? -along : along;
        }

        private float WallCastDistance => Mathf.Max(0.01f, controller.radius + wallCheckDistance - wallCheckRadius);

        private bool FindWall(out RaycastHit hit, out int side)
        {
            Vector3 origin = BodyCenter;
            bool right = CastForWall(origin, transform.right, WallCastDistance, out RaycastHit rightHit);
            bool left = CastForWall(origin, -transform.right, WallCastDistance, out RaycastHit leftHit);

            if (right && (!left || rightHit.distance <= leftHit.distance)) { hit = rightHit; side = 1; return true; }
            if (left) { hit = leftHit; side = -1; return true; }

            hit = default; side = 0;
            return false;
        }

        private bool CastForWall(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            if (!Physics.SphereCast(origin, wallCheckRadius, direction, out hit, distance, wallLayer, QueryTriggerInteraction.Ignore))
                return false;
            // Reject floors, ceilings and steep ramps. Only near-vertical surfaces count as walls.
            return Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) <= Mathf.Sin(maxWallSlope * Mathf.Deg2Rad);
        }

        private bool IsNearGround()
        {
            return Physics.Raycast(FeetPosition + Vector3.up * 0.05f, Vector3.down, minHeightAboveGround + 0.05f,
                                   groundLayer, QueryTriggerInteraction.Ignore);
        }

        // ============================================================ MONKEY BARS
        private bool TryGrabBar()
        {
            if (!MonkeyBarsUnlocked || Time.time < regrabAllowedTime) return false;

            Vector3 grabPoint = FeetPosition + Vector3.up * (controller.height + grabReachAboveHead);
            Collider exclude = Time.time < lastBarExcludeUntil ? lastBar : null;
            Collider bar = FindBar(grabPoint, grabRadius, exclude);
            if (bar == null) return false;

            StartHang(bar);
            return true;
        }

        private void StartHang(Collider bar)
        {
            currentBar = bar;
            GetBarLine(bar, out barStart, out Vector3 barEnd);

            Vector3 line = barEnd - barStart;
            barLength = line.magnitude;
            if (barLength < 0.01f) { currentBar = null; return; }
            barAxis = line / barLength;

            // Make "forward along the bar" match the way the player is facing.
            if (Vector3.Dot(barAxis, transform.forward) < 0f)
            {
                barStart = barEnd;
                barAxis = -barAxis;
            }

            barT = Mathf.Clamp(Vector3.Dot(HandPosition - barStart, barAxis), 0f, barLength);

            velocity = Vector3.zero;
            HangSpeed = 0f;
            IsJumping = false;
            WallSide = 0;
            currentWall = null;
            RefillAirJumps();
            SetState(MovementState.Hanging);
        }

        private void UpdateHanging(float dt)
        {
            if (currentBar == null || !currentBar.enabled || !currentBar.gameObject.activeInHierarchy || !MonkeyBarsUnlocked)
            {
                DropFromBar(false);
                return;
            }
            if (dropPressed) { DropFromBar(false); return; }
            if (jumpBufferTimer > 0f) { DropFromBar(true); return; }

            // Move along the bar axis only.
            float input = Vector3.Dot(GetWishDirection(), barAxis);
            HangSpeed = Mathf.MoveTowards(HangSpeed, input * hangMoveSpeed, hangMoveSpeed * 6f * dt);
            barT += HangSpeed * dt;

            if (barT > barLength || barT < 0f)
            {
                // Hand over to a connecting bar if there is one, otherwise stop at the end.
                Vector3 probe = barStart + barAxis * barT;
                Collider next = FindBar(probe, grabRadius * 0.75f, currentBar);
                if (next != null)
                {
                    float keepSpeed = HangSpeed;
                    StartHang(next);
                    HangSpeed = keepSpeed;
                }
                else
                {
                    barT = Mathf.Clamp(barT, 0f, barLength);
                    HangSpeed = 0f;
                }
            }

            // Lock the body under the bar: fixed height, fixed line, smooth snap.
            Vector3 target = barStart + barAxis * barT - Vector3.up * hangOffset;
            Vector3 delta = target - transform.position;
            float maxStep = hangSnapSpeed * dt;
            controller.Move(delta.sqrMagnitude <= maxStep * maxStep ? delta : delta.normalized * maxStep);

            if (controlScheme == ControlScheme.CameraRelative) FaceDirection(barAxis, dt);

            velocity = barAxis * HangSpeed;   // remembered so momentum carries when you let go
        }

        private void DropFromBar(bool jump)
        {
            velocity = barAxis * HangSpeed;
            if (jump)
            {
                velocity += Vector3.up * dropJumpUpForce + transform.forward * dropJumpForwardForce;
                IsJumping = true;
                Jumped?.Invoke();
            }

            jumpBufferTimer = 0f;
            lastBar = currentBar;
            lastBarExcludeUntil = Time.time + 1f;   // don't instantly re-grab the bar you just left
            regrabAllowedTime = Time.time + regrabCooldown;
            currentBar = null;
            HangSpeed = 0f;
            SetState(MovementState.Airborne);
        }

        private Collider FindBar(Vector3 point, float radius, Collider exclude)
        {
            int mask = monkeyBarLayer.value != 0 ? monkeyBarLayer.value : Physics.AllLayers;
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlapBuffer, mask, QueryTriggerInteraction.Collide);

            Collider best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = overlapBuffer[i];
                if (c == exclude || !IsBar(c)) continue;
                float d = (c.ClosestPoint(point) - point).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        private bool IsBar(Collider c)
        {
            if (c == null || c.transform.IsChildOf(transform)) return false;
            if (monkeyBarLayer.value != 0) return (monkeyBarLayer.value & (1 << c.gameObject.layer)) != 0;
            // Comparing with == (not CompareTag) avoids an error if the tag hasn't been created yet.
            return !string.IsNullOrEmpty(monkeyBarTag) && c.gameObject.tag == monkeyBarTag;
        }

        /// <summary>Works out the bar's line from its collider: the longest horizontal side of a BoxCollider, or the longest side of its bounds.</summary>
        private static void GetBarLine(Collider bar, out Vector3 start, out Vector3 end)
        {
            if (bar is BoxCollider box)
            {
                Transform t = box.transform;
                Vector3 s = t.lossyScale;
                float[] size = { Mathf.Abs(box.size.x * s.x), Mathf.Abs(box.size.y * s.y), Mathf.Abs(box.size.z * s.z) };
                Vector3[] axes = { t.right, t.up, t.forward };

                int best = -1;
                float bestLength = 0f;
                for (int i = 0; i < 3; i++)
                {
                    if (Mathf.Abs(Vector3.Dot(axes[i], Vector3.up)) > 0.7f) continue;   // ignore vertical axes
                    if (size[i] > bestLength) { bestLength = size[i]; best = i; }
                }

                if (best >= 0)
                {
                    Vector3 center = t.TransformPoint(box.center);
                    Vector3 half = axes[best] * (bestLength * 0.5f);
                    start = center - half;
                    end = center + half;
                    return;
                }
            }

            Bounds b = bar.bounds;
            if (b.size.x >= b.size.z)
            {
                start = new Vector3(b.min.x, b.center.y, b.center.z);
                end = new Vector3(b.max.x, b.center.y, b.center.z);
            }
            else
            {
                start = new Vector3(b.center.x, b.center.y, b.min.z);
                end = new Vector3(b.center.x, b.center.y, b.max.z);
            }
        }

        // ============================================================ CAMERA TILT
        private void UpdateCameraTilt(float dt)
        {
            // In Unity, a positive Z roll leans left. Wall on the right (+1) with tilt-away gives a lean left.
            float target = IsWallRunning ? wallRunTiltAngle * WallSide * (tiltAwayFromWall ? 1f : -1f) : 0f;
            CameraRoll = Mathf.Lerp(CameraRoll, target, 1f - Mathf.Exp(-tiltSpeed * dt));
            ApplyCameraTilt();
        }

        private void ApplyCameraTilt()
        {
            if (controlScheme == ControlScheme.MouseYaw && cameraTransform != null)
            {
                // MouseYaw: the camera carries the up/down look, plus the wallrun roll unless a separate pivot is set.
                bool rollOnCamera = cameraTiltPivot == null || cameraTiltPivot == cameraTransform;
                cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, rollOnCamera ? CameraRoll : 0f);
                if (!rollOnCamera)
                    cameraTiltPivot.localRotation = tiltBaseRotation * Quaternion.Euler(0f, 0f, CameraRoll);
            }
            else if (cameraTiltPivot != null)
            {
                cameraTiltPivot.localRotation = tiltBaseRotation * Quaternion.Euler(0f, 0f, CameraRoll);
            }
        }

        // ============================================================ KNOCKBACK
        /// <summary>Push the player (called by obstacles). XZ is a sideways shove that fades; Y is an upward launch.</summary>
        public void ApplyKnockback(Vector3 force)
        {
            if (IsHanging) DropFromBar(false);
            if (IsWallRunning) StopWallRun(false);

            knockback = new Vector3(force.x, 0f, force.z);
            velocity.y = Mathf.Max(velocity.y, force.y);
            lastGroundedTime = float.NegativeInfinity;   // no coyote-time jump mid-knockback
            SetState(MovementState.Airborne);
        }

        // ============================================================ RESPAWN (IRespawnable)
        public void Respawn(Vector3 position, Quaternion rotation)
        {
            // Checkpoint spawn points mark where the FEET go. If the capsule's pivot isn't at its
            // bottom (e.g. Center = 0,0,0), lift it so the player doesn't spawn half inside the floor.
            position += Vector3.up * Mathf.Max(0f, (controller.height * 0.5f - controller.center.y) * transform.lossyScale.y);

            // A CharacterController overrides transform changes while it's enabled, so turn it off for the teleport.
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;

            // Clear all momentum and special states.
            velocity = Vector3.zero;
            knockback = Vector3.zero;
            HangSpeed = 0f;
            WallSide = 0;
            IsJumping = false;
            jumpBufferTimer = 0f;
            lastGroundedTime = float.NegativeInfinity;
            currentBar = null;
            lastBar = null;
            currentWall = null;
            lastWall = null;
            regrabAllowedTime = Time.time + regrabCooldown;
            airControlLockedUntil = 0f;
            airJumpsLeft = extraAirJumps;
            doubleJumpReadyTime = 0f;

            CameraRoll = 0f;
            ApplyCameraTilt();

            SetState(MovementState.Airborne);
            Respawned?.Invoke();
        }

        // ============================================================ HELPERS
        private void RefillAirJumps()
        {
            airJumpsLeft = Mathf.Max(0, extraAirJumps);
        }

        private void SetState(MovementState newState)
        {
            if (State == newState) return;
            MovementState old = State;
            State = newState;
            StateChanged?.Invoke(old, newState);
        }

#if UNITY_EDITOR
        // Select the Player to see the wall checks (cyan) and grab sphere (magenta) in the Scene view.
        private void OnDrawGizmosSelected()
        {
            var cc = GetComponent<CharacterController>();
            if (cc == null) return;
            Vector3 center = transform.TransformPoint(cc.center);
            Vector3 feet = center - Vector3.up * (cc.height * 0.5f);
            float reach = cc.radius + wallCheckDistance;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(center, center + transform.right * reach);
            Gizmos.DrawLine(center, center - transform.right * reach);
            Gizmos.DrawWireSphere(center + transform.right * reach, wallCheckRadius);
            Gizmos.DrawWireSphere(center - transform.right * reach, wallCheckRadius);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(feet + Vector3.up * (cc.height + grabReachAboveHead), grabRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(feet, feet + Vector3.down * minHeightAboveGround);
        }
#endif
    }
}
