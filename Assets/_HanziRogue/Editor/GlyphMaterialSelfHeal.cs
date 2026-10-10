using UnityEditor;
using UnityEngine;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 字形材质自愈：每次脚本编译完（域重载）后，把两张字建材质跟代码约定对一次账。
    ///
    /// 存在的理由是一次真实的失败（2026-10-09）：<see cref="GlyphAssetBuilder"/> 里
    /// 已经把着色器选型收在一处分支，逻辑没错；但**那只在跑菜单时才生效**。
    /// 着色器是存在 .mat 资产里的一个字段，C# 改了它不会跟着变。于是编辑器里一切正常，
    /// 一进 Play 就是「英雄字块钉在世界原点、武器自己走自己的」——而代码、shader、编译
    /// 三样全是干净的，最难查的那类。
    ///
    /// 教训不是「记得跑菜单」，是「别把修法挂在一个需要人记得的动作上」。
    /// 所以把对账提到这里：只要脚本编译过，它就必须是对的。
    /// </summary>
    [InitializeOnLoad]
    internal static class GlyphMaterialSelfHeal
    {
        static GlyphMaterialSelfHeal()
        {
            // 静态构造是在域重载的**中途**跑的，那时资产数据库还在重建索引，
            // 直接读写资产不安全。delayCall 等编辑器空闲下来再动手。
            EditorApplication.delayCall += Heal;
        }

        private static void Heal()
        {
            int fixedCount = 0;

            if (GlyphAssetBuilder.AlignMaterialShader("Mat_Glyph_Bing", instancing: true))
            {
                fixedCount++;
            }

            if (GlyphAssetBuilder.AlignMaterialShader("Mat_Glyph_Hero", instancing: false))
            {
                fixedCount++;
            }

            if (fixedCount == 0)
            {
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[字形材质自愈] 有 {fixedCount} 张字建材质的着色器跟代码约定不一致，已纠正。" +
                      "（正常情况下不该发生；如果反复出现，说明有别的脚本在写这些材质。）");
        }
    }
}
