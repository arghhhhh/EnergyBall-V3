// Used by ParticleAlphaGradingFeature on the Particles Camera.
//
// URP Alpha Processing applies color grading in proportion to a pixel's alpha. Additive
// particles add color but write almost no alpha, so grading (saturation, exposure, white
// balance...) barely reached them, and never reached bloom / lens flare glow (which lands on
// alpha 0 pixels). Pass 0 runs before post-processing and makes the whole layer opaque, so the
// grade applies everywhere (empty pixels are black and stay black). Pass 1 runs after
// post-processing and puts the original alpha back, so the layer still composites additively
// over the feed.
Shader "Hidden/EnergyBall/ParticleAlphaGrading"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_SavedColor);
        ENDHLSL

        Pass
        {
            Name "Opaque"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).rgb;
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "RestoreAlpha"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 graded = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).rgb;
                half alpha = SAMPLE_TEXTURE2D_X(_SavedColor, sampler_LinearClamp, input.texcoord).a;
                return half4(graded, alpha);
            }
            ENDHLSL
        }
    }
}
