using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HanziRogue.Core;
using HanziRogue.Presentation;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 生成最小闭环的两个场景并写入 Build Settings。
    /// 用途：让项目从零到「能跑」不需要人工在编辑器里点来点去。
    /// 装配顺序：字形资产 -> 配置 -> 场景。缺任一环都会在日志里报错，不静默降级。
    /// </summary>
    public static class SceneBuilder
    {
        private const string SceneDir = "Assets/Scenes";
        private const string DataDir = "Assets/_HanziRogue/Data";

        [MenuItem("HanziRogue/Build Minimal Scenes")]
        public static void Build()
        {
            EnsureFolder(SceneDir);
            EnsureFolder(DataDir);

            ScaleConfig config = EnsureConfig();
            Material enemyMaterial = GlyphAssetBuilder.EnsureEnemyMaterial();
            Material heroMaterial = GlyphAssetBuilder.EnsureHeroMaterial();

            BuildMetaScene();
            BuildBattleScene(config, enemyMaterial, heroMaterial);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{SceneDir}/{SceneNames.Meta}.unity", true),
                new EditorBuildSettingsScene($"{SceneDir}/{SceneNames.Battle}.unity", true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SceneBuilder] MetaScene / BattleScene 已生成并写入 Build Settings。");
        }

        private static void BuildMetaScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("MetaRoot");
            root.AddComponent<MetaController>();
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{SceneNames.Meta}.unity");
            Debug.Log("[SceneBuilder] MetaScene 完成。");
        }

        private static void BuildBattleScene(ScaleConfig config, Material enemyMaterial, Material heroMaterial)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera cam = EnsureCamera();
            CreateGround();
            var camera = cam.gameObject.GetComponent<BattleCamera>();

            var bootstrapGo = new GameObject("BattleBootstrap");
            var bootstrap = bootstrapGo.AddComponent<BattleBootstrap>();
            SetPrivateField(bootstrap, "config", config);

            var rendererGo = new GameObject("EnemyRenderer");
            var enemyRenderer = rendererGo.AddComponent<InstancedEnemyRenderer>();
            SetPrivateField(enemyRenderer, "glyphMaterial", enemyMaterial);

            CreateHero(heroMaterial);

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            SetPrivateField(hud, "battleCamera", camera);

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{SceneNames.Battle}.unity");
            Debug.Log("[SceneBuilder] BattleScene 完成。");
        }

        /// <summary>正交俯视相机 + 跟随组件。俯视角固定 90 度，字才是正的。</summary>
        private static Camera EnsureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }

            cam.orthographic = true;
            cam.orthographicSize = 18f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.05f, 0.055f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.transform.position = new Vector3(0f, 60f, 0f);

            if (cam.GetComponent<BattleCamera>() == null)
            {
                cam.gameObject.AddComponent<BattleCamera>();
            }

            return cam;
        }

        /// <summary>深墨色地面。没有地面的话字潮悬在天空盒上，明暗对比不足看不清。</summary>
        private static void CreateGround()
        {
            var groundGo = new GameObject("Ground");
            var filter = groundGo.AddComponent<MeshFilter>();
            filter.sharedMesh = GlyphQuadMesh.Shared();

            var renderer = groundGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureGroundMaterial();

            float size = 220f;
            groundGo.transform.position = new Vector3(0f, 0f, 0f);
            groundGo.transform.localScale = new Vector3(size, 1f, size);
        }

        /// <summary>英雄：挂着「英雄」二字贴图的 quad，横向两倍宽——它就是「两个字的单位」。</summary>
        private static void CreateHero(Material heroMaterial)
        {
            var heroGo = new GameObject("Hero");
            var filter = heroGo.AddComponent<MeshFilter>();
            filter.sharedMesh = GlyphQuadMesh.Shared();

            var renderer = heroGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = heroMaterial;

            heroGo.AddComponent<HeroView>();

            const float glyphSize = 1.5f;
            heroGo.transform.position = new Vector3(0f, 0.12f, 0f);
            heroGo.transform.localScale = new Vector3(glyphSize * 2f, 1f, glyphSize);
        }

        private static ScaleConfig EnsureConfig()
        {
            string path = $"{DataDir}/ScaleConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<ScaleConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ScaleConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            return config;
        }

        private static Material EnsureGroundMaterial()
        {
            string path = $"{DataDir}/GroundMaterial.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Unlit/Color"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = new Color(0.075f, 0.07f, 0.085f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>设置 private [SerializeField] 字段——装配场景时不放开 public 字段污染代码层。</summary>
        private static void SetPrivateField(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[SceneBuilder] 字段 {fieldName} 在 {target.GetType().Name} 上不存在。");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
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
