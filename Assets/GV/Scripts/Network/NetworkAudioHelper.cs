using UnityEngine;
using Fusion;

namespace GV.Network
{
    /// <summary>
    /// Shared utility for checking whether a collider belongs to the local player.
    /// Used by pickup/power-up scripts to prevent double audio in multiplayer.
    /// Non-networked objects (offline testing) always return true so sounds still play.
    /// </summary>
    public static class NetworkAudioHelper
    {
        /// <summary>
        /// Returns true if the collider belongs to the local player's ship,
        /// or if there's no networking (offline testing).
        /// Use this before playing any pickup/collection audio.
        /// </summary>
        public static bool IsLocalPlayer(Collider other)
        {
            // Find the NetworkObject on the ship root
            NetworkObject netObj = null;

            if (other.attachedRigidbody != null)
                netObj = other.attachedRigidbody.GetComponent<NetworkObject>();

            if (netObj == null)
                netObj = other.GetComponentInParent<NetworkObject>();

            if (netObj == null)
                netObj = other.transform.root.GetComponent<NetworkObject>();

            // No NetworkObject = offline/local testing, always play audio
            if (netObj == null) return true;

            return netObj.HasInputAuthority;
        }

        /// <summary>
        /// Same as above but takes a GameObject (for scripts that resolve the target first).
        /// </summary>
        public static bool IsLocalPlayer(GameObject target)
        {
            if (target == null) return false;

            NetworkObject netObj = target.GetComponent<NetworkObject>();
            if (netObj == null) netObj = target.GetComponentInParent<NetworkObject>();
            if (netObj == null) netObj = target.transform.root.GetComponent<NetworkObject>();

            if (netObj == null) return true;

            return netObj.HasInputAuthority;
        }
    }
}
