Shader "Menu/Spatial Grid"
{
    Properties { _BaseColor("Floor",Color)=(.92,.945,.965,1) _LineColor("Lines",Color)=(.30,.40,.50,1) _Spacing("Spacing",Float)=1 _Opacity("Line opacity",Range(0,1))=.32 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _LineColor;
                float _Spacing, _Opacity;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;};
            struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
            V Vert(A v) {V o; o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
            half4 Frag(V i):SV_Target
            {
                float2 grid=i.world.xz/max(.1,_Spacing);
                float2 cell=abs(frac(grid-.5)-.5);
                float2 width=max(fwidth(grid),.0001);
                float gridLine=1-saturate(min(cell.x/width.x,cell.y/width.y));
                float node=1-smoothstep(1.5,2.8,length(cell/width));
                float fade=1-smoothstep(9,35,distance(i.world.xz,_WorldSpaceCameraPos.xz));
                float sheen=.015*sin(i.world.x*.37+i.world.z*.19)+.018*sin(i.world.z*.49);
                float3 baseColor=_BaseColor.rgb+sheen;
                float3 color=lerp(baseColor,_LineColor.rgb,max(gridLine*_Opacity,node*.60)*fade);
                color=lerp(color,float3(.967,.977,.989),1-fade);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
