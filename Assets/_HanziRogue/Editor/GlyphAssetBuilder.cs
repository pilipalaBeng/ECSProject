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
    /// </summary>
    public static class GlyphAssetBuilder
    {
        private const string GlyphDir = "Assets/_HanziRogue/Data/Glyphs";
        private const string MaterialDir = "Assets/_HanziRogue/Data/Materials";
        private const string ShaderName = "HanziRogue/GlyphInstanced";

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

        /// <summary>英雄材质：单实例 GameObject，不需要 instancing。</summary>
        public static Material EnsureHeroMaterial()
        {
            return EnsureMaterial("Mat_Glyph_Hero", "tex_glyph_hero", HeroTint, instancing: false);
        }

        private static Material EnsureMaterial(string name, string textureName, Color tint, bool instancing)
        {
            EnsureFolder(MaterialDir);

            Texture2D texture = ImportGlyphTexture($"{GlyphDir}/{textureName}.png");
            if (texture == null)
            {
                Debug.LogError($"[GlyphAssetBuilder] 找不到字形贴图 {textureName}.png，" +
                               "请先跑 Tools/glyph_bake.py 烘焙。");
                return null;
            }

            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[GlyphAssetBuilder] 着色器 {ShaderName} 未找到，材质无法创建。");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = Shader.Find(ShaderName);
            material.mainTexture = texture;
            material.SetColor("_Color", tint);
            material.enableInstancing = instancing;
            EditorUtility.SetDirty(material);
            return material;
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
