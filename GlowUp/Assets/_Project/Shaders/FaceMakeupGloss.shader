// The highlight of a finish on the TRACKED face (live AR Try-On, full pipeline), drawn so that it moves with the face.
//
// The older highlight was a fixed picture: a white spot that stayed in the same place on the lips whatever way the head
// turned. This one works out the highlight from the face mesh itself: for every pixel, the surface direction (the mesh's
// normal) is compared with a soft light just above the phone's screen, as a glossy surface reflects it (Blinn-Phong).
// So a glossy lip shows a sharp highlight that slides over the lip as the head turns; satin a wide, soft one; matte none.
// Shimmer adds small sparkles that twinkle. The mask picture (its alpha) says where the make-up is (the part being tried);
// the inside of an open mouth gets no highlight. Added on top of the picture (Blend One One).
// The C# copy of this math is ArColorMath.Gloss (tested): keep the two equal.
Shader "GlowUp/FaceMakeupGloss"
{
    Properties
    {
        _BaseMap ("Mask (alpha = where the make-up is)", 2D) = "white" {}
        _BaseColor ("Highlight colour (alpha = strength)", Color) = (1, 1, 1, 0)
        _Sharpness ("Sharpness (high = glossy, low = satin)", Range(1, 256)) = 40
        _Sparkle ("Sparkle (shimmer)", Range(0, 1)) = 0
        _SparkleScale ("Sparkle size (cells across the face)", Range(20, 600)) = 220
        _SparkleSpeed ("Sparkle twinkle speed", Range(0, 20)) = 6
        _LightOffset ("Light position (added to the view direction)", Vector) = (0.15, 0.45, 0, 0)
        _MouthMask ("Inside of the mouth (set by ArMouthMask)", 2D) = "black" {}
        _MouthOpen ("How open the mouth is (set by ArMouthMask)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One, Zero One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            float _Sharpness;
            float _Sparkle;
            float _SparkleScale;
            float _SparkleSpeed;
            float4 _LightOffset;
            sampler2D _MouthMask;
            float4 _MouthMask_ST;
            float _MouthOpen;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 mouthUv : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _BaseMap);
                o.mouthUv = TRANSFORM_TEX(v.texcoord, _MouthMask);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float mask = tex2D(_BaseMap, i.uv).a;
                mask *= 1.0 - saturate(tex2D(_MouthMask, i.mouthUv).r * _MouthOpen);

                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float len = length(i.worldNormal);
                // a phone that gives no normals: the surface is taken as facing the camera (the highlight then follows the mask only)
                float3 N = len > 0.1 ? i.worldNormal / len : V;
                N = dot(N, V) < 0 ? -N : N;              // the front camera's mirrored picture turns faces inside out (Cull Off)
                float3 L = normalize(V + _LightOffset.xyz);
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _Sharpness);

                // shimmer: a few cells of the face sparkle in turn, a little more where the light catches the surface
                float2 cell = floor(i.uv * _SparkleScale);
                float rnd = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                float twinkle = pow(saturate(sin(_Time.y * _SparkleSpeed + rnd * 62.83)), 12.0);
                float sparkle = step(0.9, rnd) * twinkle * (0.35 + 0.65 * saturate(dot(N, V)));

                float amount = saturate((spec + _Sparkle * sparkle) * mask * _BaseColor.a);
                return fixed4(_BaseColor.rgb * amount, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
