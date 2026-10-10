Shader "HanziRogue/GlyphInstanced"
{
    // 字潮渲染专用。CONSTRAINTS C4.1 禁止 TMP 逐字渲染，C4.2 要求走 GPU instancing。
    // 用 AlphaTest(cutout) 而不是半透明混合：字形之间无排序问题，且 ZWrite 可开。
    //
    // 这里是 indirect instancing（DrawMeshInstancedIndirect）而不是 DrawMeshInstanced：
    // 后者靠 Unity 内置的 per-instance 矩阵数组，拿不到自定义的 per-instance 数据，
    // 于是「被扎到就闪白」这种逐个体不同的表现做不出来（MaterialPropertyBlock 只能整批统一赋值）。
    // 换 indirect 之后每个实例自己带数据，闪白、将来的序列帧 UV 偏移、死亡缩放都能顺着这条通道走。
    Properties
    {
        _MainTex ("Glyph Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
        _GlyphSize ("Glyph Size", Float) = 1.1
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
            // vertex shader 里读 StructuredBuffer 需要 SM 4.5 以上，缺这一句会编不过
            #pragma target 4.5
            #include "UnityCG.cginc"

            struct GlyphInstance
            {
                float3 Position;   // 世界坐标（y 已含离地高度）
                float  Flash;      // 0 = 常态，1 = 全白。由受击计时线性衰减而来
            };

            StructuredBuffer<GlyphInstance> _GlyphInstances;

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _FlashColor;
            fixed _Cutoff;
            float _GlyphSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                // indirect 模式下实例序号由 GPU 给出来，用它去索引自己的那份数据
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float flash : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                GlyphInstance data = _GlyphInstances[v.instanceID];

                // quad 铺在 XZ 平面上，所以只缩放 x / z，y 保持 0 由实例数据给高度
                float3 world = data.Position
                             + float3(v.vertex.x * _GlyphSize, 0.0, v.vertex.z * _GlyphSize);

                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.flash = data.Flash;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * lerp(_Color, _FlashColor, i.flash);
                clip(c.a - _Cutoff);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
