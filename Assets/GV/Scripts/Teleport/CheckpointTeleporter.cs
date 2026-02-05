using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GV;

namespace GV
{
    [RequireComponent(typeof(Collider))]
    public class CheckpointTeleporter : MonoBehaviour
    {
        [Header("Teleport Settings")]
        [Tooltip("The checkpoint number to teleport to (e.g. 1 for Checkpoint 1).")]
        public int targetCheckpointNumber = 1;

        public bool manualTriggerOnly = false;

        [Header("Player Check")]
        public bool requirePlayerTag = true;
        public string playerTag = "Player";

        [Header("Positioning")]
        [Tooltip("Reappear this many meters BEFORE the target checkpoint along the checkpoint path.")]
        public float behindDistanceOnPath = 4.0f;
        public float upOffset = 0.0f;
        public float rightOffset = 0.0f;
        public bool keepVelocity = true;

        [Header("Teleport SFX")]
        public bool playTeleportSfx = true;
        public AudioClip teleportSfx;
        [Tooltip("Optional. If empty, tries Player Rigidbody's AudioSource, then this object's AudioSource, else falls back to PlayClipAtPoint.")]
        public AudioSource teleportSfxSource;
        [Range(0f, 1f)] public float teleportSfxVolume = 1f;
        public float teleportSfxDelay = 0f;

        [Header("Network (Auto-found if empty)")]
        public CheckpointNetwork network;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        void Awake()
        {
            if (!network)
            {
                network = CheckpointNetwork.Instance;
                if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (manualTriggerOnly) return;

            Rigidbody rb = other.attachedRigidbody;
            if (!IsPlayer(other, rb)) return;
            if (!rb) return;

            Teleport(rb.gameObject);
        }

        bool IsPlayer(Collider other, Rigidbody rb)
        {
            if (!requirePlayerTag) return true;
            if (rb && rb.CompareTag(playerTag)) return true;
            Transform root = other.transform.root;
            return root && root.CompareTag(playerTag);
        }

        public void Teleport(GameObject target)
        {
            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (!rb) rb = target.GetComponentInParent<Rigidbody>();
            if (!rb) return;

            if (!network)
            {
                network = CheckpointNetwork.Instance;
                if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
            }

            if (!network || network.Count == 0)
            {
                Debug.LogWarning("[CheckpointTeleporter] No CheckpointNetwork found or empty.");
                return;
            }

            // Calculate position
            Vector3 tangent;
            // GetPositionBehindOnPath handles index wrapping/clamping internally via CheckpointNetwork
            Vector3 basePos = network.GetPositionBehindOnPath(targetCheckpointNumber, behindDistanceOnPath, out tangent);

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 destPos = basePos + Vector3.up * upOffset + right * rightOffset;

            // Apply Position
            rb.position = destPos;
            if (tangent.sqrMagnitude > 0.000001f)
                rb.rotation = Quaternion.LookRotation(tangent, Vector3.up);

            // Handle Velocity
            if (!keepVelocity)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // SFX
            TryPlayTeleportSfx(rb, destPos);
        }

        void TryPlayTeleportSfx(Rigidbody playerRb, Vector3 atPos)
        {
            if (!playTeleportSfx || !teleportSfx) return;

            if (teleportSfxDelay > 0f)
                StartCoroutine(PlayTeleportSfxDelayed(playerRb, atPos));
            else
                PlayTeleportSfxNow(playerRb, atPos);
        }

        IEnumerator PlayTeleportSfxDelayed(Rigidbody playerRb, Vector3 atPos)
        {
            yield return new WaitForSeconds(teleportSfxDelay);
            PlayTeleportSfxNow(playerRb, atPos);
        }

        void PlayTeleportSfxNow(Rigidbody playerRb, Vector3 atPos)
        {
            AudioSource src = teleportSfxSource;
            if (!src && playerRb) src = playerRb.GetComponent<AudioSource>();
            if (!src) src = GetComponent<AudioSource>();

            if (src)
                src.PlayOneShot(teleportSfx, teleportSfxVolume);
            else
                AudioSource.PlayClipAtPoint(teleportSfx, atPos, teleportSfxVolume);
        }
    }
}
