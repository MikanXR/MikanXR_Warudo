// The URP form of MikanXR/ShadowCatcher, which the mod loads in its place while Warudo runs URP.
// A stencil in the shadow pass writes light survival from the main light's shadow, white where
// nothing shadows it, into a target cleared white. The pass renders the scene twice, with and
// without shadows, and divides the two, so everything but the catchers cancels out. Without
// shadows URP leaves the shadow keywords off and the catcher writes exactly 1. It casts no
// shadow of its own, so it never shadows itself. The pass carries no LightMode tag, which URP
// draws as SRPDefaultUnlit: the mod project builds under the built-in pipeline, whose shader
// stripping removes every pass tagged for a scriptable pipeline.
Shader "MikanXR/ShadowCatcherURP"
{
    Properties
    {
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "MikanStencil" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Catcher"
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _ShadowStrength;
            float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half attenuation = MainLightRealtimeShadow(TransformWorldToShadowCoord(input.positionWS));
                half survival = lerp(1.0h - (half)_ShadowStrength, 1.0h, saturate(attenuation));
                return half4(survival, survival, survival, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
