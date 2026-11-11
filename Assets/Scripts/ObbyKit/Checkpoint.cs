// =====================================================================================
//  Checkpoint.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  Put this on each FLAG. When the player walks into its trigger, it becomes the
//  respawn point.
//
//  UNITY SETUP
//  1. Select your flag GameObject (or an empty parent around it).
//  2. Add a collider that covers the area the player should touch — a BoxCollider is
//     easiest — and tick "Is Trigger". (Adding this script ticks it for you.)
//     Keep any SOLID collider for the flag pole on a separate child object.
//  3. Add this Checkpoint component.
//  4. Set "Index" in course order: 0, 1, 2, ...
//  5. RECOMMENDED: create an empty child called "SpawnPoint", place it on the platform
//     next to the flag (not inside the pole) with its blue Z arrow facing the way the
//     player should look, and drag it into "Spawn Point".
//  6. "Level Number": set this on the FIRST flag of each level (for example 3 on the flag
//     that starts Level 3). That switches on that level's movement rules — wallrunning
//     on Level 3, monkey bars on Level 5. Leave it at 0 on the other flags.
//  7. Feedback (optional): drag the flag cloth's MeshRenderer into "Flag Renderers"
//     to recolour it, a ParticleSystem into "Activation Particles", and/or an
//     AudioSource. Anything else (animations, UI popups) can go in "On Activated".
//
//  The player is detected by its IRespawnable component (PlayerController), so no tag
//  is needed. A CharacterController DOES fire OnTriggerEnter without a Rigidbody.
// =====================================================================================

using UnityEngine;
using UnityEngine.Events;

namespace ObbyKit
{
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        // ---------------------------------------------------------------- Inspector
        [Header("Ordering")]
        [Tooltip("Order along the course (0 = first). Used by 'Only Advance Forward' on the manager.")]
        [SerializeField] private int index;

        [Tooltip("If above 0, touching this flag sets the player's Current Level " +
                 "(3 = wallrunning, 5 = monkey bars). 0 = leave the level alone.")]
        [SerializeField] private int levelNumber;

        [Header("Spawn")]
        [Tooltip("Where the player reappears. Its blue Z arrow is the facing direction. " +
                 "If empty, the flag's own position and forward are used.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Added to the spawn position. A small lift stops the player spawning inside the floor.")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.1f, 0f);

        [Header("Feedback (all optional)")]
        [Tooltip("Renderers to recolour on activation (e.g. the flag cloth).")]
        [SerializeField] private Renderer[] flagRenderers;
        [SerializeField] private Color activatedColor = new Color(0.2f, 1f, 0.3f);
        [SerializeField] private ParticleSystem activationParticles;
        [SerializeField] private AudioSource activationSound;
        [Tooltip("Extra hooks: animation, UI text, etc.")]
        [SerializeField] private UnityEvent onActivated;

        // ---------------------------------------------------------------- Public read-only
        public int Index => index;
        public int LevelNumber => levelNumber;
        public bool IsActivated { get; private set; }

        public Vector3 SpawnPosition =>
            (spawnPoint != null ? spawnPoint.position : transform.position) + spawnOffset;

        public Quaternion SpawnRotation =>
            CheckpointManager.FlatRotation((spawnPoint != null ? spawnPoint : transform).forward);

        // ---------------------------------------------------------------- Private
        // URP/HDRP Lit uses _BaseColor; Built-in Standard uses _Color. Setting both is harmless.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock propertyBlock;

        // Reset() runs when the component is first added in the editor.
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (!col.isTrigger)
            {
                Debug.LogWarning($"[ObbyKit] Checkpoint '{name}': collider was not a trigger, so 'Is Trigger' " +
                                 "has been switched on for you. Put any solid pole collider on a child object.", this);
                col.isTrigger = true;
            }
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<IRespawnable>() == null) return;

            var manager = CheckpointManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[ObbyKit] Player touched a Checkpoint but there is no CheckpointManager in the scene.", this);
                return;
            }

            if (manager.TryActivate(this))
                PlayFeedback();
        }

        private void PlayFeedback()
        {
            bool firstTime = !IsActivated;
            IsActivated = true;

            if (flagRenderers != null)
            {
                foreach (var r in flagRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetColor(BaseColorId, activatedColor);
                    propertyBlock.SetColor(ColorId, activatedColor);
                    r.SetPropertyBlock(propertyBlock);
                }
            }

            // Only celebrate the first time you reach a flag, not every time you re-touch it.
            if (firstTime)
            {
                if (activationParticles != null) activationParticles.Play();
                if (activationSound != null) activationSound.Play();
            }

            onActivated?.Invoke();
        }

#if UNITY_EDITOR
        // Scene-view helper: shows where and which way the player will respawn.
        private void OnDrawGizmos()
        {
            Vector3 p = SpawnPosition;
            Gizmos.color = IsActivated ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(p + Vector3.up * 0.5f, 0.35f);
            Gizmos.DrawLine(p + Vector3.up * 0.5f, p + Vector3.up * 0.5f + SpawnRotation * Vector3.forward);
        }
#endif
    }
}
