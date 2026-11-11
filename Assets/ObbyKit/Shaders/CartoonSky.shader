// ObbyKit — cartoon skybox for URP: bright gradient sky, crisp sun disc with glow,
// and soft-edged puffy clouds that drift slowly. Follows the scene's main (sun) light.
// Use it as the Skybox material in Window ▸ Rendering ▸ Lighting ▸ Environment.
Shader "ObbyKit/CartoonSky"
{
    Properties
    {
        _TopColor ("Sky Top", Color) = (0.20, 0.52, 0.96, 1)
        _HorizonColor ("Horizon", Color) = (0.68, 0.89, 1.0, 1)
        _BelowColor ("Below Horizon", Color) = (0.42, 0.76, 0.92, 1)
        _GradientPower ("Gradient Curve", Range(0.2, 3)) = 0.55
        _SunColor ("Sun", Color) = (1.0, 0.95, 0.72, 1)
        _SunSize ("Sun Size", Range(0.002, 0.1)) = 0.012
        _SunGlow ("Sun Glow", Range(0, 2)) = 0.6
        _CloudColor ("Cloud Light", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud Shade", Color) = (0.74, 0.84, 0.97, 1)
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.42
        _CloudScale ("Cloud Scale", Float) = 1.6
        _CloudSpeed ("Cloud Speed", Float) = 0.006
        _CloudSoftness ("Cloud Edge Softness", Range(0.001, 0.2)) = 0.03
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BelowColor;
                half4 _SunColor;
                half4 _CloudColor;
                half4 _CloudShade;
                float _GradientPower;
                float _SunSize;
                float _SunGlow;
                float _CloudCover;
                float _CloudScale;
                float _CloudSpeed;
                float _CloudSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
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
                float v = 0.0;
                float amp = 0.55;
                for (int k = 0; k < 4; k++)
                {
                    v += ValueNoise(p) * amp;
                    p = p * 2.02 + 11.3;
                    amp *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                half3 col = (y >= 0.0)
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(y), _GradientPower))
                    : lerp(_HorizonColor.rgb, _BelowColor.rgb, saturate(-y * 6.0));

                float3 sunDir = normalize(_MainLightPosition.xyz);
                float sd = dot(d, sunDir);
                col += _SunColor.rgb * (pow(saturate(sd), 48.0) * _SunGlow + pow(saturate(sd), 6.0) * _SunGlow * 0.15);

                float cloudMask = 0.0;
                if (y > 0.0)
                {
                    // Project onto a flat cloud "ceiling" so clouds shrink toward the horizon.
                    float2 uv = d.xz / (y + 0.18) * _CloudScale;
                    float2 drift = float2(_Time.y * _CloudSpeed, _Time.y * _CloudSpeed * 0.4);
                    float n = Fbm(uv + drift);
                    float cover = 1.0 - _CloudCover;
                    cloudMask = smoothstep(cover, cover + _CloudSoftness, n);

                    // Darker on the side facing away from the sun.
                    float2 toSun = normalize(sunDir.xz + 1e-4) * 0.08;
                    float n2 = Fbm(uv + drift + toSun);
                    float shade = smoothstep(0.0, 0.06, n2 - n);
                    half3 cloud = lerp(_CloudColor.rgb, _CloudShade.rgb, shade);
                    cloud += _SunColor.rgb * pow(saturate(sd), 12.0) * 0.25;

                    cloudMask *= smoothstep(0.0, 0.12, y);    // thin out toward the horizon
                    col = lerp(col, cloud, cloudMask);
                }

                // Crisp cartoon sun disc, hidden behind clouds.
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.75, sd);
                col = lerp(col, _SunColor.rgb * 1.6, disc * (1.0 - cloudMask));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
