using System;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;
using HanziRogue.Gameplay;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 无头自检：不进 PlayMode，直接建一个 World 把「千兵追英雄」跑起来。
    /// 验证五件事：实体确实生成了、字潮从外圈起步、位置没算成 NaN、
    /// 敌人平均距离朝英雄下降、英雄被场地边界拦住跑不出去。
    /// 宪法 §4.3：行为与性能结论必须实测，禁止凭代码观感下结论。
    /// </summary>
    public static class BoidsSmokeTest
    {
        private const int Frames = 240;
        private const int ConvergeFrames = 300;
        private const float DeltaTime = 1f / 60f;

        /// <summary>
        /// 「贴身」判定半径。小于此距离才算真正咬住英雄。
        /// 绕圈式追击（Boids 聚合 / 对齐的副作用）下这个数会一直是 0，
        /// 所以它专门用来抓「追而不咬」这种最难从平均距离上看出来的毛病。
        /// </summary>
        private const float CloseRadius = 1.5f;

        public static void Run()
        {
            var world = new World("BoidsSmokeTest");
            try
            {
                EntityManager em = world.EntityManager;
                GameConfig config = LoadConfig();
                Entity configEntity = em.CreateEntity(typeof(GameConfig));
                em.SetComponentData(configEntity, config);

                // GetOrCreateSystem 只创建系统，不会自动挂进所属 group 的 update list，
                // 因此这里显式挂载——否则 world.Update() 只会空转顶层 group。
                // HeroInputSystem 故意不挂：无头环境没有键盘，改为直接驱动 HeroInput 组件，
                // 这样验证的是 HeroMoveSystem 的移动与边界，而不是输入读取。
                var simulationGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
                simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpawnSystem>());
                simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<HeroMoveSystem>());
                simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<BoidsSystem>());

                int systemCount = 0;
                foreach (var handle in world.Systems)
                {
                    systemCount++;
                }

                Debug.Log($"[SmokeTest] World 顶层系统数 = {systemCount}，" +
                          $"GameConfig 实体数 = {em.CreateEntityQuery(typeof(GameConfig)).CalculateEntityCount()}");

                world.SetTime(new TimeData(0f, DeltaTime));
                world.Update();

                int spawned = em.CreateEntityQuery(typeof(EnemyTag)).CalculateEntityCount();
                float spawnInnerGap = MinDistanceToHero(em);
                float spawnMinPair = MinPairwiseDistance(em);

                // 阶段一：英雄一路往 +X 跑。场地半边长 50，速度 14，4 秒足以撞到边界。
                // 这一段只验证「边界拦得住」——英雄比敌人快，被追期间平均距离上升是正确行为，
                // 所以不能拿这一段判断收敛。
                SetHeroInput(em, new float2(1f, 0f));

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 1; i <= Frames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                stopwatch.Stop();
                float2 heroPos = ReadHeroPosition(em);
                bool bounded = math.abs(heroPos.x) <= config.FieldSize + 0.01f
                               && math.abs(heroPos.y) <= config.FieldSize + 0.01f;

                // 阶段二：英雄停下，看字潮是否真的扑上来
                SetHeroInput(em, float2.zero);
                float before = AverageDistanceToHero(em);

                for (int i = Frames + 1; i <= Frames + ConvergeFrames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                float after = AverageDistanceToHero(em);
                float finalGap = MinDistanceToHero(em);
                int closeCount = CountWithinRadius(em, CloseRadius);
                float msPerFrame = stopwatch.ElapsedMilliseconds / (float)(Frames + ConvergeFrames);

                bool pass = spawned == config.EnemyCount
                            && !float.IsNaN(before)
                            && !float.IsNaN(after)
                            && spawnInnerGap >= config.SpawnInnerRadius - 0.5f
                            && spawnMinPair > 0.05f
                            && after < before - 5f
                            && finalGap < CloseRadius
                            && closeCount >= 1
                            && bounded;

                Debug.Log($"[SmokeTest] 生成敌人 {spawned}/{config.EnemyCount}");
                Debug.Log($"[SmokeTest] 起始最近敌人距离 {spawnInnerGap:F2}（应 >= 内径 {config.SpawnInnerRadius}）");
                Debug.Log($"[SmokeTest] 起始最近两个敌人间距 {spawnMinPair:F3}（应 > 0.05，防止「全部叠在同一点」）");
                Debug.Log($"[SmokeTest] 英雄最终位置 ({heroPos.x:F2}, {heroPos.y:F2})，" +
                          $"边界 ±{config.FieldSize}，{(bounded ? "被拦在场内" : "跑出场地")}");
                Debug.Log($"[SmokeTest] 英雄停下后：到英雄平均距离 {before:F2} -> {after:F2}（{ConvergeFrames} 帧），" +
                          $"最近敌人 {finalGap:F2}");
                Debug.Log($"[SmokeTest] 贴身（< {CloseRadius}m）敌人数 {closeCount}——" +
                          $"绕圈式追击这个数会是 0，直扑才会大于 0");
                Debug.Log($"[SmokeTest] 单帧逻辑耗时 {msPerFrame:F2} ms（Editor 未开 Burst 的上限值，非真实性能）");
                Debug.Log(pass
                    ? $"[SmokeTest] PASS：环形起步、无重叠、英雄被边界拦住、停下后字潮扑到 {CloseRadius} 米内"
                    : "[SmokeTest] FAIL：见上方数值定位问题");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SmokeTest] 异常：{e}");
            }
            finally
            {
                world.Dispose();
            }
        }

        private static void SetHeroInput(EntityManager em, float2 move)
        {
            var query = em.CreateEntityQuery(typeof(HeroInput), typeof(HeroTag));
            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new HeroInput { Move = move });
            }

            entities.Dispose();
            query.Dispose();
        }

        private static float2 ReadHeroPosition(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(HeroTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            float2 result = positions.Length > 0 ? positions[0].Value : float2.zero;
            positions.Dispose();
            query.Dispose();
            return result;
        }

        private static float AverageDistanceToHero(EntityManager em)
        {
            return DistanceToHero(em, average: true);
        }

        private static float MinDistanceToHero(EntityManager em)
        {
            return DistanceToHero(em, average: false);
        }

        /// <summary>
        /// 任意两个敌人的最小间距。这条断言存在的理由：
        /// 曾经因为 Random 结构体按值传参，1000 个单位全生成在同一个坐标上，
        /// 而当时所有断言（数量 / 非 NaN / 距离下降）全都能通过。
        /// </summary>
        private static float MinPairwiseDistance(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            float min = float.MaxValue;
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = i + 1; j < positions.Length; j++)
                {
                    float d = math.distance(positions[i].Value, positions[j].Value);
                    min = math.min(min, d);
                }
            }

            int count = positions.Length;
            positions.Dispose();
            query.Dispose();
            return count < 2 ? float.NaN : min;
        }

        /// <summary>敌人到英雄的距离统计；出现任何 NaN/Inf 直接返回 NaN，让判据失败。</summary>
        private static float DistanceToHero(EntityManager em, bool average)
        {
            float2 hero = ReadHeroPosition(em);

            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            int count = positions.Length;

            double sum = 0d;
            float min = float.MaxValue;
            bool bad = false;
            for (int i = 0; i < count; i++)
            {
                float d = math.distance(positions[i].Value, hero);
                if (float.IsNaN(d) || float.IsInfinity(d))
                {
                    bad = true;
                    break;
                }

                sum += d;
                min = math.min(min, d);
            }

            positions.Dispose();
            query.Dispose();

            if (bad || count == 0)
            {
                return float.NaN;
            }

            return average ? (float)(sum / count) : min;
        }

        /// <summary>统计真正咬到英雄的敌人数（距离 &lt; radius）。</summary>
        private static int CountWithinRadius(EntityManager em, float radius)
        {
            float2 hero = ReadHeroPosition(em);
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (math.distance(positions[i].Value, hero) < radius)
                {
                    count++;
                }
            }

            positions.Dispose();
            query.Dispose();
            return count;
        }

        /// <summary>
        /// 配置来源。优先读 ScaleConfig.asset——自检跑的必须是游戏里真用的那套数值，
        /// 否则「自检过了」不代表「游戏里对」（曾经这里硬编码 SeparationRadius=0.8，
        /// 而实际运行是 1.15，自检验的根本不是真实配置）。读不到才回退内置值，并打警告。
        /// </summary>
        private static GameConfig LoadConfig()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScaleConfig>(
                "Assets/_HanziRogue/Data/ScaleConfig.asset");

            if (asset == null)
            {
                Debug.LogWarning("[SmokeTest] 读不到 ScaleConfig.asset，回退内置默认值——" +
                                 "此时自检数值可能与游戏实际运行不一致。");
                return new GameConfig
                {
                    EnemyCount = 1000,
                    FieldSize = 50f,
                    SpawnInnerRadius = 8f,
                    HeroSpeed = 14f,
                    EnemySpeed = 8f,
                    CellSize = 3f,
                    SeparationRadius = 1.15f,
                    NeighborRadius = 2.5f,
                    SeparationWeight = 1.6f,
                    CohesionWeight = 0f,
                    AlignmentWeight = 0f,
                    SeekWeight = 2.2f,
                    TurnRate = 5f,
                    SpeedVariance = 0.12f
                };
            }

            return new GameConfig
            {
                EnemyCount = asset.EnemyCount,
                FieldSize = asset.FieldSize,
                SpawnInnerRadius = asset.SpawnInnerRadius,
                HeroSpeed = asset.HeroSpeed,
                EnemySpeed = asset.EnemySpeed,
                CellSize = asset.CellSize,
                SeparationRadius = asset.SeparationRadius,
                NeighborRadius = asset.NeighborRadius,
                SeparationWeight = asset.SeparationWeight,
                CohesionWeight = asset.CohesionWeight,
                AlignmentWeight = asset.AlignmentWeight,
                SeekWeight = asset.SeekWeight,
                TurnRate = asset.TurnRate,
                SpeedVariance = asset.SpeedVariance
            };
        }
    }
}
