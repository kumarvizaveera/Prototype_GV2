// VSXGames Energy Shield Effect Shader - URP Version
Shader "VSX/Radar System/Energy Shield URP" 
{
    Properties
    {
        _MainTex("Texture (RGB)", 2D) = "white" {}
        
        [Header(Hit Effect)]
        _EffectShapeNoiseTex("Effect Shape Noise Texture (RGB)", 2D) = "white" {}
        _EffectShapeAmount("Effect Shape Amount", Range(0, 4)) = 0.5
        _GrowthSpeed("Effect Growth Speed", float) = 8

        _EffectEdgeStrength("Effect Edge Amount", float) = 1
        _EffectEdgeSharpness("Effect Edge Sharpness", float) = 10
        _EffectInnerGlowStrength("Effect Inner Glow Strength", float) = 0.03

        [Header(Rim Glow)]
        [HDR]_RimColor("Rim Color", Color) = (0.75, 2, 4)
        _RimOpacity("Rim Opacity", Range(0, 1)) = 1
        _RimEdgeAmount("Rim Edge Amount", Range(0.5,20)) = 5

        [Header(Effect Instances)]
        // Buffer of hit positions, can add more
        _EffectPosition0 ("Effect Position 0", Vector) = (0,0,0,0)
        _EffectPosition1 ("Effect Position 1", Vector) = (0,0,0,0)
        _EffectPosition2 ("Effect Position 2", Vector) = (0,0,0,0)
        _EffectPosition3 ("Effect Position 3", Vector) = (0,0,0,0)
        _EffectPosition4 ("Effect Position 4", Vector) = (0,0,0,0)
        _EffectPosition5 ("Effect Position 5", Vector) = (0,0,0,0)
        _EffectPosition6 ("Effect Position 6", Vector) = (0,0,0,0)
        _EffectPosition7 ("Effect Position 7", Vector) = (0,0,0,0)
        _EffectPosition8 ("Effect Position 8", Vector) = (0,0,0,0)
        _EffectPosition9 ("Effect Position 9", Vector) = (0,0,0,0)

        [Header(Effect Colors)]
        [HDR] _EffectColor0 ("Effect Color 0", Color) = (0,0,0,1)
        [HDR] _EffectColor1 ("Effect Color 1", Color) = (0,0,0,1)
        [HDR] _EffectColor2 ("Effect Color 2", Color) = (0,0,0,1)
        [HDR] _EffectColor3 ("Effect Color 3", Color) = (0,0,0,1)
        [HDR] _EffectColor4 ("Effect Color 4", Color) = (0,0,0,1)
        [HDR] _EffectColor5 ("Effect Color 5", Color) = (0,0,0,1)
        [HDR] _EffectColor6 ("Effect Color 6", Color) = (0,0,0,1)
        [HDR] _EffectColor7 ("Effect Color 7", Color) = (0,0,0,1)
        [HDR] _EffectColor8 ("Effect Color 8", Color) = (0,0,0,1)
        [HDR] _EffectColor9 ("Effect Color 9", Color) = (0,0,0,1)
    }

    SubShader 
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "EnergyShield"
            Tags { "LightMode" = "UniversalForward" }

            // Standard transparency blending
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD3;
                float3 positionOS : TEXCOORD4; // Pass Object Space position for hit effects
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _EffectShapeNoiseTex_ST;
                
                float4 _RimColor;
                float _RimOpacity;
                float _RimEdgeAmount;

                float _EffectShapeAmount;
                float _GrowthSpeed;
                float _EffectEdgeStrength;
                float _EffectEdgeSharpness;
                float _EffectInnerGlowStrength;

                float4 _EffectPosition0;
                float4 _EffectPosition1;
                float4 _EffectPosition2;
                float4 _EffectPosition3;
                float4 _EffectPosition4;
                float4 _EffectPosition5;
                float4 _EffectPosition6;
                float4 _EffectPosition7;
                float4 _EffectPosition8;
                float4 _EffectPosition9;

                float4 _EffectColor0;
                float4 _EffectColor1;
                float4 _EffectColor2;
                float4 _EffectColor3;
                float4 _EffectColor4;
                float4 _EffectColor5;
                float4 _EffectColor6;
                float4 _EffectColor7;
                float4 _EffectColor8;
                float4 _EffectColor9;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            TEXTURE2D(_EffectShapeNoiseTex);
            SAMPLER(sampler_EffectShapeNoiseTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Position calculations
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionOS = input.positionOS.xyz;

                // Normal and ViewDir calculations for Rim
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, float4(0,0,0,0)); // We don't need tangents
                output.normalWS = normalInput.normalWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                
                return output;
            }
            
            // Helper function to calculate single effect contribution
            half4 CalculateEffect(int index, float4 effectPos, float4 effectColor, float3 fragPosOS, float noise, float innerGlowStrength)
            {
                // w component of effectPos is time
                float size = _GrowthSpeed * effectPos.w;
                
                // Distance in object space
                float dist = distance(fragPosOS, effectPos.xyz);
                
                float amount = size - dist - noise;
                
                // Calculate edge
                float edge = (max((size - abs(amount) * (_EffectEdgeSharpness * size)), 0) / max(size, 0.001));
                
                // Calculate inner glow
                float inner = max(dist, 1);
                // Ensure inner glow is only inside the effect area
                inner = min(max(amount, 0) * 100, 1) * inner;
                
                return (_EffectEdgeStrength * edge + innerGlowStrength * inner) * effectColor;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // --- Rim Effect ---
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                
                // Fresnel effect
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float rim = 1.0 - NdotV;
                rim = pow(rim, _RimEdgeAmount);
                
                half4 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 rimColor = rim * _RimOpacity * _RimColor * mainTexColor;
                
                // --- Hit Effects ---
                float noise = _EffectShapeAmount * SAMPLE_TEXTURE2D(_EffectShapeNoiseTex, sampler_EffectShapeNoiseTex, input.uv).r;
                float innerGlow = max(_EffectInnerGlowStrength, 0);

                half4 hitEffectsColor = half4(0,0,0,0);

                // Unrolling loop not strictly necessary but cleaner for explicit indices
                hitEffectsColor += CalculateEffect(0, _EffectPosition0, _EffectColor0, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(1, _EffectPosition1, _EffectColor1, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(2, _EffectPosition2, _EffectColor2, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(3, _EffectPosition3, _EffectColor3, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(4, _EffectPosition4, _EffectColor4, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(5, _EffectPosition5, _EffectColor5, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(6, _EffectPosition6, _EffectColor6, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(7, _EffectPosition7, _EffectColor7, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(8, _EffectPosition8, _EffectColor8, input.positionOS, noise, innerGlow);
                hitEffectsColor += CalculateEffect(9, _EffectPosition9, _EffectColor9, input.positionOS, noise, innerGlow);

                // Replicate color normalization logic from original
                float totalWeight = 0.0001;
                totalWeight += length(_EffectColor0);
                totalWeight += length(_EffectColor1);
                totalWeight += length(_EffectColor2);
                totalWeight += length(_EffectColor3);
                totalWeight += length(_EffectColor4);
                totalWeight += length(_EffectColor5);
                totalWeight += length(_EffectColor6);
                totalWeight += length(_EffectColor7);
                totalWeight += length(_EffectColor8);
                totalWeight += length(_EffectColor9);

                // The original logic normalizes the SUM based on weights. 
                // However, CalculateEffect already multiplied by color. 
                // Let's look closer at original frag:
                // half4 effectColor0 = (...) * _EffectColor0;
                // result += (length(_EffectColor0) / total) * effectColor0;
                
                // So we need to weight the results we calculated above.
                // Since I added them directly, I need to restructure or apply weights now.
                // Actually my Additive accumulation is slightly different from original weighted average.
                // The original does a weighted average of the effects, then multiplies by MainTex.
                
                // Let's refine the accumulation to match original exactly.
                half4 weightedResult = half4(0,0,0,0);
                
                weightedResult += (length(_EffectColor0) / totalWeight) * CalculateEffect(0, _EffectPosition0, _EffectColor0, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor1) / totalWeight) * CalculateEffect(1, _EffectPosition1, _EffectColor1, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor2) / totalWeight) * CalculateEffect(2, _EffectPosition2, _EffectColor2, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor3) / totalWeight) * CalculateEffect(3, _EffectPosition3, _EffectColor3, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor4) / totalWeight) * CalculateEffect(4, _EffectPosition4, _EffectColor4, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor5) / totalWeight) * CalculateEffect(5, _EffectPosition5, _EffectColor5, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor6) / totalWeight) * CalculateEffect(6, _EffectPosition6, _EffectColor6, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor7) / totalWeight) * CalculateEffect(7, _EffectPosition7, _EffectColor7, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor8) / totalWeight) * CalculateEffect(8, _EffectPosition8, _EffectColor8, input.positionOS, noise, innerGlow);
                weightedResult += (length(_EffectColor9) / totalWeight) * CalculateEffect(9, _EffectPosition9, _EffectColor9, input.positionOS, noise, innerGlow);
                
                weightedResult *= mainTexColor;

                // Combine Rim and Hit Effects
                // Rim has alpha, Hit Effects are additive 'light'.
                // Since we use Premultiplied blending (One OneMinusSrcAlpha), 
                // we should return:
                // RGB = (RimRGB * RimAlpha) + HitEffectRGB
                // Alpha = RimAlpha
                // (Assuming Hit Effects are purely additive light and don't contribute to opacity blocking background)
                
                half4 finalColor;
                finalColor.rgb = rimColor.rgb + weightedResult.rgb;
                finalColor.a = rimColor.a; // Alpha comes from rim only, making shield transparent but glowing
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}
