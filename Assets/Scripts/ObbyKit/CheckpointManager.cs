// =====================================================================================
//  CheckpointManager.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  Central, scene-wide record of "where does the player respawn?".
//
//  UNITY SETUP
//  1. Create an empty GameObject in the scene called "CheckpointManager" and add this
//     component. Only ONE per scene (extra copies destroy themselves).
//  2. (Optional) Assign "Initial Spawn Point" — an empty Transform at the start of the
//     course. If left empty, the player's position when the game starts is used.
//  3. Nothing else needs wiring: Checkpoint and VoidZone find this through
//     CheckpointManager.Instance, and PlayerController registers itself in Start().
//
//  DESIGN NOTES
//  - Anything that can be respawned implements IRespawnable (PlayerController does).
//    VoidZone and Checkpoint only talk to that interface, never to PlayerController
//    directly, so you can swap in a different player script later.
//  - [DefaultExecutionOrder(-100)] makes sure Instance exists before other scripts'
//    Awake/Start run.
// =====================================================================================

using System;
using UnityEngine;

namespace ObbyKit
{
    /// <summary>Anything that can be teleported back to a checkpoint.</summary>
    public interface IRespawnable
    {
        /// <summary>Teleport to the given pose and clear all momentum.</summary>
        void Respawn(Vector3 position, Quaternion rotation);

        /// <summary>The object's transform (used for height checks and the default spawn).</summary>
        Transform Transform { get; }
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class CheckpointManager : MonoBehaviour
    {
        /// <summary>Global access point. Null if no manager is in the scene.</summary>
        public static CheckpointManager Instance { get; private set; }

        // ---------------------------------------------------------------- Inspector
        [Header("Spawn")]
        [Tooltip("Where the player respawns before touching any checkpoint. " +
                 "Leave empty to use the player's starting position.")]
        [SerializeField] private Transform initialSpawnPoint;

        [Header("Rules")]
        [Tooltip("If ON, touching a checkpoint with a LOWER Index than the current one " +
                 "does not move the respawn point backwards.")]
        [SerializeField] private bool onlyAdvanceForward = true;

        [Tooltip("Minimum seconds between two respawns. Stops the void trigger and the " +
                 "height check from both firing on the same fall.")]
        [SerializeField] private float respawnCooldown = 0.25f;

        // ---------------------------------------------------------------- Public state
        /// <summary>The checkpoint the player will respawn at (null = initial spawn).</summary>
        public Checkpoint ActiveCheckpoint { get; private set; }
        public Vector3 RespawnPosition { get; private set; }
        public Quaternion RespawnRotation { get; private set; } = Quaternion.identity;

        /// <summary>The registered player (set by PlayerController.Start).</summary>
        public IRespawnable Player { get; private set; }

        /// <summary>Raised when a new checkpoint becomes the active one.</summary>
        public event Action<Checkpoint> CheckpointActivated;

        /// <summary>Raised after the player has been teleported back.</summary>
        public event Action Respawned;

        // ---------------------------------------------------------------- Private
        private bool hasSpawn;
        private float lastRespawnTime = float.NegativeInfinity;
        private Vector3 initialPosition;
        private Quaternion initialRotation = Quaternion.identity;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[ObbyKit] A second CheckpointManager was found on '{name}'. " +
                                 "Only one is allowed per scene; removing the duplicate.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (initialSpawnPoint != null)
                SetInitialSpawn(initialSpawnPoint.position, FlatRotation(initialSpawnPoint.forward));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================ API

        /// <summary>Called by the player when it spawns. Sets the default spawn if none was given.</summary>
        public void RegisterPlayer(IRespawnable player)
        {
            Player = player;
            if (!hasSpawn && player != null)
                SetInitialSpawn(player.Transform.position, FlatRotation(player.Transform.forward));
        }

        /// <summary>
        /// Try to make <paramref name="checkpoint"/> the active respawn point.
        /// Returns true if it became active (so the flag should play its feedback).
        /// </summary>
        public bool TryActivate(Checkpoint checkpoint)
        {
            if (checkpoint == null || checkpoint == ActiveCheckpoint) return false;

            if (onlyAdvanceForward && ActiveCheckpoint != null && checkpoint.Index < ActiveCheckpoint.Index)
                return false;

            ActiveCheckpoint = checkpoint;
            RespawnPosition = checkpoint.SpawnPosition;
            RespawnRotation = checkpoint.SpawnRotation;
            hasSpawn = true;

            CheckpointActivated?.Invoke(checkpoint);
            return true;
        }

        /// <summary>
        /// Teleport a respawnable (default: the registered player) to the active checkpoint.
        /// Returns false if nothing happened (no target, or still on cooldown).
        /// </summary>
        public bool RespawnPlayer(IRespawnable target = null)
        {
            target ??= Player;
            if (target == null) return false;
            if (Time.time - lastRespawnTime < respawnCooldown) return false;

            lastRespawnTime = Time.time;
            target.Respawn(RespawnPosition, RespawnRotation);
            Respawned?.Invoke();
            return true;
        }

        /// <summary>Forget all checkpoints and go back to the initial spawn (e.g. a "Restart" button).</summary>
        public void ResetProgress()
        {
            ActiveCheckpoint = null;
            RespawnPosition = initialPosition;
            RespawnRotation = initialRotation;
        }

        // ================================================================ Helpers

        private void SetInitialSpawn(Vector3 position, Quaternion rotation)
        {
            initialPosition = position;
            initialRotation = rotation;
            RespawnPosition = position;
            RespawnRotation = rotation;
            hasSpawn = true;
        }

        /// <summary>Yaw-only rotation so the player never respawns tilted.</summary>
        public static Quaternion FlatRotation(Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            return forward.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
