// ObbyKit — cartoon foam that hugs anything standing in the ocean (pillars, springs, posts).
// Put it on a flat Quad centred on the object at water level. Scale the quad to the
// object's footprint + 2 × Foam Width. The shape is worked out from the quad's scale:
// Roundness 0 = rounded box (pillars), 1 = circle (posts, springs).
Shader "ObbyKit/FoamRing"
{
    Properties
    {
        _FoamColor ("Foam Colour", Color) = (1, 1, 1, 0.95)
        _Width ("Foam Width (m)", Float) = 2.2
        _Roundness ("Roundness (0 box, 1 circle)", Range(0, 1)) = 0.15
        _RingSpacing ("Ring Spacing (m)", Float) = 0.9
        _Speed ("Ring Speed", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                float _Width;
                float _Roundness;
                float _RingSpacing;
                float _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;      // metres from the centre, in the quad's plane
                float2 halfFoot : TEXCOORD1;   // half-size of the object's footprint
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float4x4 m = GetObjectToWorldMatrix();
                float2 size = float2(length(m._m00_m10_m20), length(m._m01_m11_m21));
                o.local = v.positionOS.xy * size;
                o.halfFoot = max(size * 0.5 - _Width, 0.05);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 h = i.halfFoot;
                float corner = min(h.x, h.y) * _Roundness;
                float2 q = abs(i.local) - (h - corner);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;   // distance outside the footprint
                clip(d + 0.05);

                float t = _Time.y * _Speed;
                float wobble = (ValueNoise(i.local * 0.9 + t * 0.7) - 0.5) * 0.35;
                float rings = frac((d + wobble) / max(_RingSpacing, 0.05) - t);
                float band = smoothstep(0.0, 0.1, rings) * (1.0 - smoothstep(0.3, 0.42, rings));
                float hug = 1.0 - smoothstep(0.15, 0.55, d + wobble * 0.5);            // solid foam right at the object
                float fade = 1.0 - saturate(d / max(_Width, 0.1));
                float a = saturate(max(hug, band * 0.85) * fade) * _FoamColor.a;
                half3 col = MixFog(_FoamColor.rgb, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
