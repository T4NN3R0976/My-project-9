// ObbyKit — glowing laser beam for URP (additive). Put it on a cylinder: the side facing
// the camera is brightest, edges fade out, and energy pulses flow along the beam.
// Use a bright HDR colour so Bloom makes it glow.
Shader "ObbyKit/Laser"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (4, 0.35, 0.45, 1)
        _CoreSharpness ("Edge Falloff", Range(0.2, 8)) = 2.5
        _PulseSpeed ("Pulse Speed", Float) = 6
        _FlowSpeed ("Energy Flow Speed", Float) = 9
        _FlowScale ("Energy Flow Scale", Float) = 14
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _CoreSharpness;
                float _PulseSpeed;
                float _FlowSpeed;
                float _FlowScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                float core = pow(saturate(abs(dot(n, v))), _CoreSharpness);
                float flow = 0.75 + 0.25 * sin(i.uv.y * _FlowScale - _Time.y * _FlowSpeed);
                float pulse = 0.85 + 0.15 * sin(_Time.y * _PulseSpeed);
                half3 col = _Color.rgb * core * flow * pulse;
                col = MixFogColor(col, half3(0, 0, 0), i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
