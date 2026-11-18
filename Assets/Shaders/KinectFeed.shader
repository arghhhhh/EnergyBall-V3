// The Kinect backdrop on the camera feed quad (driven by KinectFeedDisplay).
//
// _FEED_COLOR           the color camera. The frame arrives as raw YUY2 (each RGBA32 texel holds
//                       two pixels: Y0, U, Y1, V) and is decoded here, so the CPU never converts
//                       it. Filtered by hand: bilinear over the decoded pixels.
// _FEED_INFRARED        the depth camera's infrared image, brightness = (ir * gain) ^ gamma.
// _FEED_INFRARED_COLOR  infrared brightness with the color camera's hue: each infrared pixel
//                       looks up its color pixel through the depth-to-color map (computed per
//                       depth frame) and takes that pixel's chroma. Pixels without depth (no
//                       map entry) stay gray; pixels outside the color camera's view take the
//                       hue of the nearest color pixel on the frame's edge.
//
// Quad UVs: (0,0) at the (-0.5,-0.5) corner, and every feed texture's first row (the image top)
// sits at v = 0, which the quad's rotation puts at the top of the screen.
Shader "EnergyBall/KinectFeed"
{
    Properties
    {
        [NoScaleOffset] _ColorTex ("Color (YUY2 packed in RGBA32)", 2D) = "black" {}
        _ColorDims ("Color Frame Size (pixels)", Vector) = (1920, 1080, 0, 0)
        [NoScaleOffset] _InfraredTex ("Infrared (R16)", 2D) = "black" {}
        [NoScaleOffset] _DepthToColorTex ("Depth To Color Map (RGFloat)", 2D) = "black" {}
        _DepthDims ("Depth Frame Size (pixels)", Vector) = (512, 424, 0, 0)
        _InfraredBrightness ("Infrared Brightness", Float) = 4
        _InfraredGamma ("Infrared Gamma", Float) = 0.75
        _ColorAmount ("Color Amount", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _FEED_COLOR _FEED_INFRARED _FEED_INFRARED_COLOR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_ColorTex);
            TEXTURE2D(_InfraredTex);
            SAMPLER(sampler_InfraredTex);
            TEXTURE2D(_DepthToColorTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorDims;
                float4 _DepthDims;
                float _InfraredBrightness;
                float _InfraredGamma;
                float _ColorAmount;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
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
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            // One color pixel as (Y, U, V), 0..1.
            float3 LoadYuv(int2 px)
            {
                px = clamp(px, int2(0, 0), int2(_ColorDims.xy) - 1);
                float4 texel = LOAD_TEXTURE2D(_ColorTex, int2(px.x >> 1, px.y));
                float y = (px.x & 1) ? texel.b : texel.r;
                return float3(y, texel.g, texel.a);
            }

            float3 SampleYuv(float2 uv)
            {
                float2 p = uv * _ColorDims.xy - 0.5;
                int2 i = (int2)floor(p);
                float2 f = p - i;
                float3 top = lerp(LoadYuv(i), LoadYuv(i + int2(1, 0)), f.x);
                float3 bottom = lerp(LoadYuv(i + int2(0, 1)), LoadYuv(i + int2(1, 1)), f.x);
                return lerp(top, bottom, f.y);
            }

            // Full-range BT.601 (JFIF): the Kinect color stream is JPEG-sourced. Gamma-encoded.
            float3 YuvToRgb(float3 yuv)
            {
                float y = yuv.x;
                float u = yuv.y - 0.5;
                float v = yuv.z - 0.5;
                return saturate(float3(
                    y + 1.402 * v,
                    y - 0.344136 * u - 0.714136 * v,
                    y + 1.772 * u));
            }

            // Infrared brightness, gamma-encoded (display value).
            float InfraredValue(float2 uv)
            {
                // R16 is the raw 16-bit intensity / 65535.
                float ir = SAMPLE_TEXTURE2D(_InfraredTex, sampler_InfraredTex, uv).r;
                return pow(saturate(ir * _InfraredBrightness), _InfraredGamma);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 display;

            #if defined(_FEED_COLOR)
                display = YuvToRgb(SampleYuv(input.uv));
            #else
                float value = InfraredValue(input.uv);
                display = value.xxx;

                #if defined(_FEED_INFRARED_COLOR)
                // The map has one entry per depth pixel: look up this pixel's own entry.
                int2 depthPx = clamp(int2(input.uv * _DepthDims.xy), int2(0, 0), int2(_DepthDims.xy) - 1);
                float2 colorPx = LOAD_TEXTURE2D(_DepthToColorTex, depthPx).rg;
                // Pixels without depth have no map entry (-Infinity) and stay gray. Pixels past
                // the color frame's edge (the infrared view is taller than the color one: strips
                // at the top and bottom in Fit) take the hue of the nearest edge pixel, so the
                // tint carries on instead of stopping at a hard line.
                if (all(isfinite(colorPx)))
                {
                    colorPx = clamp(colorPx, 0, _ColorDims.xy - 1);
                    float3 rgb = YuvToRgb(SampleYuv(colorPx / _ColorDims.xy));
                    float luma = dot(rgb, float3(0.299, 0.587, 0.114));
                    // Chroma relative to the pixel's own brightness, so dim color pixels still
                    // give their hue; the floor keeps near-black sensor noise from exploding.
                    float3 chroma = (rgb - luma) / max(luma, 0.08);
                    display = saturate(value * (1.0 + chroma * _ColorAmount));
                }
                #endif
            #endif

            #if !defined(UNITY_COLORSPACE_GAMMA)
                display = SRGBToLinear(display);
            #endif
                return half4(display, 1);
            }
            ENDHLSL
        }
    }
}
