// UI shader for compositing a premultiplied-alpha RenderTexture (the post-processed particle
// layer, see ParticleLayerCompositor) over the screen: result = src.rgb + dst.rgb * (1 - src.a).
// Unlike UI/Default (SrcAlpha, OneMinusSrcAlpha) this keeps bloom / lens-flare glow, which the
// particle pass writes with alpha 0, and doesn't darken semi-transparent particles twice.
Shader "EnergyBall/UIPremultipliedComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            ZTest [unity_GUIZTestMode]
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // The texture is already premultiplied; the RawImage color tints/fades it as a whole.
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
            }
            ENDHLSL
        }
    }
}
