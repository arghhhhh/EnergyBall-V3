// Vertex-colored unlit line shader for the debug skeleton.
// ZTest Always so skeleton lines are never culled by the body depth
// occluder (or anything else) — they're a diagnostic overlay and should
// always be visible on top.
Shader "EnergyBall/SkeletonLine"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "IgnoreProjector" = "True" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
}
