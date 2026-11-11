using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Walking, sprinting, jumping and mouse look for a 3D character.
// W = forward, S = backward, A = strafe left, D = strafe right.
// Moving the mouse left/right turns the player; up/down tilts the camera.
// Attach to your player object. A CharacterController is added automatically.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;

    [Header("Jumping")]
    public float jumpHeight = 1.5f;
    public float gravity = -20f;
    public float coyoteTime = 0.15f;       // can still jump briefly after walking off a ledge
    public float jumpBufferTime = 0.15f;   // jump pressed slightly before landing still counts

    [Header("Third-Person Camera")]
    [Tooltip("Leave empty and the script uses the Main Camera.")]
    public Camera playerCamera;
    [Tooltip("Leave empty and the script creates a pivot automatically.")]
    public Transform cameraTransform;      // the pivot the camera orbits around
    public float pivotHeight = 1.5f;       // how high above the player's feet the camera looks
    public float cameraDistance = 1f;      // how far behind the player the camera sits
    public float cameraSideOffset = 0f;    // e.g. 0.6 for an over-the-shoulder view
    public float cameraCollisionRadius = 0.3f;
    public LayerMask cameraCollisionLayers = ~0;

    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    public float minLookAngle = -80f;      // how far you can look down
    public float maxLookAngle = 80f;       // how far you can look up
    public bool invertY = false;

    private CharacterController controller;
    private Vector3 velocity;              // vertical velocity (gravity / jump)
    private float lastGroundedTime = -1f;
    private float lastJumpPressedTime = -1f;
    private float pitch;                   // current up/down camera angle
    private Vector3 knockback;             // sideways push from obstacles, fades over time

    [Header("Knockback")]
    [Tooltip("How quickly a knockback fades while on the ground.")]
    public float groundKnockbackFade = 6f;
    [Tooltip("How quickly a knockback fades in the air (lower = fly further).")]
    public float airKnockbackFade = 1f;

    public bool IsGrounded => controller.isGrounded;
    public bool IsSprinting { get; private set; }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        LockCursor(true);
    }

    void Update()
    {
        HandleCursor();
        HandleMouseLook();
        HandleMovement();
    }

    // ---------- Mouse look ----------
    void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 mouse = ReadMouseDelta() * mouseSensitivity;

        // Left/right: rotate the whole player
        transform.Rotate(Vector3.up * mouse.x);

        // Up/down: tilt only the camera
        pitch += invertY ? mouse.y : -mouse.y;
        pitch = Mathf.Clamp(pitch, minLookAngle, maxLookAngle);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // ---------- Walking / sprinting / jumping ----------
    void HandleMovement()
    {
        Vector2 input = ReadMoveInput();
        bool sprintHeld = ReadSprintHeld();
        if (ReadJumpPressed())
            lastJumpPressedTime = Time.time;

        // Ground check
        if (controller.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (velocity.y < 0f)
                velocity.y = -2f; // keeps the character snapped to the ground
        }

        // Move relative to where the player is facing:
        // W/S = forward/back, A/D = strafe left/right (no turning)
        Vector3 move = transform.right * input.x + transform.forward * input.y;
        move = Vector3.ClampMagnitude(move, 1f);

        // Only sprint when moving forward (not backwards or purely sideways)
        IsSprinting = sprintHeld && input.y > 0.1f;
        float speed = IsSprinting ? sprintSpeed : walkSpeed;

        // Jumping
        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        bool jumpQueued = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (canJump && jumpQueued)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastJumpPressedTime = -1f;
            lastGroundedTime = -1f;
        }

        // Gravity
        velocity.y += gravity * Time.deltaTime;

        // Apply (walking + any knockback + gravity/jump)
        Vector3 finalMove = move * speed + knockback + Vector3.up * velocity.y;
        controller.Move(finalMove * Time.deltaTime);

        // Let the knockback fade out
        float fade = controller.isGrounded ? groundKnockbackFade : airKnockbackFade;
        knockback = Vector3.Lerp(knockback, Vector3.zero, fade * Time.deltaTime);
    }

    // Called by obstacles like SpinningBeam to push the player
    public void ApplyKnockback(Vector3 force)
    {
        knockback = new Vector3(force.x, 0f, force.z);
        velocity.y = Mathf.Max(velocity.y, force.y); // launch upwards
        lastGroundedTime = -1f;                      // no coyote-time jump mid-knockback
    }

    // ---------- Cursor: Escape frees the mouse, clicking the game locks it again ----------
    void HandleCursor()
    {
        if (ReadEscapePressed()) LockCursor(false);
        else if (ReadClickPressed() && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ===== Input helpers: work with both the new Input System and the old Input Manager =====

    Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 v = Vector2.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1;
        }
        var pad = Gamepad.current;
        if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > v.sqrMagnitude)
            v = pad.leftStick.ReadValue();
        return v;
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    Vector2 ReadMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 d = Vector2.zero;
        if (Mouse.current != null)
            d = Mouse.current.delta.ReadValue() * 0.1f; // scaled to roughly match the old system
        if (Gamepad.current != null)
            d += Gamepad.current.rightStick.ReadValue() * 2f;
        return d;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }

    bool ReadSprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        return (kb != null && kb.leftShiftKey.isPressed) ||
               (pad != null && pad.leftStickButton.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    bool ReadJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        return (kb != null && kb.spaceKey.wasPressedThisFrame) ||
               (pad != null && pad.buttonSouth.wasPressedThisFrame);
#else
        return Input.GetButtonDown("Jump");
#endif
    }

    bool ReadEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    bool ReadClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }
}