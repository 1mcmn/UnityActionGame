Shader "Menu/Native Hero Print"
{
    Properties
    {
        [PerRendererData] _MainTex ("Art", 2D) = "white" {}
        _StyleAngle ("Angle style", Range(-1,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 worldPosition:TEXCOORD1; };
            sampler2D _MainTex; float _StyleAngle; float4 _ClipRect;
            v2f vert(appdata i) { v2f o; o.worldPosition=i.vertex; o.vertex=UnityObjectToClipPos(i.vertex); o.uv=i.uv; o.color=i.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 c=tex2D(_MainTex,i.uv)*i.color;
                float band=1-smoothstep(.03,.038,abs(i.uv.y-(.72-i.uv.x*.19)));
                float dot=1-smoothstep(.18,.27,length(frac(i.uv*185)-.5));
                c.rgb=lerp(c.rgb,float3(.09,.17,.13),dot*band*(.14+_StyleAngle*.1));
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPosition.xy,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
