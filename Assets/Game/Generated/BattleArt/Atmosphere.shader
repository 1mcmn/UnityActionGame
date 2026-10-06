Shader "Interblade/Atmosphere"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.07, 0.12, 0.19, 1)
        _Horizon ("Horizon", Color) = (0.30, 0.37, 0.40, 1)
        _Ground ("Lower hemisphere", Color) = (0.09, 0.11, 0.13, 1)
    }
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
            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith, _Horizon, _Ground;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float height = direction.y;
                float3 sky = height >= 0 ? lerp(_Horizon.rgb, _Zenith.rgb, smoothstep(0, .7, height)) :
                    lerp(_Horizon.rgb, _Ground.rgb, smoothstep(0, .45, -height));
                float2 uv = direction.xz / max(.22, height + .38);
                float veil = Noise(uv * float2(4, 11)) * .65 + Noise(uv * float2(9, 24)) * .35;
                float haze = smoothstep(.51, .82, veil) * smoothstep(.02, .16, height) * (1 - smoothstep(.45, .8, height));
                sky += haze * float3(.025, .028, .027);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
