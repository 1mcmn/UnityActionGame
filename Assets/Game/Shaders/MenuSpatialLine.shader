Shader "Menu/Spatial Line"
{
    Properties { _BaseColor("Color",Color)=(.3,.5,.65,1) _Round("Round node",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Round;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            V Vert(A i) {V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
            half4 Frag(V i):SV_Target
            {
                float alpha=1;
                if(_Round>.5) {float d=length(i.uv-.5);alpha=1-smoothstep(.45-fwidth(d),.45,d);}
                return half4(_BaseColor.rgb*i.color.rgb,_BaseColor.a*i.color.a*alpha);
            }
            ENDHLSL
        }
    }
}
