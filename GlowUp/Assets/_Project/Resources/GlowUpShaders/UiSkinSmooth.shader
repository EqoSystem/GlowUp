// SKIN SMOOTHING for foundation, concealer and powder in the guided try-on (18 November 2026). The camera picture is read under the face
// mask and softened with an EDGE-KEEPING blur (a bilateral filter, 12 taps): blemishes, pores and uneven colour fade, while lines that are
// real edges (the nose, the jaw, the hairline) stay sharp because pixels that differ a lot are weighed little. It is drawn UNDER the
// foundation's tint, so the tint then multiplies a smoother skin. The mask (its alpha) is the face without eyes, lips and brows.
// The camera picture's coordinates arrive in the SECOND texture coordinate (ArGuidedFaceLayer.SetCameraUvs).
// In Resources so Shader.Find works in a build.
Shader "GlowUp/UiSkinSmooth"
{
    Properties
    {
        [PerRendererData] _MainTex ("Mask", 2D) = "white" {}
        _CamTex ("Camera picture", 2D) = "gray" {}
        _Color ("Strength (alpha)", Color) = (1, 1, 1, 1)
        _RadiusPx ("Blur radius in camera pixels", Range(1, 16)) = 6
        _Edge ("Edge keeping (smaller keeps more edge)", Range(0.02, 0.4)) = 0.05
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            float4 _ClipRect;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _CamTex;
            float4 _CamTex_TexelSize;
            fixed4 _Color;
            float _RadiusPx;
            float _Edge;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; float2 cam : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; float2 cam : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.cam = v.cam;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float amount = saturate(tex2D(_MainTex, i.uv).a * i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                amount *= UnityGet2DClipping(i.world.xy, _ClipRect);       // the before / after split cuts the layer
                #endif
                float3 centre = tex2D(_CamTex, i.cam).rgb;
                float3 sum = centre;
                float weight = 1.0;
                float2 texel = _CamTex_TexelSize.xy * _RadiusPx;
                // two rings of taps around the pixel (6 near, 6 far); a tap that is much lighter or darker than the centre counts little
                const float2 ring[12] = {
                    float2(1.0, 0.0), float2(0.5, 0.866), float2(-0.5, 0.866), float2(-1.0, 0.0), float2(-0.5, -0.866), float2(0.5, -0.866),
                    float2(1.7, 0.0), float2(0.85, 1.47), float2(-0.85, 1.47), float2(-1.7, 0.0), float2(-0.85, -1.47), float2(0.85, -1.47) };
                [unroll] for (int k = 0; k < 12; k++)
                {
                    float3 s = tex2D(_CamTex, i.cam + ring[k] * texel * 0.6).rgb;
                    float3 d = s - centre;
                    float w = exp(-dot(d, d) / (_Edge * _Edge));
                    sum += s * w; weight += w;
                }
                return fixed4(sum / weight, amount);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
