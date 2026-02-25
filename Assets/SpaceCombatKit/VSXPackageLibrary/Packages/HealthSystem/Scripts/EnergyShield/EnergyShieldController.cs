using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace VSX.Health
{
    /// <summary>
    /// This class manages the visual effects for an energy shield.
    /// </summary>
    public class EnergyShieldController : EnergyShieldControllerBase
    {

        [Header("Health Settings")]


        [Tooltip("The Damageable that is used to drive the shield effects.")]
        [SerializeField]
        protected Damageable damageable;


        [Tooltip("Whether to ignore internally generated effects (effects with a world position the same as the damageable's position).")]
        [SerializeField]
        protected bool ignoreInternalEffects = true;

        [Tooltip ("The health based gradient colors for the shield. Left is zero health and right is full health.")]
        [SerializeField]
        [GradientUsageAttribute(true)] protected Gradient healthBasedColor = new Gradient();


        [Header("Health Based Rim Color")]


        [Tooltip("Whether to use the health based color gradient to drive the rim glow color.")]
        [SerializeField]
        protected bool healthBasedRimColor = true;


        [Header("Visibility Settings")]

        [Tooltip("Whether the shield is always visible.")]
        [SerializeField]
        protected bool alwaysVisible = false;

        [Tooltip("The minimum visibility (rim opacity) of the shield when always visible.")]
        [SerializeField]
        [Range(0, 1)]
        protected float minVisibility = 0.5f;

        [Tooltip("Whether the shield should pulse.")]
        [SerializeField]
        protected bool pulseVisibility = false;

        [Tooltip("The speed of the pulse.")]
        [SerializeField]
        protected float pulseSpeed = 1f;

        [Tooltip("The minimum opacity during pulse.")]
        [SerializeField]
        [Range(0, 1)]
        protected float pulseMin = 0f;

        [Tooltip("The maximum opacity during pulse.")]
        [SerializeField]
        [Range(0, 1)]
        protected float pulseMax = 1f;

        [Tooltip("Whether to disable the shield object on start (e.g., waiting for powerup).")]
        [SerializeField]
        protected bool startDisabled = true;


        protected float remainingDuration = 0f;

        protected TMP_Text timerText;
        protected string timerFormat = "{0:0.0}";

        public virtual void SetUI(TMP_Text timerText, string timerFormat)
        {
            this.timerText = timerText;
            this.timerFormat = timerFormat;
            if (this.timerText != null) this.timerText.gameObject.SetActive(true);
        }


        [Header("Damage Effects")]


        [Tooltip("Whether to modify the strength of the hit effect based on the damage value.")]
        [SerializeField]
        protected bool damageBasedEffectStrength = true;

        [Tooltip("The value that is multiplied by the damage value to get the effect strength.")]
        [SerializeField]
        protected float damageToEffectStrength = 0.1f;

        [Tooltip("Whether to override the color of the shield with a specific color for damage.")]
        [SerializeField]
        protected bool overrideDamageEffectColor = false;

        [Tooltip("The unique color for damage hit effects.")]
        [SerializeField]
        [ColorUsageAttribute(true, true)] protected Color damageEffectColorOverride = new Color(0.075f, 0.5f, 1f);


        [Header("Heal Effects")]


        [Tooltip("Whether to modify the strength of the hit effect based on the heal value.")]
        [SerializeField]
        protected bool healBasedEffectStrength = true;

        [Tooltip("The value that is multiplied by the healing value to get the effect strength.")]
        [SerializeField]
        protected float healToEffectStrength = 0.1f;

        [Tooltip("Whether to override the color of the shield with a specific color for healing.")]
        [SerializeField]
        protected bool overrideHealEffectColor = false;

        [Tooltip("The unique color for heal hit effects.")]
        [SerializeField]
        [ColorUsageAttribute(true, true)] protected Color healEffectColorOverride = new Color(1f, 0f, 0.5f);
      


        // Called when this component is first added to a gameobject or reset in inspector
        protected override void Reset()
        {
            base.Reset();

            // Disable the independent collision detection by default, since the Damageable will handle collision damage.
            detectCollisions = false;

            // Find a Damageable component
            damageable = GetComponent<Damageable>();
            if (damageable == null)
            {
                damageable = transform.root.GetComponentInChildren<Damageable>();
            }

            // Initialize the zero health color to an orange-red
            GradientColorKey zeroHealthColor = new GradientColorKey(new Color(1, 0.2f, 0) * 5, 0);
            GradientAlphaKey zeroHealthAlpha = new GradientAlphaKey(1, 0);

            // Initialize the zero health color to a sci-fi blue
            GradientColorKey fullHealthColor = new GradientColorKey(new Color(0, 0.5f, 1) * 5, 1);
            GradientAlphaKey fullHealthAlpha = new GradientAlphaKey(1, 1);

            // Initialize the color gradients
            healthBasedColor.SetKeys(new GradientColorKey[] { zeroHealthColor, fullHealthColor }, 
                                        new GradientAlphaKey[] { zeroHealthAlpha, fullHealthAlpha });
        }


        protected override void Awake()
        {
            base.Awake();

            if (energyShieldMeshRenderer == null)
            {
                energyShieldMeshRenderer = GetComponent<MeshRenderer>();
            }

            // Hook up the damage and healing events to show effects
            if (damageable != null)
            {
                damageable.onDamaged.AddListener(OnDamaged);
                damageable.onHealed.AddListener(OnHealed);
            }

            if (startDisabled)
            {
                SetShieldActive(false);
            }
        }

        
        /// <summary>
        /// Called when the linked damageable is damaged.
        /// </summary>
        /// <param name="info">The damage information.</param>
        public virtual void OnDamaged(HealthEffectInfo info)
        {
            if (ignoreInternalEffects && Mathf.Approximately(Vector3.Distance(info.worldPosition, damageable.transform.position), 0))
            {
                return;
            }

            // Calculate the color for damage
            Color c = overrideDamageEffectColor ? damageEffectColorOverride : healthBasedColor.Evaluate(damageable.CurrentHealthFraction);

            // Modify the color based on damage amount
            if (damageBasedEffectStrength) c *= info.amount * damageToEffectStrength;

            // Show the effect
            ShowEffect(info.worldPosition, c);
        }

        /// <summary>
        /// Called when the shield is healed;
        /// </summary>
        public virtual void OnHealed(HealthEffectInfo info)
        {
            if (ignoreInternalEffects && Mathf.Approximately(Vector3.Distance(info.worldPosition, damageable.transform.position), 0))
            {
                return;
            }

            // Calculate the color for healing
            Color c = overrideHealEffectColor ? healEffectColorOverride : healthBasedColor.Evaluate(damageable.CurrentHealthFraction);

            // Modify the color based on heal amount
            if (healBasedEffectStrength) c *= info.amount * healToEffectStrength;

            // Show the effect
            ShowEffect(info.worldPosition, c);
        }

        
        // Called every frame to update hit visual effects.
        protected override void UpdateEffects()
        {
            base.UpdateEffects();

            if (energyShieldMeshRenderer == null) return;

            // Adjust the rim color based on the current health
            if (healthBasedRimColor)
            {
                energyShieldMeshRenderer.material.SetColor("_RimColor", healthBasedColor.Evaluate(damageable.CurrentHealthFraction));
            }

            if (alwaysVisible)
            {
                float targetVisibility = minVisibility;

                if (pulseVisibility)
                {
                    float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
                    targetVisibility = Mathf.Lerp(pulseMin, pulseMax, t);
                }

                float currentOpacity = energyShieldMeshRenderer.material.GetFloat("_RimOpacity");
                if (currentOpacity < targetVisibility)
                {
                    energyShieldMeshRenderer.material.SetFloat("_RimOpacity", targetVisibility);
                }
            }
        }

        // Activate the shield for a specific duration
        public virtual void ActivateShield(float duration)
        {
            SetShieldActive(true);
            remainingDuration = duration;
        }

        public bool IsShieldActive
        {
            get
            {
                return energyShieldMeshRenderer != null && energyShieldMeshRenderer.enabled;
            }
        }

        /// <summary>
        /// Returns the shield's current health as a 0-1 fraction.
        /// Used by HUD components to drive shield fill bars.
        /// </summary>
        public float ShieldHealthFraction
        {
            get
            {
                if (damageable == null || damageable.HealthCapacity <= 0) return 0f;
                return Mathf.Clamp01(damageable.CurrentHealth / damageable.HealthCapacity);
            }
        }

        /// <summary>
        /// The Damageable component that represents the shield's health pool.
        /// Other systems can read this to know which Damageable is the shield.
        /// </summary>
        public Damageable ShieldDamageable { get { return damageable; } }

        /// <summary>
        /// Auto-register the shield's Damageable with every other Damageable
        /// in the vehicle hierarchy so incoming damage is intercepted by the
        /// shield automatically (no manual Inspector wiring needed).
        /// </summary>
        protected virtual void RegisterShieldWithHullDamageables()
        {
            if (damageable == null) return;

            Damageable[] allDamageables = transform.root.GetComponentsInChildren<Damageable>(true);
            foreach (Damageable d in allDamageables)
            {
                if (d == damageable) continue;

                if (d.ShieldDamageable == null)
                {
                    d.ShieldDamageable = damageable;
                }
            }
        }

        public virtual void SetShieldActive(bool active)
        {
            if (energyShieldMeshRenderer != null) energyShieldMeshRenderer.enabled = active;
            Collider c = GetComponent<Collider>();
            if (c != null) c.enabled = active;

            // Sync the shield Damageable state so HUD and NetworkedHealthSync
            // properly reflect whether the shield is active.
            // When inactive: health = 0, isDamageable = false (hidden on HUD, no damage accepted).
            // When active: restore to full health, isDamageable = true.
            if (damageable != null)
            {
                if (active)
                {
                    damageable.SetDamageable(true);
                    damageable.SetHealable(true);
                    damageable.Restore(true); // Restores to full healthCapacity

                    // Auto-wire the shield Damageable to all hull Damageables
                    // so damage is intercepted. Safe to call multiple times.
                    RegisterShieldWithHullDamageables();
                }
                else
                {
                    damageable.SetDamageable(false);
                    damageable.SetHealable(false);
                    damageable.SetHealth(0); // Zero health so HUD hides the shield bar
                }
            }

            if (!active && timerText != null)
            {
                timerText.gameObject.SetActive(false);
            }
        }

        // Called every frame
        protected override void Update()
        {
            base.Update();
            
            if (remainingDuration > 0)
            {
                remainingDuration -= Time.deltaTime;

                if (timerText != null)
                {
                    timerText.text = string.Format(timerFormat, remainingDuration);
                }

                if (remainingDuration <= 0)
                {
                    remainingDuration = 0;
                    SetShieldActive(false);
                }
            }
        }
    }
}