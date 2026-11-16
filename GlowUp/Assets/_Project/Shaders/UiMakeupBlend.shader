// The same "worn, not painted" blend as GlowUp/FaceMakeupBlend, for the shapes of the GUIDED preview (UI Images over
// the camera picture, used on phones without live face tracking). The Image's sprite (its alpha) is the shape, the
// Image's colour is the shade and its alpha the strength.
Shader "GlowUp/UiMakeupBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SkinBase ("Skin Under It", Color) = (0.74, 0.52, 0.47, 1)
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
        Blend DstColor SrcColor, Zero One
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
            fixed4 _Color;
            fixed4 _SkinBase;


            // The tint may not LIGHTEN the skin by more than a limited gain (a deep shade may still darken it): a light shade on darker skin used to double
            // the picture's brightness and turn the face grey-white ("a ghost"). The hue of the shade is kept, only the light is capped.
            float3 TintFor(float3 shade, float3 skin)
            {
                float3 ratio = shade / max(skin, 0.08);
                float lum = max(dot(ratio, float3(0.2126, 0.7152, 0.0722)), 0.0001);
                float capped = min(lum, 1.3);
                return saturate(0.5 * ratio * (capped / lum));
            }
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float amount = saturate(tex2D(_MainTex, i.uv).a * i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                amount *= UnityGet2DClipping(i.world.xy, _ClipRect);       // the before / after split cuts the shapes
                #endif
                float3 tint = TintFor(i.color.rgb, _SkinBase.rgb);
                return fixed4(lerp(float3(0.5, 0.5, 0.5), tint, amount), 1);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
