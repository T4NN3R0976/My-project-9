// =====================================================================================
//  LaserHazard.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  A laser beam that sends the player back to their last checkpoint when touched.
//
//  UNITY SETUP
//  1. Put this on an object with a trigger collider shaped like the beam (a thin
//     BoxCollider is easiest). Adding the script ticks "Is Trigger" for you.
//  2. Needs a CheckpointManager in the scene, which handles the respawn.
//  3. Optional: "Bob Height" moves the beam up and down so players have to time their jump.
//     "Move Root" picks what moves (for example the glowing beam meshes together with this
//     trigger). If empty, this object moves.
//  The glow itself comes from the ObbyKit/Laser shader on the beam meshes.
// =====================================================================================

using UnityEngine;
using UnityEngine.Events;

namespace ObbyKit
{
    [RequireComponent(typeof(Collider))]
    public class LaserHazard : MonoBehaviour
    {
        [Header("Motion (optional)")]
        [Tooltip("How far (metres) the beam moves up and down. 0 = stays still.")]
        [SerializeField] private float bobHeight = 0f;
        [SerializeField] private float bobSpeed = 1.5f;
        [Tooltip("What moves when bobbing. Defaults to this object.")]
        [SerializeField] private Transform moveRoot;

        [Header("Events")]
        [Tooltip("Optional: sounds or effects when the player gets zapped.")]
        [SerializeField] private UnityEvent onPlayerHit;

        private Vector3 startLocal;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (moveRoot == null) moveRoot = transform;
            startLocal = moveRoot.localPosition;
        }

        private void Update()
        {
            if (bobHeight > 0f)
                moveRoot.localPosition = startLocal + Vector3.up * ((Mathf.Sin(Time.time * bobSpeed) * 0.5f + 0.5f) * bobHeight);
        }

        private void OnTriggerEnter(Collider other)
        {
            var respawnable = other.GetComponentInParent<IRespawnable>();
            if (respawnable == null) return;

            var manager = CheckpointManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[ObbyKit] A LaserHazard hit the player but there is no CheckpointManager in the scene.", this);
                return;
            }
            if (manager.RespawnPlayer(respawnable)) onPlayerHit?.Invoke();
        }
    }
}
