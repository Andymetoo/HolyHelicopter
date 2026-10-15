Shader "HolyHelicopter/HordeBillboard"
{
    Properties
    {
        _MainTex("Pixel monsters", 2D) = "white" {}
        _ShadowPass("Foot shadows", Float) = 0
        _ZWrite("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "PixelSwarm"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float _ShadowPass;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
                float flash : TEXCOORD2;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                // CPU-packed camera-facing corners use a normal scene MeshRenderer. This is
                // dependable under both URP render graph and its compatibility renderer.
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                output.flash = input.color.r;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                if (_ShadowPass > .5)
                {
                    float distance = length((input.uv - .5) * 2);
                    clip(.94 - distance);
                    return half4(MixFog(half3(.014,.018,.025), input.fog), saturate((1 - distance) * .44));
                }
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(color.a - .5);
                color.rgb = lerp(color.rgb, half3(1,.98,.65), saturate(input.flash));
                color.rgb = MixFog(color.rgb, input.fog);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
