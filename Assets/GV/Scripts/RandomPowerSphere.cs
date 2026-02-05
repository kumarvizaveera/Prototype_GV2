using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GV;
using GV.PowerUps;
using VSX.Engines3D;

namespace GV
{
    [RequireComponent(typeof(Collider))]
    public class RandomPowerSphere : MonoBehaviour
    {
        [Header("Power Up Components")]
        [Tooltip("Reference to the Teleport component on this object.")]
        public TeleportPowerUp teleport;
        [Tooltip("Reference to the Invisibility component on this object.")]
        public InvisibilityPowerUp invisibility;
        [Tooltip("Reference to the Shield component on this object.")]
        public ShieldPowerUp shield;
        [Tooltip("Reference to the Super Boost component on this object.")]
        public SuperBoostOrb superBoost;
        [Tooltip("Reference to the Super Weapon component on this object.")]
        public SuperWeaponOrb superWeapon;

        [Header("Settings")]
        [Tooltip("If true, the sphere disappears after use.")]
        public bool consumeOnPickup = true;
        [Tooltip("If > 0, the sphere reappears after this many seconds.")]
        public float respawnTime = -1f;

        [Header("Feedback")]
        public AudioClip mysteryPickupSound;
        public GameObject mysteryPickupEffect;

        private Collider m_Collider;
        private Renderer[] m_Renderers;

        private void Awake()
        {
            m_Collider = GetComponent<Collider>();
            m_Renderers = GetComponentsInChildren<Renderer>();
            
            // Validate components
            if (!teleport && !invisibility && !shield && !superBoost && !superWeapon)
            {
                Debug.LogWarning("[RandomPowerSphere] No power-up components assigned! Trying to find them on this object.");
                teleport = GetComponent<TeleportPowerUp>();
                invisibility = GetComponent<InvisibilityPowerUp>();
                shield = GetComponent<ShieldPowerUp>();
                superBoost = GetComponent<SuperBoostOrb>();
                superWeapon = GetComponent<SuperWeaponOrb>();
            }

            // Ensure they are all set to manual trigger only to avoid double activation
            if (teleport) teleport.manualTriggerOnly = true;
            if (invisibility) invisibility.manualTriggerOnly = true;
            if (shield) shield.manualTriggerOnly = true;
            if (superBoost) superBoost.manualTriggerOnly = true;
            if (superWeapon) superWeapon.manualTriggerOnly = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Find the vehicle root
            Rigidbody rb = other.attachedRigidbody;
            if (!rb) return;

            GameObject target = rb.gameObject;

            // Check if player (optional, but usually good practice to avoid enemies triggering it)
            if (!target.CompareTag("Player") && !target.transform.root.CompareTag("Player"))
            {
                 // If you want enemies to pick it up, remove this check.
                 // For now, let's assume only player picks up mystery spheres.
                 return;
            }

            if (PowerUpManager.Instance == null)
            {
                Debug.LogWarning("[RandomPowerSphere] PowerUpManager instance not found! Creating one automatically.");
                GameObject mgr = new GameObject("PowerUpManager");
                mgr.AddComponent<PowerUpManager>();
            }

            if (PowerUpManager.Instance == null)
            {
                Debug.LogError("[RandomPowerSphere] Failed to create PowerUpManager!");
                return;
            }

            // Determine candidates
            List<PowerUpType> candidates = new List<PowerUpType>();
            if (teleport) candidates.Add(PowerUpType.Teleport);
            if (invisibility) candidates.Add(PowerUpType.Invisibility);
            if (shield) candidates.Add(PowerUpType.Shield);
            if (superBoost) candidates.Add(PowerUpType.SuperBoost);
            if (superWeapon) candidates.Add(PowerUpType.SuperWeapon);

            PowerUpType selected = PowerUpManager.Instance.GetRandomUncollectedPower(candidates);

            if (selected != PowerUpType.None)
            {
                Debug.Log($"[RandomPowerSphere] Mystery Sphere granted: {selected}");
                ApplyPower(selected, target);

                // Feedback
                if (mysteryPickupSound) AudioSource.PlayClipAtPoint(mysteryPickupSound, transform.position);
                if (mysteryPickupEffect) Instantiate(mysteryPickupEffect, transform.position, Quaternion.identity);

                // Consume
                if (consumeOnPickup)
                {
                    if (respawnTime > 0)
                    {
                        StartCoroutine(RespawnRoutine());
                    }
                    else
                    {
                        gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                Debug.Log("[RandomPowerSphere] All power-ups collected! Nothing granted.");
                // Optional: Play a "denied" sound or give a fallback bonus (like points)
            }
        }

        private void ApplyPower(PowerUpType type, GameObject target)
        {
            switch (type)
            {
                case PowerUpType.Teleport:
                    if (teleport) teleport.Apply(target);
                    break;
                case PowerUpType.Invisibility:
                    if (invisibility) invisibility.Apply(target);
                    break;
                case PowerUpType.Shield:
                    if (shield) shield.Apply(target);
                    break;
                case PowerUpType.SuperBoost:
                    if (superBoost) superBoost.Apply(target);
                    break;
                case PowerUpType.SuperWeapon:
                    if (superWeapon) superWeapon.Apply(target);
                    break;
            }
        }

        private IEnumerator RespawnRoutine()
        {
            // Hide visuals and disable collider
            SetVisuals(false);

            yield return new WaitForSeconds(respawnTime);

            // Show visuals and enable collider
            SetVisuals(true);
        }

        private void SetVisuals(bool active)
        {
            if (m_Collider) m_Collider.enabled = active;
            foreach (var r in m_Renderers) r.enabled = active;
        }
    }
}
