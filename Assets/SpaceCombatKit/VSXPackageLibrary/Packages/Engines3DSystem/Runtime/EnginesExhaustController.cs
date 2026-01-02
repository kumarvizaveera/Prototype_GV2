using System;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.Engines3D
{
    /// <summary>
    /// Controls the visual effects for an Engines component.
    /// Added: global controls for exhaust Color, Distance (length), and Speed.
    /// Added: optional particle-material tint override via MaterialPropertyBlock to better match the set color.
    /// </summary>
    public class EnginesExhaustController : MonoBehaviour
    {
        // =========================
        // Global Exhaust Controls
        // =========================

        public enum ColorBlendMode
        {
            Replace,
            Multiply
        }

        [Header("Global Exhaust Color (Optional Override)")]
        [Tooltip("If enabled, drives a global exhaust color from a gradient over engine level (0-1).")]
        public bool overrideExhaustColor = false;

        [Tooltip("Exhaust color over engine level (0-1).")]
        [GradientUsage(true)]
        public Gradient exhaustColorByLevel;

        [Tooltip("Additional tint multiplied with the evaluated gradient color.")]
        public Color exhaustColorTint = Color.white;

        [Tooltip("Replace forces the exact color value. Multiply multiplies existing colors.")]
        public ColorBlendMode exhaustColorBlend = ColorBlendMode.Replace;

        [Tooltip("Apply global color override to ParticleSystem.Main.startColor.")]
        public bool applyColorToParticles = true;

        [Tooltip("Also apply global color to particle MATERIAL properties via MaterialPropertyBlock (recommended for exact match).")]
        public bool applyColorToParticleMaterials = true;

        [Tooltip("Material color property keys to set on PARTICLE materials (only the ones used by your shader matter).")]
        public List<string> particleMaterialColorPropertyKeys = new List<string>()
        {
            "_BaseColor", "_Color", "_TintColor", "_EmissionColor", "_EmissiveColor"
        };

        [Tooltip("Apply global color override to animated renderers (material color keys below).")]
        public bool applyColorToRenderers = true;

        [Tooltip("Material color property keys to set when applying global color to renderers (common: _EmissionColor, _BaseColor, _Color).")]
        public List<string> rendererColorPropertyKeys = new List<string>() { "_EmissionColor" };

        [Tooltip("Apply global color override to lights.")]
        public bool applyColorToLights = true;


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


        // =========================
        // Original Types (Extended)
        // =========================

        /// <summary>
        /// A renderer that is animated by the engines input.
        /// </summary>
        [System.Serializable]
        public class AnimatedRenderer
        {
            [Tooltip("The renderer being animated.")]
            [SerializeField] protected Renderer renderer;

            [Tooltip("The material index of the material to animate properties of.")]
            [SerializeField] protected int materialIndex;

            [Tooltip("If true, this renderer receives the controller's global exhaust color override.")]
            [SerializeField] protected bool applyGlobalColorOverride = true;

            protected Material mat;

            [Tooltip("The list of shader color animations.")]
            public List<ShaderColorSetting> shaderColorSettings = new List<ShaderColorSetting>();

            [System.Serializable]
            public class ShaderFloatSetting
            {
                [Tooltip("The shader float key.")]
                public string key;

                [Tooltip("The float value over the course of the animation (0-1).")]
                public AnimationCurve curve;
            }

            [System.Serializable]
            public class ShaderColorSetting
            {
                [Tooltip("The shader color key.")]
                public string key;

                [Tooltip("The color of the shader property over the course of the animation.")]
                [GradientUsage(true)]
                public Gradient color;
            }

            [Tooltip("The list of shader float animations.")]
            public List<ShaderFloatSetting> shaderFloatSettings = new List<ShaderFloatSetting>();

            public virtual void Initialize()
            {
                if (renderer == null) return;

                Material[] mats = renderer.materials;
                if (mats == null || mats.Length == 0) return;

                materialIndex = Mathf.Clamp(materialIndex, 0, mats.Length - 1);
                mat = mats[materialIndex];
            }

            public virtual void Update(
                float animationPosition,
                bool globalColorEnabled,
                Color globalColor,
                ColorBlendMode blendMode,
                List<string> globalColorKeys)
            {
                if (mat == null) return;

                foreach (ShaderFloatSetting floatSetting in shaderFloatSettings)
                {
                    if (!string.IsNullOrEmpty(floatSetting.key))
                        mat.SetFloat(floatSetting.key, floatSetting.curve.Evaluate(animationPosition));
                }

                foreach (ShaderColorSetting colorSetting in shaderColorSettings)
                {
                    if (!string.IsNullOrEmpty(colorSetting.key) && colorSetting.color != null)
                        mat.SetColor(colorSetting.key, colorSetting.color.Evaluate(animationPosition));
                }

                if (!globalColorEnabled || !applyGlobalColorOverride || globalColorKeys == null) return;

                for (int i = 0; i < globalColorKeys.Count; i++)
                {
                    string key = globalColorKeys[i];
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!mat.HasProperty(key)) continue;

                    if (blendMode == ColorBlendMode.Replace)
                    {
                        mat.SetColor(key, globalColor);
                    }
                    else
                    {
                        Color current = mat.GetColor(key);
                        mat.SetColor(key, current * globalColor);
                    }
                }
            }
        }

        /// <summary>
        /// A particle system that is animated by the engines component.
        /// Extended: optional startLifetime & startColor animation, plus global speed/distance/color overrides.
        /// Also optionally forces particle MATERIAL tint keys via MaterialPropertyBlock for closer color match.
        /// </summary>
        [System.Serializable]
        public class AnimatedParticle
        {
            [Tooltip("The particle system to animate.")]
            [SerializeField] protected ParticleSystem particleSystem;

            protected ParticleSystem.MainModule mainModule;
            protected ParticleSystem.EmissionModule emissionModule;

            [Tooltip("Whether to animate the particle system's start speed.")]
            [SerializeField] protected bool animateStartSpeed;

            [Tooltip("The particle system's start speed over the duration of the animation.")]
            [SerializeField] protected AnimationCurve startSpeedCurve;

            [Tooltip("Whether to animate the particle system's start lifetime (controls exhaust length / distance).")]
            [SerializeField] protected bool animateStartLifetime;

            [Tooltip("The particle system's start lifetime over the duration of the animation.")]
            [SerializeField] protected AnimationCurve startLifetimeCurve;

            [Tooltip("Whether to animate the particle system's start color.")]
            [SerializeField] protected bool animateStartColor;

            [Tooltip("The particle system's start color over the duration of the animation.")]
            [SerializeField, GradientUsage(true)] protected Gradient startColorGradient;

            [Tooltip("If true, this particle system receives the controller's global exhaust speed override.")]
            [SerializeField] protected bool applyGlobalSpeedOverride = true;

            [Tooltip("If true, this particle system receives the controller's global exhaust distance override (lifetime scaling).")]
            [SerializeField] protected bool applyGlobalDistanceOverride = true;

            [Tooltip("If true, this particle system receives the controller's global exhaust color override (startColor).")]
            [SerializeField] protected bool applyGlobalColorOverride = true;

            [Tooltip("If true, also forces particle renderer material tint using MaterialPropertyBlock.")]
            [SerializeField] protected bool applyGlobalColorToParticleMaterial = true;

            // Baselines so overrides don't permanently destroy authoring
            protected ParticleSystem.MinMaxCurve baseStartSpeed;
            protected ParticleSystem.MinMaxCurve baseStartLifetime;
            protected ParticleSystem.MinMaxGradient baseStartColor;

            // MaterialPropertyBlock for particle renderer tint
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
                bool globalSpeedEnabled,
                float globalSpeedMul,
                bool globalDistanceEnabled,
                float globalDistanceMul,
                bool globalColorEnabled,
                Color globalColor,
                ColorBlendMode blendMode,
                bool forceMaterialTint,
                List<string> particleMatKeys)
            {
                if (particleSystem == null) return;

                emissionModule.enabled = !Mathf.Approximately(animationPosition, 0f);

                // Speed
                float speed = (animateStartSpeed && startSpeedCurve != null)
                    ? startSpeedCurve.Evaluate(animationPosition)
                    : EnginesExhaustController.EvaluateMinMaxCurve(baseStartSpeed, animationPosition);

                if (globalSpeedEnabled && applyGlobalSpeedOverride)
                    speed *= globalSpeedMul;

                mainModule.startSpeed = speed;

                // Distance/Length via lifetime
                float lifetime = (animateStartLifetime && startLifetimeCurve != null)
                    ? startLifetimeCurve.Evaluate(animationPosition)
                    : EnginesExhaustController.EvaluateMinMaxCurve(baseStartLifetime, animationPosition);

                if (globalDistanceEnabled && applyGlobalDistanceOverride)
                    lifetime *= globalDistanceMul;

                mainModule.startLifetime = lifetime;

                // Start Color
                Color col = (animateStartColor && startColorGradient != null)
                    ? startColorGradient.Evaluate(animationPosition)
                    : EnginesExhaustController.EvaluateMinMaxGradient(baseStartColor, animationPosition);

                if (globalColorEnabled && applyGlobalColorOverride)
                {
                    if (blendMode == ColorBlendMode.Replace) col = globalColor;
                    else col = col * globalColor;
                }

                mainModule.startColor = col;

                // Optional: also force PARTICLE MATERIAL tint keys for closer match
                bool doMatTint =
                    globalColorEnabled &&
                    forceMaterialTint &&
                    applyGlobalColorToParticleMaterial &&
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

                        // Setting unused keys is harmless; only keys the shader uses will matter.
                        if (blendMode == ColorBlendMode.Replace)
                        {
                            mpb.SetColor(key, globalColor);
                        }
                        else
                        {
                            // Multiply mode: multiply against white (exact "existing" readback isn't available reliably here).
                            mpb.SetColor(key, Color.white * globalColor);
                        }
                    }

                    psRenderer.SetPropertyBlock(mpb);
                }
            }
        }

        /// <summary>
        /// A transform that is animated by the engines component.
        /// Extended: optional scaling by global distance multiplier (useful for mesh/trail exhaust).
        /// </summary>
        [System.Serializable]
        public class AnimatedTransform
        {
            [Tooltip("The transform to animate.")]
            [SerializeField] protected Transform m_Transform;

            [Tooltip("The scale that the transform has when the control value is 0.")]
            [SerializeField] protected Vector3 startScale = new Vector3(1, 1, 1);

            [Tooltip("The scale that the transform has when the control value is 1.")]
            [SerializeField] protected Vector3 endScale = new Vector3(1, 1, 1);

            [Tooltip("The curve for transitioning between start and end values.")]
            [SerializeField] protected AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

            [Tooltip("If true, multiply this transform's scale by the controller's global exhaust distance multiplier.")]
            [SerializeField] protected bool applyGlobalDistanceOverride = false;

            [Tooltip("Axis mask for distance scaling (typical: (0,0,1) to stretch in local Z only).")]
            [SerializeField] protected Vector3 distanceScaleMask = new Vector3(0, 0, 1);

            public virtual void Update(float animationPosition, bool globalDistanceEnabled, float globalDistanceMul)
            {
                if (m_Transform == null) return;

                float val = (curve != null) ? curve.Evaluate(animationPosition) : animationPosition;
                Vector3 s = val * endScale + (1 - val) * startScale;

                if (globalDistanceEnabled && applyGlobalDistanceOverride)
                {
                    float d = Mathf.Max(0f, globalDistanceMul);
                    Vector3 mul = Vector3.one + Vector3.Scale(distanceScaleMask, new Vector3(d - 1f, d - 1f, d - 1f));
                    s = Vector3.Scale(s, mul);
                }

                m_Transform.localScale = s;
            }
        }

        /// <summary>
        /// A light that is animated by the Engines component.
        /// Extended: optional global color override/blend.
        /// </summary>
        [System.Serializable]
        public class AnimatedLight
        {
            [Tooltip("The animated light.")]
            public Light m_Light;

            [Tooltip("The color of the light over the course of the animation.")]
            [GradientUsage(true)]
            public Gradient lightColor;

            [Tooltip("The light intensity over the range of the engine control.")]
            public AnimationCurve lightIntensity = AnimationCurve.Linear(0, 0, 1, 1);

            public virtual void Update(float level, bool globalColorEnabled, Color globalColor, ColorBlendMode blendMode)
            {
                if (m_Light == null) return;

                Color baseCol = (lightColor != null) ? lightColor.Evaluate(level) : m_Light.color;

                if (globalColorEnabled)
                {
                    m_Light.color = (blendMode == ColorBlendMode.Replace) ? globalColor : (baseCol * globalColor);
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

        public enum MovementAxis
        {
            Procedural,
            None,
            Horizontal,
            Vertical,
            Forward
        }

        public enum SteeringAxis
        {
            Procedural,
            None,
            Pitch,
            Yaw,
            Roll
        }

        [Tooltip("The steering axes to show effects for.")]
        [SerializeField] protected SteeringAxis steeringAxis;

        [Tooltip("The movement axes to show effects for.")]
        [SerializeField] protected MovementAxis movementAxis;

        public enum EngineMode
        {
            All,
            Cruising,
            Boost
        }

        [System.Serializable]
        public class EngineModeAnimationSettings
        {
            [Tooltip("The engine mode that drives the effects.")]
            public EngineMode mode;

            [Tooltip("The particles animated by the engine.")]
            public List<AnimatedParticle> animatedParticles = new List<AnimatedParticle>();

            [Tooltip("The renderers animated by the engine.")]
            public List<AnimatedRenderer> animatedRenderers = new List<AnimatedRenderer>();

            [Tooltip("The transforms animated by the engine.")]
            public List<AnimatedTransform> animatedTransforms = new List<AnimatedTransform>();

            [Tooltip("The lights animated by the engine.")]
            public List<AnimatedLight> animatedLights = new List<AnimatedLight>();

            public virtual void Initialize()
            {
                foreach (AnimatedParticle animatedParticle in animatedParticles)
                    animatedParticle.Initialize();

                foreach (AnimatedRenderer animatedRenderer in animatedRenderers)
                    animatedRenderer.Initialize();
            }

            public virtual void Update(
                float level,
                bool globalSpeedEnabled, float globalSpeedMul,
                bool globalDistanceEnabled, float globalDistanceMul,
                bool globalColorEnabled, Color globalColor, ColorBlendMode blendMode,
                bool applyColorToParticles,
                bool applyColorToParticleMaterials,
                List<string> particleMatKeys,
                bool applyColorToRenderers,
                List<string> rendererColorKeys,
                bool applyColorToLights)
            {
                bool particleColorEnabled = globalColorEnabled && applyColorToParticles;
                bool particleMatEnabled = globalColorEnabled && applyColorToParticleMaterials;
                bool rendererColorEnabled = globalColorEnabled && applyColorToRenderers;
                bool lightColorEnabled = globalColorEnabled && applyColorToLights;

                foreach (AnimatedParticle animatedParticle in animatedParticles)
                {
                    animatedParticle.Update(
                        level,
                        globalSpeedEnabled, globalSpeedMul,
                        globalDistanceEnabled, globalDistanceMul,
                        particleColorEnabled, globalColor,
                        blendMode,
                        particleMatEnabled,
                        particleMatKeys);
                }

                foreach (AnimatedRenderer animatedRenderer in animatedRenderers)
                {
                    animatedRenderer.Update(
                        level,
                        rendererColorEnabled, globalColor,
                        blendMode,
                        rendererColorKeys);
                }

                foreach (AnimatedTransform animatedTransform in animatedTransforms)
                {
                    animatedTransform.Update(level, globalDistanceEnabled, globalDistanceMul);
                }

                foreach (AnimatedLight animatedLight in animatedLights)
                {
                    animatedLight.Update(level, lightColorEnabled, globalColor, blendMode);
                }
            }
        }

        [Tooltip("The visual effects settings for the engine.")]
        public List<EngineModeAnimationSettings> settings = new List<EngineModeAnimationSettings>();


        protected virtual void Reset()
        {
            engines = GetComponent<Engines>();
        }

        protected virtual void Awake()
        {
            foreach (EngineModeAnimationSettings setting in settings)
                setting.Initialize();
        }

        protected virtual void LateUpdate()
        {
            if (engines == null) return;

            Transform com = (centerOfMass != null) ? centerOfMass : engines.transform;

            Vector3 thrusterLocalPos = com.InverseTransformPoint(transform.position);
            Vector3 thrusterLocalDirection = com.InverseTransformDirection(transform.forward);

            // Movement
            Vector3 translationAxis;
            switch (movementAxis)
            {
                case MovementAxis.Procedural:
                    translationAxis = engines.ModulatedMovementInputs;
                    break;
                case MovementAxis.Horizontal:
                    translationAxis = new Vector3(engines.ModulatedMovementInputs.x, 0, 0);
                    break;
                case MovementAxis.Vertical:
                    translationAxis = new Vector3(0, engines.ModulatedMovementInputs.y, 0);
                    break;
                case MovementAxis.Forward:
                    translationAxis = new Vector3(0, 0, engines.ModulatedMovementInputs.z);
                    break;
                default:
                    translationAxis = Vector3.zero;
                    break;
            }
            float movementAmount = Mathf.Clamp(-Vector3.Dot(thrusterLocalDirection, translationAxis), 0, 1);

            // Steering
            Vector3 rotationAxis;
            switch (steeringAxis)
            {
                case SteeringAxis.Procedural:
                    rotationAxis = engines.ModulatedSteeringInputs;
                    break;
                case SteeringAxis.Pitch:
                    rotationAxis = new Vector3(engines.ModulatedSteeringInputs.x, 0, 0);
                    break;
                case SteeringAxis.Yaw:
                    rotationAxis = new Vector3(0, engines.ModulatedSteeringInputs.y, 0);
                    break;
                case SteeringAxis.Roll:
                    rotationAxis = new Vector3(0, 0, engines.ModulatedSteeringInputs.z);
                    break;
                default:
                    rotationAxis = Vector3.zero;
                    break;
            }

            Vector3 tmp = Vector3.ProjectOnPlane(thrusterLocalPos, thrusterLocalDirection).normalized;
            if (Mathf.Abs(tmp.x) > 0.01f) tmp.x = Mathf.Sign(tmp.x);
            if (Mathf.Abs(tmp.y) > 0.01f) tmp.y = Mathf.Sign(tmp.y);
            if (Mathf.Abs(tmp.z) > 0.01f) tmp.z = Mathf.Sign(tmp.z);

            float steeringAmount = Mathf.Clamp(
                -Vector3.Dot(Vector3.Cross(rotationAxis, tmp), thrusterLocalDirection.normalized),
                0, 1);

            // Thruster level
            float level = Mathf.Min(movementAmount + steeringAmount, 1f);

            // Global override values
            bool colorEnabled = overrideExhaustColor && exhaustColorByLevel != null;
            Color globalColor = colorEnabled ? (exhaustColorByLevel.Evaluate(level) * exhaustColorTint) : Color.white;

            bool speedEnabled = overrideExhaustSpeed;
            float globalSpeedMul = speedEnabled
                ? exhaustSpeedMultiplier * (exhaustSpeedByLevel != null ? exhaustSpeedByLevel.Evaluate(level) : 1f)
                : 1f;

            bool distEnabled = overrideExhaustDistance;
            float globalDistMul = distEnabled
                ? exhaustDistanceMultiplier * (exhaustDistanceByLevel != null ? exhaustDistanceByLevel.Evaluate(level) : 1f)
                : 1f;

            for (int i = 0; i < settings.Count; i++)
            {
                EngineModeAnimationSettings setting = settings[i];

                switch (setting.mode)
                {
                    case EngineMode.All:
                        setting.Update(
                            level,
                            speedEnabled, globalSpeedMul,
                            distEnabled, globalDistMul,
                            colorEnabled, globalColor, exhaustColorBlend,
                            applyColorToParticles,
                            applyColorToParticleMaterials,
                            particleMaterialColorPropertyKeys,
                            applyColorToRenderers,
                            rendererColorPropertyKeys,
                            applyColorToLights);
                        break;

                    case EngineMode.Cruising:
                        if (engines.ModulatedBoostInputs.magnitude < 0.5f)
                        {
                            setting.Update(
                                level,
                                speedEnabled, globalSpeedMul,
                                distEnabled, globalDistMul,
                                colorEnabled, globalColor, exhaustColorBlend,
                                applyColorToParticles,
                                applyColorToParticleMaterials,
                                particleMaterialColorPropertyKeys,
                                applyColorToRenderers,
                                rendererColorPropertyKeys,
                                applyColorToLights);
                        }
                        break;

                    case EngineMode.Boost:
                        if (engines.ModulatedBoostInputs.magnitude >= 0.5f)
                        {
                            // Keep original behavior: boost settings run at full level
                            float boostLevel = 1f;

                            Color boostColor = colorEnabled ? (exhaustColorByLevel.Evaluate(boostLevel) * exhaustColorTint) : Color.white;
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
                                colorEnabled, boostColor, exhaustColorBlend,
                                applyColorToParticles,
                                applyColorToParticleMaterials,
                                particleMaterialColorPropertyKeys,
                                applyColorToRenderers,
                                rendererColorPropertyKeys,
                                applyColorToLights);
                        }
                        break;
                }
            }
        }

        // =========================
        // Helpers for MinMaxCurve / MinMaxGradient
        // =========================

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
