using UnityEngine;
using VSX.Weapons;

namespace GV.Network
{
    /// <summary>
    /// Overrides weapon aim direction on the HOST's copy of a client's ship.
    ///
    /// Problem: On the host, the client's ship has no camera/cursor, so WeaponsController
    /// (via AimController) aims all weapons straight forward. Projectiles always go forward.
    ///
    /// Solution: The client sends its aim position via RPC. This component applies it
    /// AFTER WeaponsController's LateUpdate (exec order 30) to override the straight-forward aim.
    ///
    /// Execution order 50 ensures we run after WeaponsController (30) in LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class NetworkedAimOverride : MonoBehaviour
    {
        private Vector3 _aimPosition;
        private bool _hasAimPosition = false;
        private WeaponsController _weaponsController;

        /// <summary>
        /// Called by NetworkedSpaceshipBridge when it receives RPC aim data from the client.
        /// </summary>
        public void SetAimPosition(Vector3 worldAimPos)
        {
            _aimPosition = worldAimPos;
            _hasAimPosition = true;
        }

        private void Start()
        {
            _weaponsController = GetComponentInChildren<WeaponsController>(true);
            if (_weaponsController == null)
            {
                // Search from root in case we're on a sibling branch
                _weaponsController = transform.root.GetComponentInChildren<WeaponsController>(true);
            }

            if (_weaponsController != null)
            {
                Debug.Log($"[NetworkedAimOverride] Found WeaponsController on {_weaponsController.gameObject.name} " +
                          $"(guns={_weaponsController.GunWeapons.Count}, missiles={_weaponsController.MissileWeapons.Count})");
            }
            else
            {
                Debug.LogWarning($"[NetworkedAimOverride] No WeaponsController found on {gameObject.name}!");
            }
        }

        /// <summary>
        /// Runs AFTER WeaponsController.LateUpdate (exec 30) thanks to our exec order 50.
        /// Overrides the aim that WeaponsController set (which was straight-forward due to no cursor).
        /// </summary>
        private void LateUpdate()
        {
            if (!_hasAimPosition) return;

            // Lazy-find WeaponsController if not found yet (weapons may register after Start)
            if (_weaponsController == null)
            {
                _weaponsController = GetComponentInChildren<WeaponsController>(true);
                if (_weaponsController == null)
                    _weaponsController = transform.root.GetComponentInChildren<WeaponsController>(true);
                if (_weaponsController == null) return;
            }

            // Override gun weapon aim
            foreach (var gun in _weaponsController.GunWeapons)
            {
                gun.Aim(_aimPosition);
            }

            // Override missile weapon aim
            foreach (var missile in _weaponsController.MissileWeapons)
            {
                missile.Aim(_aimPosition);
            }
        }
    }
}
