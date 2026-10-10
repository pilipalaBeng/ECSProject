Shader "HanziRogue/GlyphFlat"
{
    // 单实例字形渲染（英雄；将来的一次性单位 / 拾取物也走这条）。
    //
    // 与 GlyphInstanced 的分工，以及为什么不能混用：
    //
    //   GlyphInstanced 是为 DrawMeshInstancedIndirect 写的。它的位置与闪白强度来自
    //   StructuredBuffer，顶点阶段**从头到尾不读 UNITY_MATRIX_M**。这套写法对
    //   「挂在 GameObject 上的普通 MeshRenderer」是错的：那种画法下 SV_InstanceID 恒为 0，
    //   而 _GlyphInstances 只被 InstancedEnemyRenderer 绑在**敌人材质**上，
    //   英雄材质上从没绑过 → 读到全零 → 字块被画死在世界原点，尺寸恒为 _GlyphSize。
    //
    //   2026-10-09 实测后果：英雄字块钉在原点、1.1 米（和「兵」一样大），
    //   而武器材质是 Unlit/Color（读 Transform），老老实实跟着真身跑到 7 米开外。
    //   玩家看到的是「英雄不动、武器自己走自己的」——根因却只有一个。
    //
    // 本 shader 就是那个答案：走正常的 UnityObjectToClipPos，老老实实读 Transform。
    // 属性表刻意与 GlyphInstanced 对齐（多一个 _Flash，少一个 _GlyphSize），
    // 两种字形的材质在 Inspector 上长得一样，表现层换着色器不用改 C# 代码。
    Properties
    {
        _MainTex ("Glyph Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Tint", Color) = (1,1,1,1)
        _Flash ("Flash Amount", Range(0,1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }

        // 俯视相机看的是 quad 背面，不关剔除整个字形会消失（GlyphInstanced 同款坑）
        Cull Off
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _FlashColor;
            fixed _Flash;
            fixed _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                // 与 GlyphInstanced 的实质差别只有这一行：这里认 Transform
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // 两条通道分开：_Color 是底色（常态 / 阵亡），_Flash 只叠受击那一层。
            // 合成一个颜色让 C# 去算，会让「正在闪红的时候阵亡」这种叠加态没有确定结果，
            // 而这种时刻在快死的时候恰好最常见。
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * lerp(_Color, _FlashColor, saturate(_Flash));
                clip(c.a - _Cutoff);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
