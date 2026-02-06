using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GV;

namespace GV.PowerUps
{
    public class TeleportPowerUp : MonoBehaviour
    {
        [Header("Power Up Settings")]
        public bool manualTriggerOnly = false;
        public PowerUpType powerUpType = PowerUpType.Teleport;

        [Header("Teleport Settings")]
        [Tooltip("Number of checkpoints to jump forward.")]
        public int checkpointsToJump = 6;

        [Tooltip("Reappear this many meters BEFORE the target checkpoint along the path.")]
        public float behindDistanceOnPath = 4.0f;

        public float upOffset = 0.0f;
        public float rightOffset = 0.0f;

        [Header("Physics")]
        public bool keepVelocity = true;
        
        [Header("SFX")]
        public AudioClip teleportSfx;
        [Tooltip("Optional: Instantiate this GameObject when teleporting (e.g. for audio prefab).")]
        public GameObject teleportSoundObject;
        [Range(0f, 1f)] public float teleportSfxVolume = 1f;

        [Header("Auto Pilot")]
        public bool autoPilotAfterTeleport = true;
        public float autoPilotSeconds = 3.0f;
        public bool autoPilotUseCurrentSpeed = true;
        public float autoPilotSpeed = 50f;
        public float autoPilotSpeedMultiplier = 1f;

        [Header("UI")]
        public TMPro.TMP_Text autoPilotTimerText;
        [Tooltip("Format string. {0} is time remaining.")]
        public string timerFormat = "Auto Pilot ending in ({0:0.0}) seconds";

        [Header("Dependencies (Auto-filled)")]
        public CheckpointNetwork network;

        [Header("Debug")]
        public bool debugLogs = true;

        private void Start()
        {
            if (debugLogs) Debug.Log($"[TeleportPowerUp] Started on {gameObject.name}. Manual: {manualTriggerOnly}");
            if (autoPilotTimerText) autoPilotTimerText.gameObject.SetActive(false);
            
            if (!network)
            {
                network = CheckpointNetwork.Instance;
                if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (manualTriggerOnly) return;
            
            // Check for player
            Rigidbody rb = other.attachedRigidbody;
            if (!rb) return;
            if (!rb.CompareTag("Player") && !rb.transform.root.CompareTag("Player")) return;

            if (debugLogs) Debug.Log($"[TeleportPowerUp] Triggered by {rb.name}");
            Apply(rb.gameObject);
        }

        public void Apply(GameObject target)
        {
            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (!rb) rb = target.GetComponentInParent<Rigidbody>();
            if (!rb) return;

            // Register Collection
            if (PowerUpManager.Instance != null)
            {
                PowerUpManager.Instance.RegisterCollection(powerUpType);
            }

            // Perform Teleport
            Teleport(rb);
        }

        private void Teleport(Rigidbody rb)
        {
            if (!network)
            {
                network = CheckpointNetwork.Instance;
                if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
            }

            if (!network || network.Count == 0)
            {
                if (network) network.Build();
                if (!network || network.Count == 0)
                {
                    Debug.LogError("[TeleportPowerUp] Cannot teleport: No CheckpointNetwork found or empty.");
                    return;
                }
            }

            // Find current position index
            int anchorIndex = network.GetNearestIndex(rb.position);
            
            // Calculate target
            int targetIndex = network.ClampOrWrapIndex(anchorIndex + checkpointsToJump);

            // Calculate position
            Vector3 tangent;
            Vector3 basePos = network.GetPositionBehindOnPath(targetIndex, behindDistanceOnPath, out tangent);

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 destPos = basePos + Vector3.up * upOffset + right * rightOffset;

            // Save velocity
            Vector3 savedVel = rb.linearVelocity;
            Vector3 savedAngVel = rb.angularVelocity;

            // Move
            rb.position = destPos;
            
            // Rotate to face track direction
            if (tangent.sqrMagnitude > 0.000001f)
                rb.rotation = Quaternion.LookRotation(tangent, Vector3.up);

            // Restore physics
            if (keepVelocity)
            {
                rb.linearVelocity = savedVel;
                rb.angularVelocity = savedAngVel;
            }
            else
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // SFX
            if (teleportSfx)
            {
                AudioSource.PlayClipAtPoint(teleportSfx, destPos, teleportSfxVolume);
            }
            if (teleportSoundObject != null)
            {
                Instantiate(teleportSoundObject, destPos, Quaternion.identity);
            }

            if (debugLogs) Debug.Log($"[TeleportPowerUp] Teleported {rb.name} from index {anchorIndex} to {targetIndex} (+{checkpointsToJump})");

            // Auto Pilot
            if (autoPilotAfterTeleport)
            {
                var ap = rb.GetComponent<PathAutoPilot>();
                if (!ap) ap = rb.gameObject.AddComponent<PathAutoPilot>();
                
                // Initialize array if null to prevent NullReference
                if (ap.disableWhileActive == null) ap.disableWhileActive = new MonoBehaviour[0];

                // FORCE overrides for PowerUp behavior:
                // 1. Do not let player cancel it manually (it's a forced jump)
                ap.cancelOnAnyInput = false;
                
                // 2. Clear any disabling as we now rely on physics override
                ap.disableWhileActive = new MonoBehaviour[0];

                // 3. UI
                ap.statusText = autoPilotTimerText;
                if (!string.IsNullOrEmpty(timerFormat)) ap.uiFormat = timerFormat;

                float speedToUse = autoPilotSpeed;
                if (autoPilotUseCurrentSpeed)
                {
                    float v = savedVel.magnitude;
                    speedToUse = Mathf.Max(5f, v * autoPilotSpeedMultiplier);
                }

                int nextIndex = network.GetNextIndex(targetIndex);
                ap.Begin(nextIndex, autoPilotSeconds, speedToUse);
            }
        }
    }
}
