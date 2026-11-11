// Make-up on the TRACKED face (live AR Try-On), drawn so that it looks worn and not painted on.
//
// The earlier layer laid a flat, see-through colour over the face: the skin under it lost its texture and its light,
// which looked like a mask ("a ghost"). This one TINTS the camera picture instead: every pixel of the skin keeps its
// own light and dark (the lines of the lips, a highlight, a shadow) and only its colour moves towards the shade.
//
// How: the picture already on the screen is multiplied by  shade / skin  ("Skin Under It" is the colour the bare skin
// is assumed to have), so skin of that colour becomes exactly the shade, and lighter or darker skin becomes a lighter
// or darker version of it. The mask picture (its alpha) says where, and the shade's alpha says how strongly.
// Blend DstColor SrcColor gives  screen x 2 x output, so an output of 0.5 changes nothing.
Shader "GlowUp/FaceMakeupBlend"
{
    Properties
    {
        _BaseMap ("Mask (alpha = where the colour shows)", 2D) = "white" {}
        _BaseColor ("Shade (alpha = strength)", Color) = (1, 1, 1, 0)
        _SkinBase ("Skin Under It", Color) = (0.74, 0.52, 0.47, 1)
        _Feather ("Soft Edge", Range(0, 1)) = 0.6
        _MouthMask ("Inside of the mouth (set by ArMouthMask)", 2D) = "black" {}
        _MouthOpen ("How open the mouth is (set by ArMouthMask)", Range(0, 1)) = 0
        _Pigment ("Pigment: share of the shade itself over the tint (set by ArFaceMakeup)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend DstColor SrcColor, Zero One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            fixed4 _SkinBase;
            sampler2D _MouthMask;
            float4 _MouthMask_ST;
            float _MouthOpen;
            float _Feather;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 mouthUv : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _BaseMap);
                o.mouthUv = TRANSFORM_TEX(v.texcoord, _MouthMask);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float mask = tex2D(_BaseMap, i.uv).a;
                // a softer edge: the colour thins out towards the rim of the mask, as real make-up does
                mask = lerp(mask, mask * mask * (3.0 - 2.0 * mask), _Feather);
                float amount = saturate(mask * _BaseColor.a);
                // the inside of an open mouth (teeth, tongue) is not painted: the picture shows through
                amount *= 1.0 - saturate(tex2D(_MouthMask, i.mouthUv).r * _MouthOpen);
                float3 tint = saturate(0.5 * _BaseColor.rgb / max(_SkinBase.rgb, 0.08));
                return fixed4(lerp(float3(0.5, 0.5, 0.5), tint, amount), 1);
            }
            ENDCG
        }

        // PIGMENT (phase 3A, 11 November 2026): a thin see-through layer of the shade itself over the tint. The tint can at most double the
        // skin's colour, so a shade much lighter than the skin (a nude lipstick on deep lips) stayed dark; this carries the rest. URP draws an
        // untagged pass and a "UniversalForward" pass of the same shader one after the other, so this runs right after the tint.
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            sampler2D _MouthMask;
            float4 _MouthMask_ST;
            float _MouthOpen;
            float _Feather;
            float _Pigment;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 mouthUv : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _BaseMap);
                o.mouthUv = TRANSFORM_TEX(v.texcoord, _MouthMask);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float mask = tex2D(_BaseMap, i.uv).a;
                mask = lerp(mask, mask * mask * (3.0 - 2.0 * mask), _Feather);
                float amount = saturate(mask * _BaseColor.a * _Pigment);
                amount *= 1.0 - saturate(tex2D(_MouthMask, i.mouthUv).r * _MouthOpen);
                return fixed4(_BaseColor.rgb, amount);
            }
            ENDCG
        }
    }
    Fallback Off
}
