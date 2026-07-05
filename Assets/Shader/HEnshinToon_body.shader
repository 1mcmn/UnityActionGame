Shader "GenshinToon/Body"
{
    Properties//外放给外界的属性
    {   [Header(Textures)]
        _BaseMap("Base Map",2D)="white"{}//基础纹理
    }
    SubShader //子着色器
    {
        Tags{

            "RenderPipeLine"="UniversalRenderPipeLine"//指定渲染管线：URP
            "RenderType"="Opaque"//指定渲染类型：不透明
            }
            HLSLINCLUDE//公共代码块
            //预处理指令，头文件，常量定义，函数定义
            #pragma multi_compile _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _MAIN_LIGHT_SCREEN

            #pragma multi_compile_fragment _LIGHT_LAYERS
            #pragma multi_compile_fragment _LIGHT_COOKIES
            #pragma multi_compile_fragment _SCREEN_SPACE_OCClUSION
            #pragma multi_compile_fragment _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _SHADOWS_SOFT
            #pragma multi_compile_fragment _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _REFLECTION_PROBE_BOX_PROJECTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.uinty.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            CBUFFER_START(UnityPerMaterial)

                    sampler2D _BaseMap;//基础纹理
            CBUFFER_END

            ENDHLSL//代码块结束
            Pass//渲染通道
            {
            Name "universalForward"//通道名称
                Tags{
            
                "LightMode"="universalForward"//光照模型：向前渲染
                }
                HLSLINCLUDE//
                #pragma Vertex MainVS
                #pragma fragment MainFS
                //顶点着色器函数：返回裁剪空间坐标
                void MainVS(float4 positionOS:POSITION)
                {
                    Vertex

                }
                //片元着色器函数：返回颜色（RGBA）
                void MainFS()
                {
                    return float4(1,1,1,1);
                }
                ENDHLSL

            }
    }
}
