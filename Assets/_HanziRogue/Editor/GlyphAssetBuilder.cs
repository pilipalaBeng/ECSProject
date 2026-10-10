using System.IO;
using UnityEditor;
using UnityEngine;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 字形资产的装配器：把 Python 烘焙出的 PNG 转成可用的材质。
    /// 分工原因——PNG 由 Tools/glyph_bake.py 离线生成（batchmode 下字体图集不可靠），
    /// 而贴图导入参数与材质实例属于 Unity 资产管线，归 Editor 脚本管。
    /// 为什么不用 TextMeshPro：CONSTRAINTS C4.1。
    ///
    /// **两种字形走两种着色器**，这不是可选项：
    /// 字潮（兵）是 GPU instancing 的，位置来自 StructuredBuffer、顶点阶段不读 Transform；
    /// 英雄是普通 MeshRenderer，必须读 Transform。曾经两边共用 GlyphInstanced，
    /// 于是英雄字块被钉在世界原点、尺寸恒为 1.1 米（跟「兵」一样大），
    /// 表现成「英雄不动、武器却自己走自己的」（2026-10-09 实测，详见 GlyphFlat.shader 顶部注释）。
    /// </summary>
    public static class GlyphAssetBuilder
    {
        private const string GlyphDir = "Assets/_HanziRogue/Data/Glyphs";
        private const string MaterialDir = "Assets/_HanziRogue/Data/Materials";

        /// <summary>字潮（多实例）着色器：位置与闪白强度来自 StructuredBuffer，**不读 Transform**。</summary>
        private const string InstancedShaderName = "HanziRogue/GlyphInstanced";

        /// <summary>单实例字形着色器：挂在 GameObject 上的普通 MeshRenderer，老老实实读 Transform。</summary>
        private const string FlatShaderName = "HanziRogue/GlyphFlat";

        // 配色：深墨底 + 朱砂兵 + 金英雄。字潮靠明度差拉开层次，不靠饱和度打架。
        private static readonly Color EnemyTint = new Color(0.86f, 0.27f, 0.21f, 1f);
        private static readonly Color HeroTint = new Color(1f, 0.82f, 0.34f, 1f);

        [MenuItem("HanziRogue/Rebuild Glyph Assets")]
        public static void RebuildAll()
        {
            EnsureMaterial("Mat_Glyph_Bing", "tex_glyph_bing", EnemyTint, instancing: true);
            EnsureMaterial("Mat_Glyph_Hero", "tex_glyph_hero", HeroTint, instancing: false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlyphAssetBuilder] 字形材质已重建。");
        }

        /// <summary>字潮材质：开启 instancing，单批次可画上千个「兵」。</summary>
        public static Material EnsureEnemyMaterial()
        {
            return EnsureMaterial("Mat_Glyph_Bing", "tex_glyph_bing", EnemyTint, instancing: true);
        }

        /// <summary>英雄材质：单实例 GameObject，走 GlyphFlat（读 Transform）。</summary>
        public static Material EnsureHeroMaterial()
        {
            return EnsureMaterial("Mat_Glyph_Hero", "tex_glyph_hero", HeroTint, instancing: false);
        }

        private static Material EnsureMaterial(string name, string textureName, Color tint, bool instancing)
        {
            EnsureFolder(MaterialDir);

            // 着色器由「是不是 instanced 字形」决定，不由调用方各写一份名字——
            // 两边共用同一个着色器正是这次的根因，把选型收在这一个分支里，下次改不动它。
            string shaderName = instancing ? InstancedShaderName : FlatShaderName;

            Texture2D texture = ImportGlyphTexture($"{GlyphDir}/{textureName}.png");
            if (texture == null)
            {
                Debug.LogError($"[GlyphAssetBuilder] 找不到字形贴图 {textureName}.png，" +
                               "请先跑 Tools/glyph_bake.py 烘焙。");
                return null;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[GlyphAssetBuilder] 着色器 {shaderName} 未找到，{name} 无法创建。" +
                               "刚新增过 .shader 文件时，先让编辑器导入完再重跑本脚本。");
                return null;
            }

            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            // 无条件重写 shader：资产被指到别的（或已消失的）着色器上时必须纠回来。
            // 这一步就是「重跑一次装配即可修好英雄字块」能成立的原因。
            material.shader = shader;
            material.mainTexture = texture;
            material.SetColor("_Color", tint);
            // 闪色只写一个约定值，逐单位强弱由各自的通道控制：
            // 兵走 StructuredBuffer 的 Flash，英雄走 MaterialPropertyBlock 的 _Flash。
            material.SetColor("_FlashColor", Color.white);
            material.SetFloat("_Cutoff", 0.35f);

            if (instancing)
            {
                // 只有 instanced 版有 _GlyphSize：字潮是统一尺寸，正方形 quad 直接放大即可；
                // 英雄的尺寸由 GameObject 的 scale 给（它必须是两个字的宽度）。
                material.SetFloat("_GlyphSize", 1.1f);
            }

            material.enableInstancing = instancing;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 「对账」而不是「重建」：材质已经存在、但它的着色器（或 instancing 开关）跟
        /// 代码约定不符时，纠回来。返回是否真的动过资产。
        ///
        /// 为什么需要单独一个入口：<see cref="EnsureMaterial"/> 那一套是**显式重建**，
        /// 要人去点菜单才会跑。而 shader 是**存在 .mat 资产里的一个字段**——C# 改了它不会自己变。
        /// 2026-10-09 就栽在这儿：修法正确，但修法挂在一个没人会跑的动作上，
        /// 结果就是「代码全对、跑起来英雄还是不动」。所以这门手艺得有个能被自动调用的窄入口。
        ///
        /// 刻意不做的事：材质不存在就不创建（凭空生资产应该是显式动作）、
        /// 不动贴图/颜色/cutoff（那些属于外观，交给重建）。
        /// </summary>
        public static bool AlignMaterialShader(string name, bool instancing)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
            if (material == null)
            {
                // 还没建过 → 留给菜单，这里不代劳
                return false;
            }

            Shader wanted = Shader.Find(instancing ? InstancedShaderName : FlatShaderName);
            if (wanted == null)
            {
                // 刚新增的 .shader 还没导入完 → 这次跳过，等编辑器导入完的下一次重载
                return false;
            }

            if (material.shader == wanted && material.enableInstancing == instancing)
            {
                return false;
            }

            material.shader = wanted;
            material.enableInstancing = instancing;
            EditorUtility.SetDirty(material);
            return true;
        }

        /// <summary>把烘焙 PNG 按「透明底带 alpha」的规格导入，并返回资产引用。</summary>
        private static Texture2D ImportGlyphTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool dirty = false;
                dirty |= SetIfChanged(importer.alphaIsTransparency, true, v => importer.alphaIsTransparency = v);
                dirty |= SetIfChanged(importer.mipmapEnabled, true, v => importer.mipmapEnabled = v);
                dirty |= SetIfChanged(importer.filterMode, FilterMode.Trilinear, v => importer.filterMode = v);
                dirty |= SetIfChanged(importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
                // 字形贴图是 NPOT（256x128），必须关掉缩放，否则会被拉伸变形
                dirty |= SetIfChanged(importer.npotScale, TextureImporterNPOTScale.None, v => importer.npotScale = v);

                if (dirty)
                {
                    importer.SaveAndReimport();
                }
            }
            else
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool SetIfChanged<T>(T current, T wanted, System.Action<T> assign)
        {
            if (Equals(current, wanted))
            {
                return false;
            }

            assign(wanted);
            return true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
