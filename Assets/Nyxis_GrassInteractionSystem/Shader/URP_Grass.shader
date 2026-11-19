Shader "Nyxis/URP_Grass"
{
Properties
{
    [Header(Blade Texture)]
    [MainTexture] _MainTex("Alpha Texture (RGB = tint, A = shape cutout)", 2D) = "white" {}

    [Header(Anime Colors)]
    _TopColor("Top (Sun) Color", Color) = (0.8, 0.9, 0.4, 1)
    _BottomColor("Root (Shadow) Color", Color) = (0.0, 0.4, 0.35, 1)
    _ShadowTint("Shadow Tint", Color) = (0.0, 0.2, 0.2, 1)

    [Header(Shape)]
    _BladeHeight("Blade Height", Float) = 1.0
    [Range(0,1)]
    _FlattenNormals("Flatten Normals", Float) = 1.0

    [Header(Wind)]
    _WindSpeed("Wind Speed", Float) = 1.0
    _WindStrength("Wind Strength", Float) = 0.2

    [Header(Player Interaction)]
    _PushStrength("Interaction Power", Float) = 3.0
    _BendDownStrength("Bend Down Strength", Range(0,1)) = 0.5
    [Range(1,4)]
    _FalloffSoftness("Falloff Softness", Float) = 2.0

    [Header(Alpha)]
    _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
}

SubShader
{
    Tags
    {
        "RenderType" = "Opaque"
        "Queue" = "Geometry+100"
        "RenderPipeline" = "UniversalPipeline"
    }

    LOD 300
    Cull Off
    ZWrite On

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);

    CBUFFER_START(UnityPerMaterial)

        float4 _MainTex_ST;

        float4 _TopColor;
        float4 _BottomColor;
        float4 _ShadowTint;

        float _BladeHeight;
        float _FlattenNormals;

        float _WindSpeed;
        float _WindStrength;
        float _PushStrength;
        float _BendDownStrength;
        float _FalloffSoftness;

        float _Cutoff;

    CBUFFER_END

    float3 _GlobalPlayerPos;
    float _GlobalPlayerRadius;




    float3 ApplyMovement(float3 worldPos, float heightFactor)
    {
        float time = _Time.y * _WindSpeed;

        float windWave =
            sin(
                worldPos.x * 0.5 +
                worldPos.z * 0.5 +
                time
            );

        float3 windOffset =
            float3(
                windWave,
                0.0,
                windWave
            )
            * 0.1
            * _WindStrength
            * heightFactor;




        float2 grassXZ = worldPos.xz;
        float2 playerXZ = _GlobalPlayerPos.xz;

        float distanceToPlayer =
            distance(grassXZ, playerXZ);

        float radius =
            max(_GlobalPlayerRadius, 0.1);



        float3 interactionOffset = float3(0, 0, 0);

        if (distanceToPlayer < radius)
        {
            float interaction =
                smoothstep(
                    radius,
                    0.0,
                    distanceToPlayer
                );


            interaction = pow(interaction, max(_FalloffSoftness, 0.001));


            float3 pushDirection =
                worldPos - _GlobalPlayerPos;

            pushDirection.y = 0.0;

            float pushLength =
                max(length(pushDirection), 0.001);

            pushDirection /= pushLength;


            interactionOffset =
                pushDirection
                * interaction
                * _PushStrength
                * heightFactor;




            interactionOffset.y -=
                interaction
                * _BendDownStrength
                * heightFactor;
        }


        return windOffset + interactionOffset;
    }



    struct GrassAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;

        UNITY_VERTEX_INPUT_INSTANCE_ID
    };


    struct GrassVaryings
    {
        float4 positionCS : SV_POSITION;

        float3 worldPos : TEXCOORD0;
        float3 normalWS : TEXCOORD1;

        float height : TEXCOORD2;
        float2 uv     : TEXCOORD3;

        UNITY_VERTEX_INPUT_INSTANCE_ID
    };


    GrassVaryings GrassVertex(GrassAttributes IN)
    {
        GrassVaryings OUT;

        UNITY_SETUP_INSTANCE_ID(IN);
        UNITY_TRANSFER_INSTANCE_ID(IN, OUT);


        float3 worldPos =
            TransformObjectToWorld(
                IN.positionOS.xyz
            );


        float height =
            saturate(
                IN.positionOS.y /
                max(_BladeHeight, 0.001)
            );




        worldPos +=
            ApplyMovement(
                worldPos,
                height
            );


        OUT.worldPos = worldPos;
        OUT.height = height;
        OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);


        // Normal

        float3 originalNormal =
            TransformObjectToWorldNormal(
                IN.normalOS
            );


        OUT.normalWS =
            normalize(
                lerp(
                    originalNormal,
                    float3(0,1,0),
                    _FlattenNormals
                )
            );


        OUT.positionCS =
            TransformWorldToHClip(
                worldPos
            );


        return OUT;
    }

    ENDHLSL



    Pass
    {
        Name "UniversalForward"

        Tags
        {
            "LightMode" = "UniversalForward"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLPROGRAM

        #pragma target 4.5

        #pragma vertex Vert
        #pragma fragment Frag

        #pragma multi_compile_instancing

        // --- Unity 6 URP light-loop / shadow keywords ---
        #pragma multi_compile _ _FORWARD_PLUS
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
        #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
        #pragma multi_compile _ SHADOWS_SHADOWMASK
        #pragma multi_compile _ DIRLIGHTMAP_COMBINED
        #pragma multi_compile_fragment _ _LIGHT_LAYERS
        #pragma multi_compile_fragment _ _LIGHT_COOKIES
        #pragma multi_compile _ _CLUSTERED_RENDERING

        #pragma multi_compile_fog


        GrassVaryings Vert(
            GrassAttributes IN
        )
        {
            return GrassVertex(IN);
        }


        half4 Frag(
            GrassVaryings IN
        ) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(IN);



            half4 texColor =
                SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    IN.uv
                );

            clip(texColor.a - _Cutoff);




            float4 shadowCoord =
                TransformWorldToShadowCoord(
                    IN.worldPos
                );


            Light mainLight =
                GetMainLight(
                    shadowCoord
                );


            float shadow =
                mainLight.shadowAttenuation;




            float3 grassColor =
                lerp(
                    _BottomColor.rgb,
                    _TopColor.rgb,
                    IN.height
                )
                * texColor.rgb;




            float3 normal =
                normalize(
                    IN.normalWS
                );


            float3 lightDirection =
                normalize(
                    mainLight.direction
                );


            float NdotL =
                dot(
                    normal,
                    lightDirection
                );


            float lighting =
                smoothstep(
                    -0.1,
                    0.5,
                    NdotL * shadow
                );




            float3 shadowColor =
                grassColor *
                _ShadowTint.rgb;


            float3 finalColor =
                lerp(
                    shadowColor,
                    grassColor,
                    lighting
                );


   

            float3 ambient =
                SampleSH(normal);


            finalColor +=
                ambient *
                grassColor *
                0.1;



            float fogFactor =
                ComputeFogFactor(
                    IN.positionCS.z
                );


            finalColor =
                MixFog(
                    finalColor,
                    fogFactor
                );


            return half4(
                finalColor,
                1.0
            );
        }

        ENDHLSL
    }




    Pass
    {
        Name "ShadowCaster"

        Tags
        {
            "LightMode" = "ShadowCaster"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLPROGRAM

        #pragma target 4.5

        #pragma vertex ShadowVert
        #pragma fragment ShadowFrag

        #pragma multi_compile_instancing


        struct ShadowAttributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;

            UNITY_VERTEX_INPUT_INSTANCE_ID
        };


        struct ShadowVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;

            UNITY_VERTEX_INPUT_INSTANCE_ID
        };


        ShadowVaryings ShadowVert(
            ShadowAttributes IN
        )
        {
            ShadowVaryings OUT;

            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_TRANSFER_INSTANCE_ID(IN, OUT);


            float3 worldPos =
                TransformObjectToWorld(
                    IN.positionOS.xyz
                );


            float height =
                saturate(
                    IN.positionOS.y /
                    max(_BladeHeight, 0.001)
                );


            worldPos +=
                ApplyMovement(
                    worldPos,
                    height
                );


            OUT.positionCS =
                TransformWorldToHClip(
                    worldPos
                );

            OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);


            return OUT;
        }


        half4 ShadowFrag(
            ShadowVaryings IN
        ) : SV_Target
        {
            half alpha =
                SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    IN.uv
                ).a;

            clip(alpha - _Cutoff);

            return 0;
        }

        ENDHLSL
    }




    Pass
    {
        Name "DepthOnly"

        Tags
        {
            "LightMode" = "DepthOnly"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLPROGRAM

        #pragma target 4.5

        #pragma vertex DepthVert
        #pragma fragment DepthFrag

        #pragma multi_compile_instancing


        struct DepthAttributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;

            UNITY_VERTEX_INPUT_INSTANCE_ID
        };


        struct DepthVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;

            UNITY_VERTEX_INPUT_INSTANCE_ID
        };


        DepthVaryings DepthVert(
            DepthAttributes IN
        )
        {
            DepthVaryings OUT;

            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_TRANSFER_INSTANCE_ID(IN, OUT);


            float3 worldPos =
                TransformObjectToWorld(
                    IN.positionOS.xyz
                );


            float height =
                saturate(
                    IN.positionOS.y /
                    max(_BladeHeight, 0.001)
                );


            worldPos +=
                ApplyMovement(
                    worldPos,
                    height
                );


            OUT.positionCS =
                TransformWorldToHClip(
                    worldPos
                );

            OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);


            return OUT;
        }


        half4 DepthFrag(
            DepthVaryings IN
        ) : SV_Target
        {
            half alpha =
                SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    IN.uv
                ).a;

            clip(alpha - _Cutoff);

            return 0;
        }

        ENDHLSL
    }




    Pass
    {
        Name "DepthNormals"

        Tags
        {
            "LightMode" = "DepthNormals"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLPROGRAM

        #pragma target 4.5

        #pragma vertex DepthNormalsVert
        #pragma fragment DepthNormalsFrag

        #pragma multi_compile_instancing


        struct DepthNormalsAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;

            UNITY_VERTEX_INPUT_INSTANCE_ID
        };


        struct DepthNormalsVaryings
        {
            float4 positionCS : SV_POSITION;

            float3 normalWS : TEXCOORD0;
            float2 uv : TEXCOORD1;

            UNITY_VERTEX_INPUT_INSTANCE_ID
        };


        DepthNormalsVaryings DepthNormalsVert(
            DepthNormalsAttributes IN
        )
        {
            DepthNormalsVaryings OUT;

            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_TRANSFER_INSTANCE_ID(IN, OUT);


            float3 worldPos =
                TransformObjectToWorld(
                    IN.positionOS.xyz
                );


            float height =
                saturate(
                    IN.positionOS.y /
                    max(_BladeHeight, 0.001)
                );


            worldPos +=
                ApplyMovement(
                    worldPos,
                    height
                );


            OUT.positionCS =
                TransformWorldToHClip(
                    worldPos
                );


            OUT.normalWS =
                normalize(
                    TransformObjectToWorldNormal(
                        IN.normalOS
                    )
                );

            OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);


            return OUT;
        }


        half4 DepthNormalsFrag(
            DepthNormalsVaryings IN
        ) : SV_Target
        {
            half alpha =
                SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    IN.uv
                ).a;

            clip(alpha - _Cutoff);

            float3 normal =
                normalize(
                    IN.normalWS
                );


            return half4(
                normal * 0.5 + 0.5,
                0.0
            );
        }

        ENDHLSL
    }
}


}
