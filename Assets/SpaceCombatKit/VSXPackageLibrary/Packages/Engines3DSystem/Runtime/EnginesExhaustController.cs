using System;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.Engines3D
{
    /// <summary>
    /// Controls the visual effects for an Engines component.
    /// Added:
    /// - Separate single-color overrides for Flame (particles), Glow (renderers), Light (lights)
    /// - Optional particle-material tint override via MaterialPropertyBlock (better color match)
    /// - Global particle speed + distance controls remain level-driven
    /// </summary>
    public class EnginesExhaustController : MonoBehaviour
    {
        public enum ColorBlendMode { Replace, Multiply }

        // =====================================================================
        // Single Color Overrides (as requested)
        // =====================================================================

        [Header("Flame Color Override (Particles)")]
        [Tooltip("Overrides particle flame color with a single color (not by level).")]
        public bool overrideFlameColor = false;

        [Tooltip("Single flame color.")]
        [ColorUsage(true, true)]
        public Color flameColor = Color.white;

        [Tooltip("Replace forces exact color. Multiply multiplies existing color.")]
        public ColorBlendMode flameColorBlend = ColorBlendMode.Replace;

        [Tooltip("Apply flameColor to ParticleSystem.Main.startColor.")]
        public bool applyFlameToParticles = true;

        [Tooltip("Also apply flameColor to particle MATERIAL via MaterialPropertyBlock (recommended).")]
        public bool applyFlameToParticleMaterials = true;

        [Tooltip("Particle material color keys to set (only keys used by your particle shader matter).")]
        public List<string> flameParticleMaterialColorKeys = new List<string>
        {
            "_BaseColor", "_Color", "_TintColor", "_EmissionColor", "_EmissiveColor"
        };


        [Header("Glow Color Override (Renderers)")]
        [Tooltip("Overrides glow/emission color on animated renderers with a single color (not by level).")]
        public bool overrideGlowColor = false;

        [Tooltip("Single glow color.")]
        [ColorUsage(true, true)]
        public Color glowColor = Color.white;

        public ColorBlendMode glowColorBlend = ColorBlendMode.Replace;

        [Tooltip("Apply glowColor to animated renderers.")]
        public bool applyGlowToRenderers = true;

        [Tooltip("Renderer material color keys to set (common: _EmissionColor, _BaseColor, _Color).")]
        public List<string> glowRendererColorPropertyKeys = new List<string> { "_EmissionColor" };


        [Header("Light Color Override (Lights)")]
        [Tooltip("Overrides light color with a single color (not by level).")]
        public bool overrideLightColor = false;

        [Tooltip("Single light color.")]
        [ColorUsage(true, true)]
        public Color lightColor = Color.white;

        public ColorBlendMode lightColorBlend = ColorBlendMode.Replace;

        [Tooltip("Apply lightColor to animated lights.")]
        public bool applyColorToLights = true;


        // =====================================================================
        // (Optional) Legacy / By-level color override (kept for compatibility)
        // Turn this OFF if you only want the single-color overrides above.
        // =====================================================================

        [Header("Legacy: By-Level Exhaust Color (Optional)")]
        [Tooltip("If enabled, drives a global exhaust color from a gradient over engine level (0-1). Used only when the per-type override is OFF.")]
        public bool overrideExhaustColorByLevel = false;

        [GradientUsage(true)]
        public Gradient exhaustColorByLevel;

        [ColorUsage(true, true)]
        public Color exhaustColorTint = Color.white;

        [Tooltip("If per-type override is OFF, this blend mode is used for that type.")]
        public ColorBlendMode byLevelColorBlend = ColorBlendMode.Replace;


        // =====================================================================
        // Global Speed + Distance (unchanged concept)
        // =====================================================================

        [Header("Global Exhaust Speed (Particles)")]
        [Tooltip("Globally scales particle start speed (higher = faster exhaust).")]
        public bool overrideExhaustSpeed = false;

        [Min(0f)]
        public float exhaustSpeedMultiplier = 1f;

        [Tooltip("Multiplier over engine level (0-1). Default = 1 across the range.")]
        public AnimationCurve exhaustSpeedByLevel = AnimationCurve.Linear(0, 1, 1, 1);


        [Header("Global Exhaust Distance / Length (Particles + Optional Transforms)")]
        [Tooltip("Globally scales particle start lifetime (higher = longer exhaust / more distance).")]
        public bool overrideExhaustDistance = false;

        [Min(0f)]
        public float exhaustDistanceMultiplier = 1f;

        [Tooltip("Multiplier over engine level (0-1). Default = 1 across the range.")]
        public AnimationCurve exhaustDistanceByLevel = AnimationCurve.Linear(0, 1, 1, 1);


        // =====================================================================
        // Original Types (Extended)
        // =====================================================================

        [System.Serializable]
        public class AnimatedRenderer
        {
            [SerializeField] protected Renderer renderer;
            [SerializeField] protected int materialIndex;

            [Tooltip("If true, this renderer can receive the Glow color override.")]
            [SerializeField] protected bool applyGlowOverride = true;

            protected Material mat;

            public List<ShaderColorSetting> shaderColorSettings = new List<ShaderColorSetting>();
            public List<ShaderFloatSetting> shaderFloatSettings = new List<ShaderFloatSetting>();

            [System.Serializable]
            public class ShaderFloatSetting
            {
                public string key;
                public AnimationCurve curve;
            }

            [System.Serializable]
            public class ShaderColorSetting
            {
                public string key;
                [GradientUsage(true)]
                public Gradient color;
            }

            public virtual void Initialize()
            {
                if (renderer == null) return;
                var mats = renderer.materials;
                if (mats == null || mats.Length == 0) return;

                materialIndex = Mathf.Clamp(materialIndex, 0, mats.Length - 1);
                mat = mats[materialIndex];
            }

            public virtual void Update(
                float animationPosition,
                bool glowEnabled,
                Color glowColor,
                ColorBlendMode blendMode,
                List<string> glowKeys)
            {
                if (mat == null) return;

                // existing animations
                foreach (var fs in shaderFloatSettings)
                {
                    if (!string.IsNullOrEmpty(fs.key))
                        mat.SetFloat(fs.key, fs.curve.Evaluate(animationPosition));
                }

                foreach (var cs in shaderColorSettings)
                {
                    if (!string.IsNullOrEmpty(cs.key) && cs.color != null)
                        mat.SetColor(cs.key, cs.color.Evaluate(animationPosition));
                }

                // glow override
                if (!glowEnabled || !applyGlowOverride || glowKeys == null) return;

                for (int i = 0; i < glowKeys.Count; i++)
                {
                    string key = glowKeys[i];
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!mat.HasProperty(key)) continue;

                    if (blendMode == ColorBlendMode.Replace) mat.SetColor(key, glowColor);
                    else mat.SetColor(key, mat.GetColor(key) * glowColor);
                }
            }
        }

        [System.Serializable]
        public class AnimatedParticle
        {
            [SerializeField] protected ParticleSystem particleSystem;

            protected ParticleSystem.MainModule mainModule;
            protected ParticleSystem.EmissionModule emissionModule;

            [SerializeField] protected bool animateStartSpeed;
            [SerializeField] protected AnimationCurve startSpeedCurve;

            [SerializeField] protected bool animateStartLifetime;
            [SerializeField] protected AnimationCurve startLifetimeCurve;

            [SerializeField] protected bool animateStartColor;
            [SerializeField, GradientUsage(true)] protected Gradient startColorGradient;

            [Tooltip("If true, this particle system receives global speed scaling.")]
            [SerializeField] protected bool applyGlobalSpeedOverride = true;

            [Tooltip("If true, this particle system receives global distance scaling (lifetime scaling).")]
            [SerializeField] protected bool applyGlobalDistanceOverride = true;

            [Tooltip("If true, this particle system can receive Flame color override.")]
            [SerializeField] protected bool applyFlameOverride = true;

            [Tooltip("If true, also forces particle renderer material tint using MaterialPropertyBlock.")]
            [SerializeField] protected bool applyFlameOverrideToMaterial = true;

            // baselines
            protected ParticleSystem.MinMaxCurve baseStartSpeed;
            protected ParticleSystem.MinMaxCurve baseStartLifetime;
            protected ParticleSystem.MinMaxGradient baseStartColor;

            // MPB
            protected ParticleSystemRenderer psRenderer;
            protected MaterialPropertyBlock mpb;

            public virtual void Initialize()
            {
                if (particleSystem == null) return;

                mainModule = particleSystem.main;
                emissionModule = particleSystem.emission;

                baseStartSpeed = mainModule.startSpeed;
                baseStartLifetime = mainModule.startLifetime;
                baseStartColor = mainModule.startColor;

                psRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (psRenderer != null) mpb = new MaterialPropertyBlock();
            }

            public virtual void Update(
                float animationPosition,
                bool globalSpeedEnabled, float globalSpeedMul,
                bool globalDistanceEnabled, float globalDistanceMul,
                bool flameEnabled, Color flameColor, ColorBlendMode flameBlend,
                bool applyToStartColor,
                bool applyToMaterialTint,
                List<string> particleMatKeys)
            {
                if (particleSystem == null) return;

                emissionModule.enabled = !Mathf.Approximately(animationPosition, 0f);

                // speed
                float speed = (animateStartSpeed && startSpeedCurve != null)
                    ? startSpeedCurve.Evaluate(animationPosition)
                    : EnginesExhaustController.EvaluateMinMaxCurve(baseStartSpeed, animationPosition);

                if (globalSpeedEnabled && applyGlobalSpeedOverride)
                    speed *= globalSpeedMul;

                mainModule.startSpeed = speed;

                // distance via lifetime
                float lifetime = (animateStartLifetime && startLifetimeCurve != null)
                    ? startLifetimeCurve.Evaluate(animationPosition)
                    : EnginesExhaustController.EvaluateMinMaxCurve(baseStartLifetime, animationPosition);

                if (globalDistanceEnabled && applyGlobalDistanceOverride)
                    lifetime *= globalDistanceMul;

                mainModule.startLifetime = lifetime;

                // start color (authoring)
                Color col = (animateStartColor && startColorGradient != null)
                    ? startColorGradient.Evaluate(animationPosition)
                    : EnginesExhaustController.EvaluateMinMaxGradient(baseStartColor, animationPosition);

                // flame override
                if (flameEnabled && applyFlameOverride)
                {
                    if (flameBlend == ColorBlendMode.Replace) col = flameColor;
                    else col *= flameColor;
                }

                if (applyToStartColor)
                    mainModule.startColor = col;

                // optional: force particle MATERIAL tint (recommended)
                bool doMatTint =
                    flameEnabled &&
                    applyFlameOverride &&
                    applyToMaterialTint &&
                    applyFlameOverrideToMaterial &&
                    psRenderer != null &&
                    mpb != null &&
                    particleMatKeys != null &&
                    particleMatKeys.Count > 0;

                if (doMatTint)
                {
                    psRenderer.GetPropertyBlock(mpb);

                    for (int i = 0; i < particleMatKeys.Count; i++)
                    {
                        string key = particleMatKeys[i];
                        if (string.IsNullOrEmpty(key)) continue;

                        if (flameBlend == ColorBlendMode.Replace)
                            mpb.SetColor(key, flameColor);
                        else
                            mpb.SetColor(key, Color.white * flameColor);
                    }

                    psRenderer.SetPropertyBlock(mpb);
                }
            }
        }

        [System.Serializable]
        public class AnimatedTransform
        {
            [SerializeField] protected Transform m_Transform;
            [SerializeField] protected Vector3 startScale = Vector3.one;
            [SerializeField] protected Vector3 endScale = Vector3.one;
            [SerializeField] protected AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

            [Tooltip("If true, multiply this transform's scale by the controller's global exhaust distance multiplier.")]
            [SerializeField] protected bool applyGlobalDistanceOverride = false;

            [Tooltip("Axis mask for distance scaling (typical: (0,0,1) to stretch in local Z only).")]
            [SerializeField] protected Vector3 distanceScaleMask = new Vector3(0, 0, 1);

            public virtual void Update(float animationPosition, bool globalDistanceEnabled, float globalDistanceMul)
            {
                if (m_Transform == null) return;

                float val = (curve != null) ? curve.Evaluate(animationPosition) : animationPosition;
                Vector3 s = val * endScale + (1f - val) * startScale;

                if (globalDistanceEnabled && applyGlobalDistanceOverride)
                {
                    float d = Mathf.Max(0f, globalDistanceMul);
                    Vector3 mul = Vector3.one + Vector3.Scale(distanceScaleMask, new Vector3(d - 1f, d - 1f, d - 1f));
                    s = Vector3.Scale(s, mul);
                }

                m_Transform.localScale = s;
            }
        }

        [System.Serializable]
        public class AnimatedLight
        {
            public Light m_Light;

            [GradientUsage(true)]
            public Gradient lightColor;

            public AnimationCurve lightIntensity = AnimationCurve.Linear(0, 0, 1, 1);

            [Tooltip("If true, this light can receive the Light color override.")]
            public bool applyLightOverride = true;

            public virtual void Update(
                float level,
                bool lightEnabled,
                Color overrideColor,
                ColorBlendMode blendMode)
            {
                if (m_Light == null) return;

                Color baseCol = (lightColor != null) ? lightColor.Evaluate(level) : m_Light.color;

                if (lightEnabled && applyLightOverride)
                {
                    m_Light.color = (blendMode == ColorBlendMode.Replace) ? overrideColor : (baseCol * overrideColor);
                }
                else
                {
                    m_Light.color = baseCol;
                }

                m_Light.intensity = (lightIntensity != null) ? lightIntensity.Evaluate(level) : m_Light.intensity;
            }
        }

        [Tooltip("The Engines component to show visual effects for.")]
        [SerializeField] protected Engines engines;

        [Tooltip("The transform representing the center of mass of the vehicle.")]
        [SerializeField] protected Transform centerOfMass;

        public enum MovementAxis { Procedural, None, Horizontal, Vertical, Forward }
        public enum SteeringAxis { Procedural, None, Pitch, Yaw, Roll }

        [SerializeField] protected SteeringAxis steeringAxis;
        [SerializeField] protected MovementAxis movementAxis;

        public enum EngineMode { All, Cruising, Boost }

        [System.Serializable]
        public class EngineModeAnimationSettings
        {
            public EngineMode mode;

            public List<AnimatedParticle> animatedParticles = new List<AnimatedParticle>();
            public List<AnimatedRenderer> animatedRenderers = new List<AnimatedRenderer>();
            public List<AnimatedTransform> animatedTransforms = new List<AnimatedTransform>();
            public List<AnimatedLight> animatedLights = new List<AnimatedLight>();

            public virtual void Initialize()
            {
                foreach (var p in animatedParticles) p.Initialize();
                foreach (var r in animatedRenderers) r.Initialize();
            }

            public virtual void Update(
                float level,
                // global speed/dist
                bool globalSpeedEnabled, float globalSpeedMul,
                bool globalDistanceEnabled, float globalDistanceMul,
                // flame
                bool flameEnabled, Color flameColor, ColorBlendMode flameBlend,
                bool applyFlameToParticles,
                bool applyFlameToParticleMaterials,
                List<string> flameParticleMatKeys,
                // glow
                bool glowEnabled, Color glowColor, ColorBlendMode glowBlend,
                List<string> glowRendererKeys,
                // light
                bool lightEnabled, Color lightColor, ColorBlendMode lightBlend)
            {
                foreach (var p in animatedParticles)
                {
                    p.Update(
                        level,
                        globalSpeedEnabled, globalSpeedMul,
                        globalDistanceEnabled, globalDistanceMul,
                        flameEnabled, flameColor, flameBlend,
                        applyFlameToParticles,
                        applyFlameToParticleMaterials,
                        flameParticleMatKeys);
                }

                foreach (var r in animatedRenderers)
                {
                    r.Update(level, glowEnabled, glowColor, glowBlend, glowRendererKeys);
                }

                foreach (var t in animatedTransforms)
                {
                    t.Update(level, globalDistanceEnabled, globalDistanceMul);
                }

                foreach (var l in animatedLights)
                {
                    l.Update(level, lightEnabled, lightColor, lightBlend);
                }
            }
        }

        public List<EngineModeAnimationSettings> settings = new List<EngineModeAnimationSettings>();

        protected virtual void Reset()
        {
            engines = GetComponent<Engines>();
        }

        protected virtual void Awake()
        {
            foreach (var s in settings) s.Initialize();
        }

        protected virtual void LateUpdate()
        {
            if (engines == null) return;

            Transform com = (centerOfMass != null) ? centerOfMass : engines.transform;

            Vector3 thrusterLocalPos = com.InverseTransformPoint(transform.position);
            Vector3 thrusterLocalDirection = com.InverseTransformDirection(transform.forward);

            // movement axis
            Vector3 translationAxis;
            switch (movementAxis)
            {
                case MovementAxis.Procedural: translationAxis = engines.ModulatedMovementInputs; break;
                case MovementAxis.Horizontal: translationAxis = new Vector3(engines.ModulatedMovementInputs.x, 0, 0); break;
                case MovementAxis.Vertical: translationAxis = new Vector3(0, engines.ModulatedMovementInputs.y, 0); break;
                case MovementAxis.Forward: translationAxis = new Vector3(0, 0, engines.ModulatedMovementInputs.z); break;
                default: translationAxis = Vector3.zero; break;
            }
            float movementAmount = Mathf.Clamp(-Vector3.Dot(thrusterLocalDirection, translationAxis), 0, 1);

            // steering axis
            Vector3 rotationAxis;
            switch (steeringAxis)
            {
                case SteeringAxis.Procedural: rotationAxis = engines.ModulatedSteeringInputs; break;
                case SteeringAxis.Pitch: rotationAxis = new Vector3(engines.ModulatedSteeringInputs.x, 0, 0); break;
                case SteeringAxis.Yaw: rotationAxis = new Vector3(0, engines.ModulatedSteeringInputs.y, 0); break;
                case SteeringAxis.Roll: rotationAxis = new Vector3(0, 0, engines.ModulatedSteeringInputs.z); break;
                default: rotationAxis = Vector3.zero; break;
            }

            Vector3 tmp = Vector3.ProjectOnPlane(thrusterLocalPos, thrusterLocalDirection).normalized;
            if (Mathf.Abs(tmp.x) > 0.01f) tmp.x = Mathf.Sign(tmp.x);
            if (Mathf.Abs(tmp.y) > 0.01f) tmp.y = Mathf.Sign(tmp.y);
            if (Mathf.Abs(tmp.z) > 0.01f) tmp.z = Mathf.Sign(tmp.z);

            float steeringAmount = Mathf.Clamp(
                -Vector3.Dot(Vector3.Cross(rotationAxis, tmp), thrusterLocalDirection.normalized),
                0, 1);

            float level = Mathf.Min(movementAmount + steeringAmount, 1f);

            // global speed/dist
            bool speedEnabled = overrideExhaustSpeed;
            float globalSpeedMul = speedEnabled
                ? exhaustSpeedMultiplier * (exhaustSpeedByLevel != null ? exhaustSpeedByLevel.Evaluate(level) : 1f)
                : 1f;

            bool distEnabled = overrideExhaustDistance;
            float globalDistMul = distEnabled
                ? exhaustDistanceMultiplier * (exhaustDistanceByLevel != null ? exhaustDistanceByLevel.Evaluate(level) : 1f)
                : 1f;

            // legacy by-level fallback color (only if per-type override OFF)
            bool byLevelEnabled = overrideExhaustColorByLevel && exhaustColorByLevel != null;
            Color byLevelCol = byLevelEnabled ? (exhaustColorByLevel.Evaluate(level) * exhaustColorTint) : Color.white;

            // Flame color selection
            bool flameEnabled = (overrideFlameColor || byLevelEnabled);
            Color chosenFlameColor = overrideFlameColor ? flameColor : byLevelCol;
            ColorBlendMode chosenFlameBlend = overrideFlameColor ? flameColorBlend : byLevelColorBlend;

            // Glow color selection
            bool glowEnabled = applyGlowToRenderers && (overrideGlowColor || byLevelEnabled);
            Color chosenGlowColor = overrideGlowColor ? glowColor : byLevelCol;
            ColorBlendMode chosenGlowBlend = overrideGlowColor ? glowColorBlend : byLevelColorBlend;

            // Light color selection
            bool lightEnabled = applyColorToLights && (overrideLightColor || byLevelEnabled);
            Color chosenLightColor = overrideLightColor ? lightColor : byLevelCol;
            ColorBlendMode chosenLightBlend = overrideLightColor ? lightColorBlend : byLevelColorBlend;

            for (int i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];

                switch (setting.mode)
                {
                    case EngineMode.All:
                        setting.Update(
                            level,
                            speedEnabled, globalSpeedMul,
                            distEnabled, globalDistMul,
                            flameEnabled, chosenFlameColor, chosenFlameBlend,
                            applyFlameToParticles,
                            applyFlameToParticleMaterials,
                            flameParticleMaterialColorKeys,
                            glowEnabled, chosenGlowColor, chosenGlowBlend,
                            glowRendererColorPropertyKeys,
                            lightEnabled, chosenLightColor, chosenLightBlend
                        );
                        break;

                    case EngineMode.Cruising:
                        if (engines.ModulatedBoostInputs.magnitude < 0.5f)
                        {
                            setting.Update(
                                level,
                                speedEnabled, globalSpeedMul,
                                distEnabled, globalDistMul,
                                flameEnabled, chosenFlameColor, chosenFlameBlend,
                                applyFlameToParticles,
                                applyFlameToParticleMaterials,
                                flameParticleMaterialColorKeys,
                                glowEnabled, chosenGlowColor, chosenGlowBlend,
                                glowRendererColorPropertyKeys,
                                lightEnabled, chosenLightColor, chosenLightBlend
                            );
                        }
                        break;

                    case EngineMode.Boost:
                        if (engines.ModulatedBoostInputs.magnitude >= 0.5f)
                        {
                            // Keep original behavior: boost runs at full level for speed/dist curves.
                            float boostLevel = 1f;

                            float boostSpeedMul = speedEnabled
                                ? exhaustSpeedMultiplier * (exhaustSpeedByLevel != null ? exhaustSpeedByLevel.Evaluate(boostLevel) : 1f)
                                : 1f;

                            float boostDistMul = distEnabled
                                ? exhaustDistanceMultiplier * (exhaustDistanceByLevel != null ? exhaustDistanceByLevel.Evaluate(boostLevel) : 1f)
                                : 1f;

                            setting.Update(
                                boostLevel,
                                speedEnabled, boostSpeedMul,
                                distEnabled, boostDistMul,
                                flameEnabled, chosenFlameColor, chosenFlameBlend,
                                applyFlameToParticles,
                                applyFlameToParticleMaterials,
                                flameParticleMaterialColorKeys,
                                glowEnabled, chosenGlowColor, chosenGlowBlend,
                                glowRendererColorPropertyKeys,
                                lightEnabled, chosenLightColor, chosenLightBlend
                            );
                        }
                        break;
                }
            }
        }

        // =====================================================================
        // Helpers for Particle MinMax types
        // =====================================================================

        internal static float EvaluateMinMaxCurve(ParticleSystem.MinMaxCurve c, float t)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return c.constant;

                case ParticleSystemCurveMode.Curve:
                    return (c.curve != null ? c.curve.Evaluate(t) : 0f) * c.curveMultiplier;

                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Lerp(c.constantMin, c.constantMax, 0.5f);

                case ParticleSystemCurveMode.TwoCurves:
                    float a = (c.curveMin != null ? c.curveMin.Evaluate(t) : 0f);
                    float b = (c.curveMax != null ? c.curveMax.Evaluate(t) : 0f);
                    return Mathf.Lerp(a, b, 0.5f) * c.curveMultiplier;

                default:
                    return 0f;
            }
        }

        internal static Color EvaluateMinMaxGradient(ParticleSystem.MinMaxGradient g, float t)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return g.color;

                case ParticleSystemGradientMode.Gradient:
                    return g.gradient != null ? g.gradient.Evaluate(t) : Color.white;

                case ParticleSystemGradientMode.TwoColors:
                    return Color.Lerp(g.colorMin, g.colorMax, 0.5f);

                case ParticleSystemGradientMode.TwoGradients:
                    Color a = g.gradientMin != null ? g.gradientMin.Evaluate(t) : Color.white;
                    Color b = g.gradientMax != null ? g.gradientMax.Evaluate(t) : Color.white;
                    return Color.Lerp(a, b, 0.5f);

                default:
                    return Color.white;
            }
        }
    }
}
