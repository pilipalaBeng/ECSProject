using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

using HanziRogue.Core;
using HanziRogue.Gameplay;
using HanziRogue.Presentation;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 局内 / 局外往返自检（单场景双模式版）。
    ///
    /// 要回答的问题：「进战斗 → 回局外 → 再进战斗，会不会出问题？」
    ///
    /// 关键事实（Entities 1.0.16 源码 DefaultWorldInitialization.cs）：
    /// `World.DefaultGameObjectInjectionWorld` 只在 `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`
    /// 建一次——那是「进入 Play 会话」的时机，不是「加载场景」的时机。
    /// 无论是 `LoadScene` 还是 `SetActive`，都不会动这个 World，运行时 `CreateEntity` 建的实体
    /// 一个都不会少。所以「一局」的边界必须显式建立，归 `BattleSession` 管（ADR-0012）。
    ///
    /// 本自检不开场景，把这条路径压到 World + ModeSwitcher 层：直接调用真实的
    /// `ModeSwitcher.EnterBattle/EnterMeta` 与 `BattleSession.Enter/Exit`（走产品代码，不是复刻逻辑），
    /// 两个场景对照：
    ///   ① 往返 Enter → Exit → Enter —— 预期：每一环的实体数、异常数、英雄位移全部达标
    ///   ② 重复 Enter（不先 Exit）—— 预期：照样只有 1 个 GameConfig（Enter 自带清场）
    ///
    /// 宪法 §4.3：行为结论必须实测，禁止凭代码观感下结论。
    /// </summary>
    public static class SceneFlowSmokeTest
    {
        private const float DeltaTime = 1f / 60f;

        /// <summary>进战斗后观察位移的帧数。</summary>
        private const int ObserveFrames = 20;

        private const string ConfigAssetPath = "Assets/_HanziRogue/Data/ScaleConfig.asset";

        private static readonly List<string> Exceptions = new List<string>();

        /// <summary>
        /// 报告同时落盘。理由：普通 Log 在 Console 里可能被过滤级别挡住，
        /// 而 ExecuteMethod / MCP 通道都要靠日志取结论——落盘才是稳的取证方式。
        /// </summary>
        private static readonly System.Text.StringBuilder Report = new System.Text.StringBuilder();

        private static bool _capturing;
        private static int _failures;

        [MenuItem("HanziRogue/Scene Flow Smoke Test")]
        public static void Run()
        {
            Exceptions.Clear();
            Report.Length = 0;
            _failures = 0;
            Application.logMessageReceived += OnLog;

            try
            {
                Write("========== 场景①：往返 Enter → Exit → Enter ==========");
                RoundTripScenario();

                Write(string.Empty);
                Write("========== 场景②：重复 Enter（不先 Exit） ==========");
                ReenterScenario();
            }
            catch (Exception e)
            {
                Write($"[SceneFlow] 自检自身异常：{e}");
                _failures++;
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Write(string.Empty);
            Write(_failures == 0
                ? "[SceneFlow] ALL PASS：两个场景的观测都与预期一致"
                : $"[SceneFlow] FAIL：{_failures} 项观测与预期不一致");

            DumpReport();
        }

        // ---------------------------------------------------------------- 场景①

        private static void RoundTripScenario()
        {
            Rig rig = BuildRig("SceneFlowRoundTrip");
            try
            {
                // ---- 第一局：进战斗 ----
                rig.Switcher.EnterBattle();
                Step(rig.World, 1);

                Write($"[1] 首次进战斗：GameConfig={Count<GameConfig>(rig.World)}，" +
                      $"英雄={Count<HeroTag>(rig.World)}，兵={Count<EnemyTag>(rig.World)}，" +
                      $"模式={rig.Switcher.Current}");

                Check("首次进战斗后 GameConfig == 1", Count<GameConfig>(rig.World) == 1);
                Check("首次进战斗后英雄 == 1", Count<HeroTag>(rig.World) == 1);
                Check($"首次进战斗后兵 == {rig.Config.EnemyCount}",
                    Count<EnemyTag>(rig.World) == rig.Config.EnemyCount);
                Check("首次进战斗后 battleRoot 激活 / metaRoot 失活",
                    rig.BattleRoot.activeSelf && !rig.MetaRoot.activeSelf);

                // ---- 回局外：战斗实体应当全部销毁，ECS 空转 ----
                rig.Switcher.EnterMeta();
                Step(rig.World, 1);

                Write($"[2] 返回局外：GameConfig={Count<GameConfig>(rig.World)}，" +
                      $"英雄={Count<HeroTag>(rig.World)}，兵={Count<EnemyTag>(rig.World)}，" +
                      $"模式={rig.Switcher.Current}");

                Check("回局外后 GameConfig == 0", Count<GameConfig>(rig.World) == 0);
                Check("回局外后英雄 == 0", Count<HeroTag>(rig.World) == 0);
                Check("回局外后兵 == 0", Count<EnemyTag>(rig.World) == 0);
                Check("回局外后 metaRoot 激活 / battleRoot 失活",
                    rig.MetaRoot.activeSelf && !rig.BattleRoot.activeSelf);

                // ---- 局外多停几帧，确认没有实体残留、系统无事可做 ----
                Step(rig.World, 30);
                Check("局外停 30 帧后仍无任何战斗实体",
                    Count<GameConfig>(rig.World) == 0 &&
                    Count<HeroTag>(rig.World) == 0 &&
                    Count<EnemyTag>(rig.World) == 0);

                // ---- 第二局：再进战斗 ----
                rig.Switcher.EnterBattle();
                Step(rig.World, 1);

                Write($"[3] 二次进战斗：GameConfig={Count<GameConfig>(rig.World)}，" +
                      $"英雄={Count<HeroTag>(rig.World)}，兵={Count<EnemyTag>(rig.World)}");

                Check("二次进战斗后 GameConfig == 1（不是 2）", Count<GameConfig>(rig.World) == 1);
                Check("二次进战斗后英雄 == 1", Count<HeroTag>(rig.World) == 1);
                Check($"二次进战斗后兵 == {rig.Config.EnemyCount}",
                    Count<EnemyTag>(rig.World) == rig.Config.EnemyCount);

                // ---- 系统是否真的活着：驱动英雄 +X，看位移与异常 ----
                SetHeroIntent(rig.World, new float2(1f, 0f));
                float2 heroBefore = ReadHeroPosition(rig.World);
                float[] snapshot = SnapshotEnemyPositions(rig.World);

                Exceptions.Clear();
                _capturing = true;
                Step(rig.World, ObserveFrames);
                _capturing = false;

                float heroMoved = math.distance(heroBefore, ReadHeroPosition(rig.World));
                float enemyMoved = AverageEnemyDisplacement(rig.World, snapshot);

                Write($"[4] 二次进战斗后跑 {ObserveFrames} 帧：英雄位移 {heroMoved:F3} 米，" +
                      $"兵平均位移 {enemyMoved:F3} 米");
                Write($"[5] 这 {ObserveFrames} 帧里系统抛异常 {Exceptions.Count} 条" +
                      (Exceptions.Count > 0 ? $"：{Exceptions[0]}" : string.Empty));

                Check("二次进战斗后系统零异常", Exceptions.Count == 0);
                Check("二次进战斗后英雄能位移（> 1 米）", heroMoved > 1f);
                Check("二次进战斗后兵能位移（> 0.01 米）", enemyMoved > 0.01f);
            }
            finally
            {
                Teardown(rig);
            }
        }

        // ---------------------------------------------------------------- 场景②

        private static void ReenterScenario()
        {
            Rig rig = BuildRig("SceneFlowReenter");
            try
            {
                rig.Switcher.EnterBattle();
                Step(rig.World, 1);
                int firstCount = Count<EnemyTag>(rig.World);

                // 不调 Exit，直接再 Enter——模拟「重开一局」这类入口。
                // Enter 自带清场，所以不该出现第二个 GameConfig。
                rig.Switcher.EnterBattle();
                Step(rig.World, 1);

                Write($"[1] 首次进战斗：GameConfig={Count<GameConfig>(rig.World)}，兵={firstCount}");
                Write($"[2] 直接二次 Enter：GameConfig={Count<GameConfig>(rig.World)}，" +
                      $"英雄={Count<HeroTag>(rig.World)}，兵={Count<EnemyTag>(rig.World)}");

                Check("重复 Enter 后 GameConfig == 1（Enter 自带清场）",
                    Count<GameConfig>(rig.World) == 1);
                Check("重复 Enter 后英雄 == 1", Count<HeroTag>(rig.World) == 1);
                Check($"重复 Enter 后兵 == {rig.Config.EnemyCount}（不叠加）",
                    Count<EnemyTag>(rig.World) == rig.Config.EnemyCount);

                SetHeroIntent(rig.World, new float2(0f, 1f));
                float2 heroBefore = ReadHeroPosition(rig.World);
                float[] snapshot = SnapshotEnemyPositions(rig.World);

                Exceptions.Clear();
                _capturing = true;
                Step(rig.World, ObserveFrames);
                _capturing = false;

                float heroMoved = math.distance(heroBefore, ReadHeroPosition(rig.World));
                float enemyMoved = AverageEnemyDisplacement(rig.World, snapshot);

                Write($"[3] 再跑 {ObserveFrames} 帧：英雄位移 {heroMoved:F3} 米，" +
                      $"兵平均位移 {enemyMoved:F3} 米，系统异常 {Exceptions.Count} 条");

                Check("重复 Enter 后系统零异常", Exceptions.Count == 0);
                Check("重复 Enter 后英雄能位移（> 1 米）", heroMoved > 1f);
                Check("重复 Enter 后兵能位移（> 0.01 米）", enemyMoved > 0.01f);
            }
            finally
            {
                Teardown(rig);
            }
        }

        // ---------------------------------------------------------------- 测试台

        private sealed class Rig
        {
            public World World;
            public World PreviousDefault;
            public ScaleConfig Config;
            public ModeSwitcher Switcher;
            public GameObject Flow;
            public GameObject MetaRoot;
            public GameObject BattleRoot;
        }

        /// <summary>
        /// 搭一个「只有逻辑、没有场景资产」的最小台子：自建 World + 真实的产品组件。
        /// ModeSwitcher 在这里由 AddComponent 得到——编辑模式下不会触发 Awake，
        /// 初始模式就是字段默认值，测试起点确定。
        /// </summary>
        private static Rig BuildRig(string worldName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScaleConfig>(ConfigAssetPath);
            if (asset == null)
            {
                throw new InvalidOperationException($"读不到 {ConfigAssetPath}");
            }

            var world = new World(worldName);
            World previousDefault = World.DefaultGameObjectInjectionWorld;
            World.DefaultGameObjectInjectionWorld = world;

            var sim = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpawnSystem>());
            sim.AddSystemToUpdateList(world.GetOrCreateSystem<HeroMoveSystem>());
            sim.AddSystemToUpdateList(world.GetOrCreateSystem<BoidsSystem>());
            // HeroIntentSystem 故意不挂（沿用 BoidsSmokeTest 的理由）：无头环境没有键盘，
            // 改为直接驱动 HeroIntent 组件，这样测的是移动与集群，不是输入读取。

            var flow = new GameObject("GameFlow");
            var sessionGo = new GameObject("BattleSession");
            sessionGo.transform.SetParent(flow.transform, false);
            var session = sessionGo.AddComponent<BattleSession>();
            SetPrivate(session, "config", asset);

            var metaRoot = new GameObject("MetaRoot");
            var battleRoot = new GameObject("BattleRoot");

            var switcher = flow.AddComponent<ModeSwitcher>();
            SetPrivate(switcher, "session", session);
            SetPrivate(switcher, "metaRoot", metaRoot);
            SetPrivate(switcher, "battleRoot", battleRoot);

            return new Rig
            {
                World = world,
                PreviousDefault = previousDefault,
                Config = asset,
                Switcher = switcher,
                Flow = flow,
                MetaRoot = metaRoot,
                BattleRoot = battleRoot
            };
        }

        private static void Teardown(Rig rig)
        {
            UnityEngine.Object.DestroyImmediate(rig.Flow);
            UnityEngine.Object.DestroyImmediate(rig.MetaRoot);
            UnityEngine.Object.DestroyImmediate(rig.BattleRoot);
            World.DefaultGameObjectInjectionWorld = rig.PreviousDefault;
            rig.World.Dispose();
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (info == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name} 上没有字段 {field}");
            }

            info.SetValue(target, value);
        }

        private static void Check(string label, bool ok)
        {
            Write($"    {(ok ? "PASS" : "FAIL")}  {label}");
            if (!ok)
            {
                _failures++;
            }
        }

        private static void Write(string line)
        {
            Debug.Log(line);
            Report.AppendLine(line);
        }

        private static void DumpReport()
        {
            string path = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "../Logs/sceneflow_report.txt"));
            System.IO.File.WriteAllText(path, Report.ToString());
        }

        private static float _elapsed;

        private static void Step(World world, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _elapsed += DeltaTime;
                world.SetTime(new TimeData(_elapsed, DeltaTime));
                world.Update();
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!_capturing)
            {
                return;
            }

            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                Exceptions.Add($"{type}: {condition}");
            }
        }

        private static int Count<T>(World world) where T : unmanaged, IComponentData
        {
            var query = world.EntityManager.CreateEntityQuery(typeof(T));
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }

        private static void SetHeroIntent(World world, float2 move)
        {
            EntityManager em = world.EntityManager;
            var query = em.CreateEntityQuery(typeof(HeroIntent), typeof(HeroTag));
            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new HeroIntent { Move = move });
            }

            entities.Dispose();
            query.Dispose();
        }

        private static float2 ReadHeroPosition(World world)
        {
            EntityManager em = world.EntityManager;
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(HeroTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            float2 result = positions.Length > 0 ? positions[0].Value : float2.zero;
            positions.Dispose();
            query.Dispose();
            return result;
        }

        private static float[] SnapshotEnemyPositions(World world)
        {
            var query = world.EntityManager.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            var snapshot = new float[positions.Length * 2];
            for (int i = 0; i < positions.Length; i++)
            {
                snapshot[i * 2] = positions[i].Value.x;
                snapshot[i * 2 + 1] = positions[i].Value.y;
            }

            positions.Dispose();
            query.Dispose();
            return snapshot;
        }

        private static float AverageEnemyDisplacement(World world, float[] before)
        {
            var query = world.EntityManager.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            int count = positions.Length;

            if (before.Length != count * 2)
            {
                positions.Dispose();
                query.Dispose();
                return float.NaN;
            }

            double sum = 0d;
            for (int i = 0; i < count; i++)
            {
                float2 delta = positions[i].Value - new float2(before[i * 2], before[i * 2 + 1]);
                sum += math.length(delta);
            }

            positions.Dispose();
            query.Dispose();
            return count == 0 ? float.NaN : (float)(sum / count);
        }
    }
}
