// =====================================================================================
//  ThirdPersonCamera.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  Over-the-shoulder orbit camera for the ObbyKit player.
//   • Mouse orbits around the player (the player turns to face where it moves).
//   • Scroll wheel zooms in and out.
//   • Pulls in when a wall would block the view.
//   • Leans with the player's wallrun tilt (reads PlayerController.CameraRoll).
//   • Widens the field of view a little while sprinting.
//   • Snaps behind the player on respawn.
//
//  UNITY SETUP
//  1. Put this on the scene's Main Camera. The camera must NOT be a child of the player.
//  2. Drag the player into "Target" (or leave it empty to find the PlayerController).
//  3. On the player's PlayerController, set Control Scheme = CameraRelative and
//     Camera Transform = this camera.
//  4. Collision Mask: everything except the Player layer.
//  Esc frees the mouse cursor; clicking in the Game view locks it again.
// =====================================================================================

using UnityEngine;

namespace ObbyKit
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The player to follow. Leave empty to find the PlayerController automatically.")]
        [SerializeField] private Transform target;
        [Tooltip("Point the camera orbits around, measured from the player's feet.")]
        [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
        [Tooltip("Sideways offset for an over-the-shoulder view (0 = centred).")]
        [SerializeField] private float shoulderOffset = 0.35f;
        [Tooltip("How quickly the camera catches up with the player.")]
        [SerializeField] private float followSharpness = 18f;

        [Header("Orbit")]
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private bool invertY = false;
        [SerializeField] private float minPitch = -30f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float startPitch = 15f;
        [SerializeField] private bool lockCursor = true;

        [Header("Distance")]
        [SerializeField] private float distance = 4.5f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 9f;
        [Tooltip("Metres zoomed per scroll-wheel notch × 0.1 (10 = 1 m per notch).")]
        [SerializeField] private float zoomSpeed = 10f;

        [Header("Collision")]
        [Tooltip("Layers that block the camera. Exclude the Player layer.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.25f;
        [Tooltip("How quickly the camera moves back out after an obstacle clears.")]
        [SerializeField] private float recoverSharpness = 6f;

        [Header("Feel")]
        [Tooltip("Extra field of view (degrees) at full sprint.")]
        [SerializeField] private float sprintFovBoost = 6f;
        [SerializeField] private float fovSharpness = 6f;

        private PlayerController player;
        private Camera cam;
        private float yaw, pitch, currentDistance, baseFov;
        private Vector3 smoothedPivot;

        private void Start()
        {
            cam = GetComponent<Camera>();
            baseFov = cam.fieldOfView;

            if (target == null)
            {
                var found = FindObjectOfType<PlayerController>();
                if (found != null) target = found.transform;
            }
            if (target != null) player = target.GetComponent<PlayerController>();
            if (player != null) player.Respawned += SnapBehindPlayer;

            pitch = startPitch;
            currentDistance = distance;
            if (target != null)
            {
                yaw = target.eulerAngles.y;
                smoothedPivot = target.position + pivotOffset;
            }
            if (lockCursor) SetCursorLocked(true);
        }

        private void OnDestroy()
        {
            if (player != null) player.Respawned -= SnapBehindPlayer;
        }

        private void SnapBehindPlayer()
        {
            if (target == null) return;
            yaw = target.eulerAngles.y;
            pitch = startPitch;
            smoothedPivot = target.position + pivotOffset;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

            // Cursor: Esc frees it, a click locks it again.
            if (lockCursor)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
                else if (Input.GetMouseButtonDown(0)) SetCursorLocked(true);
            }

            // Orbit with the mouse (only while the cursor is locked, so you can click UI when it's free).
            if (!lockCursor || Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch + (invertY ? my : -my), minPitch, maxPitch);
            }

            // One scroll notch is 0.1 on this axis, so zoomSpeed 10 = 1 m per notch.
            distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * zoomSpeed, minDistance, maxDistance);

            // Follow the player smoothly, but jump straight there after a teleport.
            Vector3 desiredPivot = target.position + pivotOffset;
            if ((desiredPivot - smoothedPivot).sqrMagnitude > 25f) smoothedPivot = desiredPivot;
            else smoothedPivot = Vector3.Lerp(smoothedPivot, desiredPivot, 1f - Mathf.Exp(-followSharpness * dt));

            float roll = player != null ? player.CameraRoll : 0f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, roll);
            Vector3 shoulderPoint = smoothedPivot + rotation * new Vector3(shoulderOffset, 0f, 0f);
            Vector3 back = rotation * Vector3.back;

            // Pull in when something is between the player and the camera; ease back out afterwards.
            float wanted = distance;
            if (Physics.SphereCast(smoothedPivot, collisionRadius, (shoulderPoint + back * distance - smoothedPivot).normalized,
                                   out RaycastHit hit, distance + shoulderOffset, collisionMask, QueryTriggerInteraction.Ignore))
                wanted = Mathf.Max(0.3f, hit.distance - shoulderOffset * 0.5f);
            currentDistance = wanted < currentDistance
                ? wanted
                : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-recoverSharpness * dt));

            transform.SetPositionAndRotation(shoulderPoint + back * currentDistance, rotation);

            // Slight FOV kick at sprint speed.
            if (player != null && sprintFovBoost > 0f)
            {
                float t = Mathf.InverseLerp(player.WalkSpeed, player.SprintSpeed, player.HorizontalSpeed);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + sprintFovBoost * t, 1f - Mathf.Exp(-fovSharpness * dt));
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
