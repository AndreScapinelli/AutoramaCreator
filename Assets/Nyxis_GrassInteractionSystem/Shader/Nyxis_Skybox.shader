Shader "Nyxis/Skybox"
{
    Properties
    {
        [HDR] _Tint ("Tint Color", Color) = (0.5, 0.5, 0.5, 1)
        _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Cubemap ("Cubemap (HDR)", Cube) = "grey" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "SkyboxPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Cubemap);
            SAMPLER(sampler_Cubemap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                half _Rotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord   : TEXCOORD0;
            };

            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * (3.14159265 / 180.0);
                float sinY, cosY;
                sincos(alpha, sinY, cosY);
                float2x2 m = float2x2(cosY, -sinY, sinY, cosY);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 rotated = RotateAroundYInDegrees(IN.positionOS.xyz, _Rotation);
                OUT.positionCS = TransformObjectToHClip(rotated);
                OUT.texcoord = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURECUBE(_Cubemap, sampler_Cubemap, IN.texcoord);
                half3 color = tex.rgb * _Tint.rgb * _Exposure;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
