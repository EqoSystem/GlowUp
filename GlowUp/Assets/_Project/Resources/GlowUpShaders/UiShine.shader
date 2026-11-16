// The highlight of a finish on the GUIDED try-on that follows the face. ADDS light (it never paints a grey film over the skin)
// where the mask says light would catch: a wide soft sheen (satin), a tight bright spot (glossy), glitter (shimmer), a tinted
// sheen (metallic). Matte draws nothing. _Sharpness narrows the sheen, _Sparkle adds twinkling points.
// In Resources so Shader.Find works in a build.
Shader "GlowUp/UiShine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint (alpha = strength)", Color) = (1, 1, 1, 1)
        _Sharpness ("Sharpness", Range(0.3, 6)) = 1
        _Sparkle ("Sparkle", Range(0, 1)) = 0
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
        Blend SrcAlpha One
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
            float _Sharpness;
            float _Sparkle;

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

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                float mask = tex2D(_MainTex, i.uv).a;
                float light = pow(saturate(mask), _Sharpness);
                // small points that come and go; they only exist where the mask lets light through
                float2 cell = floor(i.uv * 110.0);
                float h = hash(cell);
                float twinkle = step(0.9, h) * (0.5 + 0.5 * sin(_Time.y * 6.0 + h * 60.0));
                light += _Sparkle * twinkle * saturate(mask * 2.0) * 1.5;
                #ifdef UNITY_UI_CLIP_RECT
                light *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return fixed4(i.color.rgb, saturate(light * i.color.a));
            }
            ENDCG
        }
    }
    Fallback Off
}
