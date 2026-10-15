Shader "HolyHelicopter/WorldPalette"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Emission("Emissive", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Emission;
            CBUFFER_END
            float4 _HolyLampPosition;
            float4 _HolyLampDirection;
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; half fog:TEXCOORD0; float3 positionWS:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
                half shade = 0.56 + 0.44 * saturate(dot(n, normalize(float3(-0.45,0.85,0.3))));
                output.color = input.color * _BaseColor;
                output.color.rgb *= lerp(shade, 1.35, saturate(_Emission));
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 beam = input.positionWS - _HolyLampPosition.xyz;
                float distance = max(length(beam), .01);
                half cone = saturate((dot(beam / distance, _HolyLampDirection.xyz) - .84) / .13);
                half pool = cone * cone * saturate(1 - distance / 155) * _HolyLampPosition.w;
                half3 color = input.color.rgb * lerp(.48,1,saturate(_Emission)) + pool * half3(.11,.18,.20) * (1 - saturate(_Emission));
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
    }
}
