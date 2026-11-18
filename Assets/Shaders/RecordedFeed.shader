// Camera feed for recorded clips: draws the video, keeping people at full brightness and
// dimming / desaturating / tinting everything else, using the clip's per-frame person mask.
//
// Same UV layout as the Kinect feed quad: v = 0 is the image's top row, for both the video
// (_MainTex, flipped on upload) and the mask (_MaskTex, rows top to bottom).
Shader "EnergyBall/RecordedFeed"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
        _MaskTex ("Person Mask (R8, 1 = person)", 2D) = "white" {}
        _BackgroundBrightness ("Background Brightness", Range(0, 1)) = 0.35
        _BackgroundSaturation ("Background Saturation", Range(0, 1)) = 0.5
        _BackgroundTint ("Background Tint", Color) = (1, 1, 1, 1)
        _Feather ("Mask Feather (mask texels)", Range(0, 8)) = 3
        _MaskGrow ("Mask Grow", Range(-0.5, 0.5)) = -0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MaskTex_TexelSize;
                float _BackgroundBrightness;
                float _BackgroundSaturation;
                float4 _BackgroundTint;
                float _Feather;
                float _MaskGrow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Bilinear 3x3 average of the mask: soft edges instead of blocky mask texels.
            float PersonMask(float2 uv)
            {
                float2 step = _MaskTex_TexelSize.xy * _Feather;
                float sum = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        sum += SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv + float2(x, y) * step).r;
                    }
                }
                float average = sum / 9.0;
                // The edge sits where the averaged mask crosses 0.5 - Grow: Grow > 0 widens the
                // lit area, < 0 pulls it inside the mask (how far scales with Feather). The ramp
                // always reaches 0 and 1, so the person's interior stays fully lit.
                float center = clamp(0.5 - _MaskGrow, 0.05, 0.95);
                float halfWidth = min(center, 1.0 - center);
                return smoothstep(center - halfWidth, center + halfWidth, average);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                float person = PersonMask(input.uv);

                half luma = dot(color, half3(0.2126, 0.7152, 0.0722));
                half3 background = lerp(luma.xxx, color, _BackgroundSaturation);
                background *= _BackgroundBrightness * _BackgroundTint.rgb;

                // Alpha 1: URP Alpha Processing masks post-processing by alpha.
                return half4(lerp(background, color, person), 1);
            }
            ENDHLSL
        }
    }
}
