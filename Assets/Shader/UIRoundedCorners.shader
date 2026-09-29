Shader "CustomRenderTexture/UIRoundedCorners"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Radius ("Corner Radius", Range(0, 0.5)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Radius;

            v2f vert(appdata_t v)
            {
                v2f o;

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;

                return o;
            }

            float roundedBoxSDF(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;

                return min(max(q.x, q.y), 0.0)
                       + length(max(q, 0.0))
                       - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv) * i.color;

                // UV centrado en 0
                float2 p = i.uv - 0.5;

                // Tamaño del rectángulo
                float2 bounds = float2(0.5, 0.5);

                // Radio de las esquinas
                float radius = _Radius;

                float distance = roundedBoxSDF(p, bounds, radius);

                // Suavizado de los bordes
                float alpha = 1.0 - smoothstep(0.0, 0.01, distance);

                color.a *= alpha;

                return color;
            }

            ENDCG
        }
    }
}