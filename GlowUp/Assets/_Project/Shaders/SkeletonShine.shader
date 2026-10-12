// The look of every "skeleton" shape of the app (the bars that stand in for words while a screen fetches its data, the
// placeholder rows of a list, the shapes of the sheet between two screens): the same as on the admin website. A soft
// blush base, and a rose-to-gold shine that travels across the whole screen from left to right, so all the shapes on a
// screen light up together as it passes.
//
// The Image's sprite (its alpha) is the shape. The Image's colour is NOT used, only its alpha (so a fading CanvasGroup
// still fades the shape). The two colours come from the theme: the app sets them for all skeleton shapes at once
// (SkeletonLook). Until it has, for example in the Editor while the app is not running, the two colours typed on the
// material are used.
Shader "GlowUp/SkeletonShine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint (only its alpha is used)", Color) = (1, 1, 1, 1)
        _BaseColor ("Base colour (until the app sets the theme's)", Color) = (0.87, 0.745, 0.765, 1)
        _ShineColor ("Shine colour (until the app sets the theme's)", Color) = (0.875, 0.78, 0.69, 1)
        _Seconds ("Seconds for one pass of the shine", Float) = 1.5
        _Width ("Width of the shine (1 = as wide as the screen)", Range(0.05, 1.5)) = 0.6
        _Tilt ("Tilt of the shine", Range(-1, 1)) = 0.18
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
            fixed4 _Color;
            fixed4 _BaseColor;
            fixed4 _ShineColor;
            float _Seconds;
            float _Width;
            float _Tilt;

            // set by the app for every skeleton shape at once (SkeletonLook.cs)
            float _GlowUpSkOn;          // 1 = the values below are set
            float _GlowUpSkTime;        // seconds, not slowed down or paused with the game
            float _GlowUpSkStill;       // 1 = no movement (Performance Mode)
            fixed4 _GlowUpSkBase;
            fixed4 _GlowUpSkShine;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; float4 screen : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 place = i.screen.xy / max(i.screen.w, 0.0001);          // 0 to 1 across the screen
                float seconds = max(_Seconds, 0.05);
                float width = max(_Width, 0.05);
                float time = _GlowUpSkOn > 0.5 ? _GlowUpSkTime : _Time.y;

                // the middle of the shine goes from just outside the left edge to just outside the right one, gently at both ends
                float phase = frac(time / seconds);
                float eased = phase * phase * (3.0 - 2.0 * phase);
                float middle = lerp(-width, 1.0 + width, eased);
                float away = saturate(abs(place.x + (place.y - 0.5) * _Tilt - middle) / width);
                float shine = 1.0 - away * away * (3.0 - 2.0 * away);         // 1 in the middle of the shine, 0 outside it
                shine = lerp(shine, 0.3, saturate(_GlowUpSkStill));

                fixed4 baseColour = _GlowUpSkOn > 0.5 ? _GlowUpSkBase : _BaseColor;
                fixed4 shineColour = _GlowUpSkOn > 0.5 ? _GlowUpSkShine : _ShineColor;
                fixed4 colour = lerp(baseColour, shineColour, shine);
                colour.a = tex2D(_MainTex, i.uv).a * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                colour.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return colour;
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
