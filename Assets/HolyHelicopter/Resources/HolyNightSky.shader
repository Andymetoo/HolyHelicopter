Shader "HolyHelicopter/NightSky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; };
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.direction=i.positionOS.xyz; return o; }
            float Hash(float3 p) { p=frac(p*0.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
            half4 Frag(Varyings i):SV_Target
            {
                float3 d=normalize(i.direction);
                float h=saturate(d.y);
                half3 col=lerp(half3(0.012,0.024,0.042),half3(0.003,0.006,0.019),sqrt(h));
                float3 cell=floor(d*420);
                float stars=step(0.9978,Hash(cell))*pow(saturate(1-length(frac(d*420)-0.5)*2),3)*smoothstep(0.05,0.3,h);
                col+=stars*half3(0.65,0.83,0.92);
                float3 moonDir=normalize(float3(-0.45,0.65,0.9));
                float moon=dot(d,moonDir);
                float halo=pow(saturate(moon),90)*0.08;
                col+=halo*half3(0.4,0.8,0.75);
                float disc=smoothstep(0.9979,0.99805,moon);
                float crater=0.82+0.1*sin(d.x*180)*sin(d.y*130)+0.08*sin(d.x*290+d.y*160);
                col=lerp(col,half3(0.65,0.82,0.72)*crater,disc);
                return half4(col,1);
            }
            ENDHLSL
        }
    }
}
