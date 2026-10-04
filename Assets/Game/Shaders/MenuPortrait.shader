Shader "Menu/Toon Halftone Portrait"
{
    Properties
    {
        _BaseMap("Portrait texture", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _InkColor("Halftone ink", Color) = (.045,.105,.24,1)
        _PaperColor("Halftone paper", Color) = (.93,.96,1,1)
        _Cutoff("Alpha cutoff", Range(0,1)) = .35
        _DotSize("Dot spacing at 1080p", Range(2,12)) = 5
        _StyleAngle("View angle style", Range(-1,1)) = 0
        _WireStrength("Wire strength", Range(0,1)) = .55
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest" }
        Pass
        {
            Name "Portrait"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _InkColor, _PaperColor;
                float _Cutoff, _DotSize, _StyleAngle, _WireStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float3 bary:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; float3 bary:TEXCOORD2; float4 screen:TEXCOORD3; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv,_BaseMap);
                o.bary = v.bary;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                clip(tex.a * _BaseColor.a - _Cutoff);
                Light light = GetMainLight();
                float ndl = dot(normalize(i.normalWS),normalize(light.direction));
                float toon = .57 + .20*smoothstep(.08,.13,ndl) + .23*smoothstep(.48,.53,ndl);
                half3 color = tex.rgb * _BaseColor.rgb * toon;
                float2 screenUV = i.screen.xy / i.screen.w;
                float diagonal = screenUV.y - (screenUV.x-.68)*.22;
                float top = smoothstep(.73 + _StyleAngle*.06, .745 + _StyleAngle*.06, diagonal);
                float bottom = 1-smoothstep(.285 + _StyleAngle*.045, .30 + _StyleAngle*.045, diagonal);
                float luminance = dot(color,float3(.2126,.7152,.0722));
                float spacing = max(2,_DotSize*_ScreenParams.y/1080);
                float2 cell = frac(i.positionCS.xy/spacing)-.5;
                float radius = lerp(.16,.59,1-saturate(luminance));
                float distance = length(cell);
                float aa = max(.02,fwidth(distance));
                float dotMask = 1-smoothstep(radius-aa,radius+aa,distance);
                half3 printColor = lerp(_PaperColor.rgb,_InkColor.rgb,dotMask);
                float3 edges = smoothstep(0,fwidth(i.bary)*1.1,i.bary);
                float wire = 1-min(edges.x,min(edges.y,edges.z));
                half3 wireColor = lerp(printColor,_PaperColor.rgb,wire*_WireStrength);
                color = lerp(color,printColor,bottom);
                color = lerp(color,wireColor,top);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
