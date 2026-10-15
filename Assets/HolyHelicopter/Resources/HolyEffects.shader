Shader "HolyHelicopter/Effects"
{
    Properties { _Tint("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float fog:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.fog = ComputeFogFactor(OUT.positionCS.z); return OUT;
            }
            half4 frag(Varyings IN):SV_Target
            {
                return half4(MixFog(IN.color.rgb * 1.4, IN.fog),1);
            }
            ENDHLSL
        }
    }
}
