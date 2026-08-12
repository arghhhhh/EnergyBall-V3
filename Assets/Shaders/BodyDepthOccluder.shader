// Depth-only occluder for Kinect-tracked bodies.
//
// Renders a 512x424 grid mesh (one vertex per depth pixel) so that each
// body pixel lands exactly where that person appears in the displayed
// color-feed quad, at the pixel's real sensor depth. Writes depth only
// (ColorMask 0): particles behind a body get z-rejected and the base-pass
// video (the person) shows through.
//
// Vertex placement: depth pixel -> color-image pixel (DepthToColor LUT)
// -> point on the feed quad surface -> scaled along the camera ray to
// view-space z = sensorDepthMeters * bodyScale. Silhouette therefore
// matches the video by construction, independent of camera FOV.
Shader "EnergyBall/BodyDepthOccluder"
{
    Properties
    {
        _DepthTex ("Kinect Depth (R16)", 2D) = "black" {}
        _BodyIndexTex ("Kinect Body Index (R8)", 2D) = "white" {}
        _DepthToColorTex ("Depth To Color LUT (RGFloat)", 2D) = "black" {}
        _ColorDims ("Color Frame Dimensions", Vector) = (1920, 1080, 0, 0)
        _BodyScale ("Body Scale", Float) = 1.0
        _DepthBias ("Depth Bias (m)", Float) = 0.0
        _MinDepth ("Min Valid Depth (m)", Float) = 0.4
        _BodiesOnly ("Occlude Tracked Bodies Only", Float) = 0
        // 0 = invisible (depth-only occluder), 15 = RGBA visible (point cloud debug view)
        [HideInInspector] _ColorMask ("Color Mask", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            ColorMask [_ColorMask]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DepthTex);
            TEXTURE2D(_BodyIndexTex);
            TEXTURE2D(_DepthToColorTex);
            SAMPLER(sampler_DepthTex);
            SAMPLER(sampler_BodyIndexTex);
            SAMPLER(sampler_DepthToColorTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorDims;
                float _BodyScale;
                float _DepthBias;
                float _MinDepth;
                float _BodiesOnly;
            CBUFFER_END

            // Feed-quad localToWorld, set per-frame from C#.
            float4x4 _QuadLocalToWorld;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0; // depth-image UV of this pixel
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // x = sensor depth in meters, y = 1 if a tracked body pixel.
                // Only visible in the point cloud debug view (_ColorMask 15).
                float2 debugData : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float4 uv4 = float4(input.uv, 0, 0);
                float bodyIndex =
                    SAMPLE_TEXTURE2D_LOD(_BodyIndexTex, sampler_BodyIndexTex, input.uv, 0).r;
                // R16 depth is millimeters / 65535; convert to meters.
                float depthMeters =
                    SAMPLE_TEXTURE2D_LOD(_DepthTex, sampler_DepthTex, input.uv, 0).r * 65.535;
                float2 colorPx =
                    SAMPLE_TEXTURE2D_LOD(_DepthToColorTex, sampler_DepthToColorTex, input.uv, 0).rg;

                // Body index 0-5 = tracked player, 255 = background (samples as 1.0).
                // With _BodiesOnly off, any valid depth pixel occludes (chairs,
                // walls, floor — anything the depth camera sees).
                bool isBody = (_BodiesOnly < 0.5) || (bodyIndex < 0.5);
                bool validDepth = depthMeters > _MinDepth;
                bool validMap = all(isfinite(colorPx))
                    && colorPx.x >= 0 && colorPx.x < _ColorDims.x
                    && colorPx.y >= 0 && colorPx.y < _ColorDims.y;

                if (!(isBody && validDepth && validMap))
                {
                    // NaN position: GPU culls any triangle touching this vertex.
                    output.positionCS = asfloat(0x7fc00000).xxxx;
                    output.debugData = float2(0, 0);
                    return output;
                }

                output.debugData = float2(depthMeters, bodyIndex < 0.5 ? 1.0 : 0.0);

                // Color pixel -> point on the displayed feed quad (quad mesh spans
                // ±0.5 in local XY with uv (0,0) at the (-0.5,-0.5) corner; the
                // color texture's first row — the image top — sits at v = 0).
                float2 quadUV = colorPx / _ColorDims.xy;
                float3 quadWS = mul(
                    _QuadLocalToWorld,
                    float4(quadUV.x - 0.5, quadUV.y - 0.5, 0, 1)
                ).xyz;

                // Slide along the camera ray to the pixel's true depth.
                float3 posVS = mul(UNITY_MATRIX_V, float4(quadWS, 1)).xyz;
                float targetZ = -(depthMeters * _BodyScale + _DepthBias);
                posVS *= targetZ / posVS.z;

                output.positionCS = mul(UNITY_MATRIX_P, float4(posVS, 1));
                return output;
            }

            // Point cloud debug view (only reaches the screen when _ColorMask is
            // 15): depth gradient, tracked-body pixels warm, environment cool.
            half4 frag(Varyings input) : SV_Target
            {
                float t = saturate((input.debugData.x - 1.0) / 4.0);
                bool isBodyPixel = input.debugData.y > 0.5;
                half3 near = isBodyPixel ? half3(1.0, 0.6, 0.1) : half3(0.1, 0.9, 0.9);
                half3 far = isBodyPixel ? half3(0.55, 0.05, 0.3) : half3(0.05, 0.15, 0.5);
                return half4(lerp(near, far, t), 1);
            }
            ENDHLSL
        }
    }
}
