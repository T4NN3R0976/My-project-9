using UnityEngine;

// A spinning obstacle that knocks the player away when it hits them.
// Put this on an empty "pivot" object at the centre of the spin,
// with the beam (a stretched cube) as a child of it.
public class SpinningBeam : MonoBehaviour
{
    [Header("Spinning")]
    [Tooltip("Degrees per second. Use a negative number to spin the other way.")]
    public float spinSpeed = 90f;
    [Tooltip("Axis to spin around. (0,1,0) = flat like a sweeper; (1,0,0) = like a windmill.")]
    public Vector3 spinAxis = Vector3.up;

    [Header("Knockback")]
    [Tooltip("How hard the player gets pushed sideways.")]
    public float knockbackForce = 15f;
    [Tooltip("How high the player gets launched.")]
    public float knockbackUpForce = 6f;
    [Tooltip("Seconds before the same beam can hit the player again.")]
    public float hitCooldown = 0.5f;

    private float lastHitTime = -10f;

    void Awake()
    {
        // Moving colliders should have a kinematic Rigidbody so collisions are reliable
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        Quaternion step = Quaternion.AngleAxis(spinSpeed * Time.fixedDeltaTime, spinAxis.normalized);
        rb.MoveRotation(step * rb.rotation);
    }

    // Called by the trigger collider on the beam (child objects report to this Rigidbody)
    void OnTriggerStay(Collider other)
    {
        if (Time.time - lastHitTime < hitCooldown) return;

        // Works with either movement script: the new ObbyKit PlayerController or the old PlayerMovement.
        ObbyKit.PlayerController obbyPlayer = other.GetComponentInParent<ObbyKit.PlayerController>();
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        bool useObby = obbyPlayer != null && obbyPlayer.enabled;
        if (!useObby && (player == null || !player.enabled)) return;

        lastHitTime = Time.time;

        // Work out which way the beam is sweeping at the point where it hit the player
        Vector3 axis = spinAxis.normalized * Mathf.Sign(spinSpeed);
        Vector3 fromCentre = other.transform.position - transform.position;
        Vector3 sweepDir = Vector3.Cross(axis, fromCentre);
        sweepDir.y = 0f;

        // Fall back to pushing straight outwards if the direction is unclear
        if (sweepDir.sqrMagnitude < 0.01f)
        {
            sweepDir = fromCentre;
            sweepDir.y = 0f;
        }

        Vector3 knock = sweepDir.normalized * knockbackForce + Vector3.up * knockbackUpForce;
        if (useObby) obbyPlayer.ApplyKnockback(knock);
        else player.ApplyKnockback(knock);
    }
}