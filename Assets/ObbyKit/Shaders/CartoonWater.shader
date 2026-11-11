// ObbyKit — stylised animated ocean for URP.
// • Three travelling sine waves move the surface (fading out far from the camera).
// • Colour: deep in the troughs, bright turquoise on crests, sky-blue at grazing angles.
// • Flat "toon" sun shading, crisp sun sparkles, and wobbly cartoon foam lines that drift.
// • Receives sun shadows and Unity fog. No textures needed.
Shader "ObbyKit/CartoonWater"
{
    Properties
    {
        _ShallowColor ("Crest Colour", Color) = (0.30, 0.86, 0.93, 1)
        _DeepColor ("Trough Colour", Color) = (0.05, 0.47, 0.80, 1)
        _HorizonColor ("Grazing-angle Colour", Color) = (0.62, 0.90, 1.0, 1)
        _FoamColor ("Foam Colour", Color) = (1, 1, 1, 1)
        _WaveHeight ("Wave Height", Float) = 0.32
        _WaveLength ("Wave Length", Float) = 16
        _WaveSpeed ("Wave Speed", Float) = 1
        _WaveFadeDistance ("Wave Fade Distance", Float) = 350
        _FoamScale ("Foam Pattern Scale", Float) = 0.07
        _FoamSpeed ("Foam Drift Speed", Float) = 0.5
        _FoamAmount ("Foam Amount", Range(0, 1)) = 0.55
        _FoamLineWidth ("Foam Line Width (m)", Range(0.05, 2)) = 0.35
        _FresnelPower ("Fresnel Power", Float) = 4
        _SparkleStrength ("Sun Sparkle", Range(0, 2)) = 0.9
        _ShadeSteps ("Toon Light Steps", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _HorizonColor;
                half4 _FoamColor;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveFadeDistance;
                float _FoamScale;
                float _FoamSpeed;
                float _FoamAmount;
                float _FoamLineWidth;
                float _FresnelPower;
                float _SparkleStrength;
                float _ShadeSteps;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  crest      : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            // One travelling sine wave: adds its height and its slope (dh/dx, dh/dz).
            void AddWave(float2 xz, float2 dir, float wavelength, float amplitude, float t, inout float h, inout float2 slope)
            {
                float k = 6.2831853 / wavelength;
                float w = sqrt(9.81 * k) * _WaveSpeed;
                float phase = dot(dir, xz) * k - w * t;
                h += amplitude * sin(phase);
                slope += amplitude * k * cos(phase) * dir;
            }

            void Waves(float2 xz, float fade, out float h, out float3 n)
            {
                float t = _Time.y;
                h = 0;
                float2 slope = 0;
                float a = _WaveHeight * fade;
                float l = max(_WaveLength, 0.5);
                AddWave(xz, normalize(float2(1.0, 0.35)), l,        a,        t, h, slope);
                AddWave(xz, normalize(float2(-0.45, 1.0)), l * 0.63, a * 0.55, t, h, slope);
                AddWave(xz, normalize(float2(0.8, -0.7)),  l * 0.37, a * 0.30, t, h, slope);
                n = normalize(float3(-slope.x, 1.0, -slope.y));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float dist = length(ws.xz - _WorldSpaceCameraPos.xz);
                float fade = saturate(1.0 - dist / max(_WaveFadeDistance, 1.0));
                float h;
                float3 n;
                Waves(ws.xz, fade, h, n);
                ws.y += h;
                o.positionWS = ws;
                o.normalWS = n;
                o.crest = h / max(_WaveHeight * 1.85, 0.001);
                o.positionCS = TransformWorldToHClip(ws);
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
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                return ValueNoise(p) * 0.6 + ValueNoise(p * 2.03 + 17.1) * 0.3 + ValueNoise(p * 4.01 + 3.7) * 0.1;
            }

            // Two drifting noise layers averaged: the foam lines are contours of this.
            float FoamField(float2 p, float t)
            {
                float n1 = Fbm(p + t * _FoamSpeed * float2(0.13, 0.07));
                float n2 = Fbm(p * 1.37 + float2(5.2, 1.3) - t * _FoamSpeed * float2(0.05, 0.11));
                return (n1 + n2) * 0.5;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 camPos = GetCameraPositionWS();
                float3 v = normalize(camPos - i.positionWS);
                float dist = length(camPos - i.positionWS);
                float t = _Time.y;

                // Base colour: troughs deep, crests bright, sky-tinted at grazing angles.
                float crest = saturate(i.crest * 0.5 + 0.5);
                half3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, smoothstep(0.25, 0.9, crest));
                float fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                col = lerp(col, _HorizonColor.rgb, saturate(fresnel));

                // Toon sun lighting in a few flat steps.
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 tint = sun.color / max(max(sun.color.r, sun.color.g), max(sun.color.b, 0.001));
                float ndl = saturate(dot(n, sun.direction));
                float steps = max(_ShadeSteps, 1.0);
                float lit = floor(ndl * steps + 0.5) / steps;
                float shadow = lerp(0.6, 1.0, sun.shadowAttenuation);
                col *= lerp(0.8, 1.05, lit) * shadow;
                col *= lerp(half3(1, 1, 1), tint, 0.25);

                // Cartoon foam: wobbly contour lines of drifting noise, broken into patches.
                // Line width is measured in metres (distance to the contour ÷ slope), so lines
                // stay evenly thin instead of swelling into blobs where the noise is flat.
                float2 fuv = i.positionWS.xz * _FoamScale;
                const float e = 0.02;
                float f0 = FoamField(fuv, t);
                float2 grad = float2(FoamField(fuv + float2(e, 0), t) - f0, FoamField(fuv + float2(0, e), t) - f0) / e;
                float slope = max(length(grad), 0.25) * _FoamScale;   // field change per metre
                float distA = abs(f0 - 0.5) / slope;
                float distB = abs(f0 - 0.33) / slope;
                float aaA = fwidth(distA) + 1e-3;
                float aaB = fwidth(distB) + 1e-3;
                float lineA = 1.0 - smoothstep(_FoamLineWidth, _FoamLineWidth + aaA, distA);
                float lineB = 1.0 - smoothstep(_FoamLineWidth * 0.7, _FoamLineWidth * 0.7 + aaB, distB);
                float patches = smoothstep(1.0 - _FoamAmount, 1.0 - _FoamAmount + 0.15, Fbm(fuv * 0.35 + t * 0.02));
                float foam = saturate(max(lineA, lineB * 0.8) * patches);
                foam = max(foam, smoothstep(0.88, 0.98, crest) * 0.6);    // light white caps on the highest crests
                foam *= saturate(1.0 - dist / 450.0);                     // fade far away to avoid shimmer
                col = lerp(col, _FoamColor.rgb * lerp(0.85, 1.0, lit), foam);

                // Crisp toon sun sparkle.
                float3 hv = normalize(sun.direction + v);
                float spec = pow(saturate(dot(n, hv)), 900.0);
                col += tint * smoothstep(0.55, 0.65, spec) * _SparkleStrength * 0.8 * sun.shadowAttenuation;

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
