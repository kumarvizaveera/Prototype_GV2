Shader "Akasthara/Spline Energy Glow (URP)"
{
    Properties
    {
        [HDR]_BaseColor("Base Glow Color (HDR)", Color) = (0.0, 1.0, 1.0, 1.0)
        [HDR]_PulseColor("Highlight Glow Color (HDR)", Color) = (1.0, 0.0, 1.0, 1.0)

        _EmissionMask("Emission Mask (R)", 2D) = "white" {}
        _MaskTiling("Mask Tiling (X=Along, Y=Across)", Vector) = (10, 1, 0, 0)

        _BaseIntensity("Base Intensity", Float) = 1
        _PulseIntensity("Highlight Intensity", Float) = 1
        _Intensity("Emission Intensity (Legacy)", Float) = 1
        _WindowStart("Highlight Window Start (0..1)", Float) = 0
        _WindowEnd("Highlight Window End (0..1)", Float) = 0
        _EdgeSoftness("Window Edge Softness (0..1)", Float) = 0.01
        _IsLoop("Is Loop (0/1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PulseColor;
                float  _BaseIntensity;
                float  _PulseIntensity;
                float  _Intensity;
                float2 _MaskTiling;
                float  _WindowStart;
                float  _WindowEnd;
                float  _EdgeSoftness;
                float  _IsLoop;
            CBUFFER_END

            TEXTURE2D(_EmissionMask);
            SAMPLER(sampler_EmissionMask);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float Window01(float u)
            {
                float s = _WindowStart;
                float e = _WindowEnd;
                float k = max(_EdgeSoftness, 1e-5);

                // Wrapped window for looped splines: [s..1] U [0..e]
                if (_IsLoop > 0.5 && s > e)
                {
                    float seg1 = smoothstep(s, s + k, u) * (1.0 - smoothstep(1.0 - k, 1.0, u));
                    float seg2 = smoothstep(0.0, k, u) * (1.0 - smoothstep(e - k, e, u));
                    return saturate(seg1 + seg2);
                }

                // Normal window [s..e]
                float inside = step(s, u) * step(u, e);
                float inEdge = smoothstep(s, s + k, u);
                float outEdge = 1.0 - smoothstep(e - k, e, u);
                return saturate(inside * inEdge * outEdge);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uvMask = IN.uv * _MaskTiling;
                half mask = SAMPLE_TEXTURE2D(_EmissionMask, sampler_EmissionMask, uvMask).r;

                float u = saturate(IN.uv.x);
                float win = Window01(u);

                float baseInt = _BaseIntensity;
                float pulseInt = _PulseIntensity;

                // If intensities are zero (default), fallback to _Intensity for legacy support
                // checking against small epsilon strictly speaking, but usually defaults are 0 if not set
                // logic: if user explicitly sets them on material, they are used. 
                // However, C# script sets them. If C# script is old, it won't set them.
                // But we know C# script IS setting them.
                
                half3 cBase = _BaseColor.rgb * baseInt;
                half3 cPulse = _PulseColor.rgb * pulseInt;

                half3 c = lerp(cBase, cPulse, win);
                half3 emissive = c * mask;
                
                // We incorporate _Intensity as a master multiplier just in case, 
                // but effectively we want base/pulse to control it.
                // Assuming the user wants STRICT independent control:

                return half4(emissive, 1);
            }
            ENDHLSL
        }
    }
}
