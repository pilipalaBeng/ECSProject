Shader "HanziRogue/GridFloor"
{
    // 战场地面：程序化网格，按世界坐标绘制。
    // 存在的唯一理由是「让玩家看出自己在动」——纯色地面上移动没有任何视差参照。
    // 网格画在世界空间而不是 UV 上，因此无论地面多大、怎么缩放，格子间距都是恒定的米数，
    // 相机跟着英雄走时格子从脚下流过，位移一目了然。
    Properties
    {
        _BaseColor ("地面底色", Color) = (0.085, 0.080, 0.095, 1)
        _FogColor ("远处淡出到（同相机背景色）", Color) = (0.055, 0.050, 0.055, 1)
        _LineColor ("细格线颜色", Color) = (0.20, 0.175, 0.140, 1)
        _MajorColor ("粗格线颜色", Color) = (0.32, 0.26, 0.17, 1)
        _CellSize ("格距（米）", Float) = 4
        _MajorEvery ("每几格一条粗线", Float) = 5
        _LineWidth ("细线宽（米）", Float) = 0.05
        _MajorWidth ("粗线宽（米）", Float) = 0.13
        _GrainAmount ("颗粒强度", Float) = 0.035
        _GrainScale ("颗粒密度（每米格数）", Float) = 6
        _FadeRadius ("边缘淡出半径（米）", Float) = 100
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            // 俯视看的是 quad 正面，但绕序取决于网格工厂，关掉剔除最稳（与字形 shader 同一坑）
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 clip : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            float4 _BaseColor;
            float4 _FogColor;
            float4 _LineColor;
            float4 _MajorColor;
            float _CellSize;
            float _MajorEvery;
            float _LineWidth;
            float _MajorWidth;
            float _GrainAmount;
            float _GrainScale;
            float _FadeRadius;

            v2f vert (appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.clip = mul(UNITY_MATRIX_VP, world);
                o.world = world.xz;
                return o;
            }

            // 到最近一条格线的距离（米），格线落在 coord 为整数的位置
            float2 DistanceToGrid(float2 w, float cell)
            {
                return abs(frac(w / cell + 0.5) - 0.5) * cell;
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 w = i.world;
                float2 fw = fwidth(w) + 1e-5;   // 每像素覆盖多少米，用它抗锯齿，远处才不糊成摩尔纹

                float2 dMinor = DistanceToGrid(w, _CellSize);
                float minor = 1.0 - smoothstep(0.0, _LineWidth * 0.5 + fw.x, min(dMinor.x, dMinor.y));

                float2 dMajor = DistanceToGrid(w, _CellSize * _MajorEvery);
                float major = 1.0 - smoothstep(0.0, _MajorWidth * 0.5 + fw.x, min(dMajor.x, dMajor.y));

                // 纸面颗粒按世界坐标定死，相机移动时它跟着地面走，是第二重视差线索
                float grain = (Hash21(floor(w * _GrainScale)) - 0.5) * _GrainAmount;

                float3 col = _BaseColor.rgb;
                col = lerp(col, _LineColor.rgb, minor);
                col = lerp(col, _MajorColor.rgb, major);
                col += grain;

                // 地面是有限的 quad，不在边缘淡出会露出一条生硬的边界
                float fade = 1.0 - smoothstep(_FadeRadius * 0.55, _FadeRadius, length(w));
                col = lerp(_FogColor.rgb, col, fade);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
