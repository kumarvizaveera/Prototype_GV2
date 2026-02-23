Shader "Custom/SkyboxCubemapRotatable"
{
    Properties
    {
        _Tex ("Cubemap", CUBE) = "" {}
        _RotationX ("Rotation X", Range(0, 360)) = 0
        _RotationY ("Rotation Y", Range(0, 360)) = 0
        _RotationZ ("Rotation Z", Range(0, 360)) = 0
        _Exposure ("Exposure", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Tex;
            float _RotationX, _RotationY, _RotationZ;
            float _Exposure;

            float3 RotateX(float3 v, float degrees)
            {
                float rad = radians(degrees);
                float s = sin(rad); float c = cos(rad);
                return float3(v.x, c*v.y - s*v.z, s*v.y + c*v.z);
            }

            float3 RotateY(float3 v, float degrees)
            {
                float rad = radians(degrees);
                float s = sin(rad); float c = cos(rad);
                return float3(c*v.x + s*v.z, v.y, -s*v.x + c*v.z);
            }

            float3 RotateZ(float3 v, float degrees)
            {
                float rad = radians(degrees);
                float s = sin(rad); float c = cos(rad);
                return float3(c*v.x - s*v.y, s*v.x + c*v.y, v.z);
            }

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)UNITY_MATRIX_M, v.vertex.xyz);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = i.dir;

                d = RotateX(d, _RotationX);
                d = RotateY(d, _RotationY);
                d = RotateZ(d, _RotationZ);

                return texCUBE(_Tex, d) * _Exposure;
            }
            ENDCG
        }
    }
}