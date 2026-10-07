Shader "HanziRogue/GlyphInstanced"
{
    // 字潮渲染专用。CONSTRAINTS C4.1 禁止 TMP 逐字渲染，C4.2 要求走 GPU instancing。
    // 用 AlphaTest(cutout) 而不是半透明混合：字形之间无排序问题，且 ZWrite 可开。
    Properties
    {
        _MainTex ("Glyph Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }

        // 俯视相机看的是 quad 背面，必须关剔除，否则整个字形会消失
        Cull Off
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;

            v2f vert (appdata v)
            {
                // 这一句让 UnityObjectToClipPos 取到 per-instance 矩阵，
                // 是 DrawMeshInstanced 生效的关键，漏了会所有实例叠在原点。
                UNITY_SETUP_INSTANCE_ID(v);

                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
