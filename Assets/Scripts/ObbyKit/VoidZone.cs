// =====================================================================================
//  VoidZone.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  The ocean / void under the level. Touch it (or fall below it) and you respawn at the
//  last checkpoint with all momentum cleared.
//
//  UNITY SETUP
//  1. Create a GameObject under the level (e.g. "Ocean" or an empty "VoidZone").
//  2. Add a BoxCollider and size it to cover the WHOLE area under the course, with
//     some extra around the edges. Make it fairly thick (5+ units tall) so a fast fall
//     can't skip through it in a single frame. Adding this script ticks "Is Trigger".
//     - If your water is a visible plane mesh, keep the mesh for looks and put this
//       BoxCollider on it (or on a child). Remove any MeshCollider from the water, or
//       the player will stand on it.
//  3. Add this VoidZone component.
//  4. "Use Height Check" (ON by default) is a safety net: if the player ends up below
//     the TOP of this box anywhere (even outside its edges), they respawn. Turn it off
//     if any part of your course is meant to sit below the water line.
//
//  What happens on respawn (inside PlayerController.Respawn):
//   - the CharacterController is briefly disabled, so the teleport isn't blocked;
//   - position and rotation are set to the checkpoint's;
//   - velocity, wallrun and hanging state are all cleared.
//  If you attach a Rigidbody-based player instead, implement IRespawnable and zero
//  rb.velocity / rb.angularVelocity inside Respawn().
// =====================================================================================

using UnityEngine;
using UnityEngine.Events;

namespace ObbyKit
{
    [RequireComponent(typeof(Collider))]
    public class VoidZone : MonoBehaviour
    {
        [Tooltip("Also respawn the player whenever they are below the top of this zone, " +
                 "even if they never touched the trigger.")]
        [SerializeField] private bool useHeightCheck = true;

        [Tooltip("Moves the height-check line up (+) or down (-) from the top of the collider.")]
        [SerializeField] private float heightCheckOffset = 0f;

        [Tooltip("Optional hooks: a splash sound or effect, a death counter, etc.")]
        [SerializeField] private UnityEvent onPlayerFell;

        private Collider zone;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            zone = GetComponent<Collider>();
            if (!zone.isTrigger)
            {
                Debug.LogWarning($"[ObbyKit] VoidZone '{name}': collider was not a trigger, so 'Is Trigger' " +
                                 "has been switched on for you.", this);
                zone.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var respawnable = other.GetComponentInParent<IRespawnable>();
            if (respawnable != null) SendBack(respawnable);
        }

        private void Update()
        {
            if (!useHeightCheck) return;

            var manager = CheckpointManager.Instance;
            if (manager == null || manager.Player == null) return;

            float killY = zone.bounds.max.y + heightCheckOffset;
            if (manager.Player.Transform.position.y < killY)
                SendBack(manager.Player);
        }

        private void SendBack(IRespawnable respawnable)
        {
            var manager = CheckpointManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[ObbyKit] Player fell into a VoidZone but there is no CheckpointManager in the scene.", this);
                return;
            }

            // RespawnPlayer returns false while on cooldown, so the event only fires once per fall.
            if (manager.RespawnPlayer(respawnable))
                onPlayerFell?.Invoke();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(0f, 0.4f, 1f, 0.15f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        }
#endif
    }
}
