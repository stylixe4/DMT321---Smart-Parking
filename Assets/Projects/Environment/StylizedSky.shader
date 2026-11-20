// ท้องฟ้าแบบ Stylized (ไล่สี + ดวงอาทิตย์ + เมฆเคลื่อนไหว) สำหรับฉาก Low-poly
// ใช้เป็น Skybox Material ได้ทั้ง Built-in และ URP
Shader "SmartParking/Stylized Sky"
{
    Properties
    {
        [Header(Sky Gradient)]
        _TopColor ("Zenith Color", Color) = (0.22, 0.47, 0.86, 1)
        _MidColor ("Mid Sky Color", Color) = (0.47, 0.69, 0.93, 1)
        _HorizonColor ("Horizon Color", Color) = (0.86, 0.91, 0.95, 1)
        _GroundColor ("Below Horizon Color", Color) = (0.62, 0.68, 0.70, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5, 8)) = 2.6

        [Header(Sun)]
        _SunDirection ("Sun Direction (world, points to sun)", Vector) = (0.35, 0.55, -0.75, 0)
        _SunColor ("Sun Color", Color) = (1, 0.92, 0.76, 1)
        _SunSize ("Sun Size", Range(0.005, 0.15)) = 0.035
        _SunGlow ("Sun Glow", Range(0, 2)) = 0.55

        [Header(Clouds)]
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.74, 0.80, 0.90, 1)
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.5
        _CloudSoftness ("Cloud Softness", Range(0.01, 0.5)) = 0.22
        _CloudScale ("Cloud Scale", Float) = 1.8
        _CloudSpeed ("Cloud Speed", Float) = 0.006
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.92
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _TopColor, _MidColor, _HorizonColor, _GroundColor;
            float _HorizonSharpness;
            float4 _SunDirection;
            fixed4 _SunColor;
            float _SunSize, _SunGlow;
            fixed4 _CloudColor, _CloudShadowColor;
            float _CloudCoverage, _CloudSoftness, _CloudScale, _CloudSpeed, _CloudOpacity;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise (float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm (float2 p)
            {
                float value = 0.0;
                float amp = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    value += amp * valueNoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    amp *= 0.5;
                }
                return value;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                // ไล่สีท้องฟ้า: ขอบฟ้า -> กลางฟ้า -> บนสุด
                float up = saturate(y);
                float t = 1.0 - pow(1.0 - up, _HorizonSharpness);
                float3 sky = lerp(_HorizonColor.rgb, _MidColor.rgb, saturate(t * 1.6));
                sky = lerp(sky, _TopColor.rgb, saturate((t - 0.45) / 0.55));

                // ใต้เส้นขอบฟ้า
                float below = saturate(-y * 6.0);
                sky = lerp(sky, _GroundColor.rgb, below);

                // ดวงอาทิตย์ + แสงฟุ้ง
                float3 sunDir = normalize(_SunDirection.xyz);
                float sunDot = saturate(dot(d, sunDir));
                float sunDist = distance(d, sunDir);
                float disk = 1.0 - smoothstep(_SunSize * 0.85, _SunSize, sunDist);
                float glow = (pow(sunDot, 48.0) * 0.8 + pow(sunDot, 6.0) * 0.25) * _SunGlow;

                // เมฆ (ฉายลงระนาบเหนือศีรษะ แล้วจางลงใกล้ขอบฟ้า)
                float cloud = 0.0;
                float shade = 1.0;
                if (y > 0.0)
                {
                    float2 uv = d.xz / (y + 0.12) * _CloudScale;
                    uv += _Time.y * _CloudSpeed * float2(1.0, 0.35);
                    float n = fbm(uv);
                    float edge = 1.0 - _CloudCoverage;
                    cloud = smoothstep(edge, edge + _CloudSoftness, n);
                    cloud *= smoothstep(0.0, 0.18, y) * _CloudOpacity;
                    shade = saturate((n - edge) / max(0.001, 1.0 - edge) * 1.6);
                }

                float3 cloudCol = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, shade);
                cloudCol += _SunColor.rgb * pow(sunDot, 12.0) * 0.35; // ขอบเมฆโดนแดด

                float3 col = sky + _SunColor.rgb * glow;
                col = lerp(col, cloudCol, cloud);
                col += _SunColor.rgb * disk * (1.0 - cloud * 0.85) * 1.5;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
