using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using HanziRogue.Core;
using HanziRogue.Presentation;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 生成最小闭环场景并写入 Build Settings。
    /// 用途：让项目从零到「能跑」不需要人工在编辑器里点来点去。
    /// 装配顺序：字形资产 -> 配置 -> 场景。缺任一环都会在日志里报错，不静默降级。
    ///
    /// 单场景结构（ADR-0013）：一个 MainScene，局内局外是两组互斥显隐的根节点。
    /// 之所以不是两个场景——ECS 的 World 全局常驻，加载场景不会重建它，
    /// 「场景边界」与「状态边界」错开，多开一个普通 Scene 承载不了 ECS 内容。
    /// </summary>
    public static class SceneBuilder
    {
        private const string SceneDir = "Assets/Scenes";
        private const string DataDir = "Assets/_HanziRogue/Data";

        [MenuItem("HanziRogue/Build Minimal Scenes")]
        public static void Build()
        {
            if (!PrepareForNewScene())
            {
                return;
            }

            EnsureFolder(SceneDir);
            EnsureFolder(DataDir);

            ScaleConfig config = EnsureConfig();
            CombatConfig combatConfig = EnsureCombatConfig();
            Material enemyMaterial = GlyphAssetBuilder.EnsureEnemyMaterial();
            Material heroMaterial = GlyphAssetBuilder.EnsureHeroMaterial();
            Material weaponMaterial = EnsureWeaponMaterial();

            // 材质没装配成功就中止，不生成「英雄/字潮看不见」的场景。
            // 那种现场比不建更难查：文件都在、日志里只有一条 error，进 Play 看到的是空场地，
            // 第一反应会去怀疑 ECS 没生成实体——方向直接错了（2026-10-09 的英雄字块事故同源）。
            if (enemyMaterial == null || heroMaterial == null || weaponMaterial == null)
            {
                Debug.LogError("[SceneBuilder] 字形/武器材质未装配成功，已中止建场景。"
                               + "常见原因：新增的 .shader 还没被编辑器导入完成，聚焦一下编辑器再重跑。");
                return;
            }

            BuildMainScene(config, combatConfig, enemyMaterial, heroMaterial, weaponMaterial);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{SceneDir}/{SceneNames.Main}.unity", true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SceneBuilder] {SceneNames.Main} 已生成并写入 Build Settings。");
        }

        /// <summary>
        /// 建新场景前先落盘有路径的脏场景。不这么做，<c>NewScene</c> 会弹保存框——
        /// 自动化通道下那就是挂死。未命名脏场景无法静默处理，直接报错中止而不是干等。
        /// </summary>
        private static bool PrepareForNewScene()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isDirty)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path))
                {
                    Debug.LogError("[SceneBuilder] 存在未命名且未保存的场景，为避免弹保存框挡住自动化已中止。" +
                                   "请先手动保存或关闭它，再重新运行。");
                    return false;
                }

                EditorSceneManager.SaveScene(scene);
            }

            return true;
        }

        private static void BuildMainScene(ScaleConfig config, CombatConfig combatConfig,
            Material enemyMaterial, Material heroMaterial, Material weaponMaterial)
        {
            // EmptyScene：每个 GameObject 都由这里显式创建，不留 Unity 默认的相机/灯光，
            // 免得「多出来一台相机」这种问题要等到运行时才发现。
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildGameFlow(config, combatConfig, out ModeSwitcher switcher);
            GameObject metaRoot = BuildMetaRoot(switcher);
            GameObject battleRoot = BuildBattleRoot(switcher, heroMaterial, enemyMaterial, weaponMaterial);

            SetPrivateField(switcher, "metaRoot", metaRoot);
            SetPrivateField(switcher, "battleRoot", battleRoot);

            // 场景装载时以局外为准，与 ModeSwitcher.Awake 的初始状态一致。
            metaRoot.SetActive(true);
            battleRoot.SetActive(false);

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{SceneNames.Main}.unity");
            Debug.Log($"[SceneBuilder] {SceneNames.Main} 完成。");
        }

        /// <summary>常驻层：不随模式切换失活。AudioListener 全局唯一，挂在树上而不是某台相机上。</summary>
        private static void BuildGameFlow(ScaleConfig config, CombatConfig combatConfig, out ModeSwitcher switcher)
        {
            var flow = new GameObject("GameFlow");
            flow.AddComponent<AudioListener>();

            var sessionGo = new GameObject("BattleSession");
            sessionGo.transform.SetParent(flow.transform, false);
            var session = sessionGo.AddComponent<BattleSession>();
            SetPrivateField(session, "config", config);
            SetPrivateField(session, "combatConfig", combatConfig);

            switcher = flow.AddComponent<ModeSwitcher>();
            SetPrivateField(switcher, "session", session);
        }

        private static GameObject BuildMetaRoot(ModeSwitcher switcher)
        {
            var metaRoot = new GameObject("MetaRoot");

            CreateCamera("MetaCamera", metaRoot.transform, 18f);

            var uiGo = new GameObject("MetaUI");
            uiGo.transform.SetParent(metaRoot.transform, false);
            var meta = uiGo.AddComponent<MetaController>();
            SetPrivateField(meta, "switcher", switcher);

            return metaRoot;
        }

        private static GameObject BuildBattleRoot(ModeSwitcher switcher, Material heroMaterial,
            Material enemyMaterial, Material weaponMaterial)
        {
            var battleRoot = new GameObject("BattleRoot");

            Camera cam = CreateCamera("BattleCamera", battleRoot.transform, 18f);
            var battleCamera = cam.gameObject.AddComponent<BattleCamera>();

            CreateGround(battleRoot.transform);

            var rendererGo = new GameObject("EnemyRenderer");
            rendererGo.transform.SetParent(battleRoot.transform, false);
            var enemyRenderer = rendererGo.AddComponent<InstancedEnemyRenderer>();
            SetPrivateField(enemyRenderer, "glyphMaterial", enemyMaterial);

            GameObject heroGo = CreateHero(heroMaterial, battleRoot.transform);
            GameObject weaponGo = CreateWeapon(weaponMaterial, battleRoot.transform);
            WireHeroWeaponView(heroGo, weaponGo);

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(battleRoot.transform, false);
            var hud = hudGo.AddComponent<HudController>();
            SetPrivateField(hud, "battleCamera", battleCamera);
            SetPrivateField(hud, "switcher", switcher);

            return battleRoot;
        }

        /// <summary>
        /// 正交俯视相机。俯视角固定 90 度，字才是正的。
        /// 两台相机都打 MainCamera 标签：两组根节点互斥，任一时刻只有一台处于激活状态，
        /// 所以 <c>Camera.main</c> 在两种模式下都指向当前这台，不会指错。
        /// </summary>
        private static Camera CreateCamera(string name, Transform parent, float orthoSize)
        {
            var camGo = new GameObject(name);
            camGo.transform.SetParent(parent, false);
            camGo.tag = "MainCamera";

            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.05f, 0.055f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camGo.transform.position = new Vector3(0f, 60f, 0f);

            return cam;
        }

        /// <summary>
        /// 带网格的地面。网格的作用是**让玩家看出自己在动**——纯色地面上移动没有任何参照，
        /// 玩家会怀疑方向键是不是坏了。网格由 GridFloor shader 按世界坐标绘制，相机跟随英雄时
        /// 格子从脚下流过，位移一眼可见（ADR-0009）。
        /// 尺寸 400 的理由：全景视野 orthoSize=55、英雄最远跑到 ±50，
        /// 此时画面最远角距中心约 148 米，400 的一半（200）能盖住。
        /// </summary>
        private static void CreateGround(Transform parent)
        {
            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(parent, false);

            var filter = groundGo.AddComponent<MeshFilter>();
            filter.sharedMesh = GlyphQuadMesh.Shared();

            var renderer = groundGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureGroundMaterial();

            const float size = 400f;
            groundGo.transform.position = new Vector3(0f, 0f, 0f);
            groundGo.transform.localScale = new Vector3(size, 1f, size);
        }

        /// <summary>英雄：挂着「英雄」二字贴图的 quad，横向两倍宽——它就是「两个字的单位」。</summary>
        private static GameObject CreateHero(Material heroMaterial, Transform parent)
        {
            var heroGo = new GameObject("Hero");
            heroGo.transform.SetParent(parent, false);

            var filter = heroGo.AddComponent<MeshFilter>();
            filter.sharedMesh = GlyphQuadMesh.Shared();

            var renderer = heroGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = heroMaterial;

            heroGo.AddComponent<HeroView>();

            const float glyphSize = 1.5f;
            heroGo.transform.position = new Vector3(0f, 0.12f, 0f);
            heroGo.transform.localScale = new Vector3(glyphSize * 2f, 1f, glyphSize);

            return heroGo;
        }

        /// <summary>
        /// 武器节点：**不是 Hero 的子节点**。
        /// Hero 的缩放是 (3, 1, 1.5)（它承载着两个字的宽度），挂在其下的子节点会继承这份缩放，
        /// 一把 0.12×1.8 米的枪会被拉成 0.36×2.7 米的棒子。所以它与 Hero 平级，
        /// 位置由 <see cref="HeroWeaponView"/> 按英雄的 ECS 坐标直接写成世界坐标。
        /// </summary>
        private static GameObject CreateWeapon(Material weaponMaterial, Transform parent)
        {
            var weaponGo = new GameObject("HeroWeapon");
            weaponGo.transform.SetParent(parent, false);

            var filter = weaponGo.AddComponent<MeshFilter>();
            filter.sharedMesh = GlyphQuadMesh.Shared();

            var renderer = weaponGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = weaponMaterial;

            // 细长 quad，长轴沿 +Z——正是 Quaternion.LookRotation 会把朝向对准的那条轴。
            // 长度 2.8 与 CombatConfig.HeroReach（3.6 = thrustDistance 2.2 + 半长 1.4）对齐：
            // 枪尖伸到哪，判定就到哪。两者对不上就会出现「刀压着字却不掉血」（2026-10-10 实测）。
            weaponGo.transform.localScale = new Vector3(0.16f, 1f, 2.8f);
            return weaponGo;
        }

        private static void WireHeroWeaponView(GameObject heroGo, GameObject weaponGo)
        {
            var weaponView = heroGo.AddComponent<HeroWeaponView>();
            SetPrivateField(weaponView, "weaponRoot", weaponGo.transform);
            SetPrivateField(weaponView, "heroRenderer", heroGo.GetComponent<MeshRenderer>());
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

        /// <summary>
        /// 战斗配置资产。与 <see cref="ScaleConfig"/> 分开放：
        /// 一份管「有多少、追得多紧」，一份管「打得多疼、扎得多快」，两者的调参频率完全不同。
        /// </summary>
        private static CombatConfig EnsureCombatConfig()
        {
            string path = $"{DataDir}/CombatConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<CombatConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CombatConfig>();
                AssetDatabase.CreateAsset(config, path);
                AssetDatabase.SaveAssets();
            }
            return config;
        }

        /// <summary>
        /// 武器材质。走 Unity 自带的 Unlit/Color——它就是一根抽象的长条，
        /// 不需要任何贴图，也不该占用位图资产的预算（C2.5）。
        /// 只在首次创建时写值，之后不覆盖，规则同地面材质。
        /// </summary>
        private static Material EnsureWeaponMaterial()
        {
            string dir = $"{DataDir}/Materials";
            EnsureFolder(dir);
            string path = $"{dir}/Mat_Weapon.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    Debug.LogError("[SceneBuilder] 找不到 Unlit/Color，武器将无法渲染。");
                    return null;
                }

                material = new Material(shader);
                material.color = new Color(0.88f, 0.84f, 0.62f);
                AssetDatabase.CreateAsset(material, path);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 地面材质。只在首次创建时把默认值写进资产，之后不再覆盖——
        /// 参数是在 Play 模式下实时手感调出来的，重建场景不该把它们冲掉。
        /// 调参入口全在材质 Inspector（格距 / 线宽 / 颜色 / 淡出半径）。
        /// </summary>
        private static Material EnsureGroundMaterial()
        {
            string dir = $"{DataDir}/Materials";
            EnsureFolder(dir);
            string path = $"{dir}/Mat_GridFloor.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("HanziRogue/GridFloor");
                if (shader == null)
                {
                    // 不静默降级到看不出移动的纯色地面——这属于「玩家会以为操作失灵」的问题
                    Debug.LogError("[SceneBuilder] 找不到 HanziRogue/GridFloor，" +
                                   "地面将退回纯色，玩家会看不出自己在移动。检查 GridFloor.shader 是否有编译错误。");
                    shader = Shader.Find("Unlit/Color");
                }

                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

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
