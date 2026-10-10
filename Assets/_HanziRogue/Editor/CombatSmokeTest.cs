using System;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using HanziRogue.Core;
using HanziRogue.Gameplay;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 战斗闭环自检。与 <c>BoidsSmokeTest</c> 的分野：那个验「追得上、贴得住」，这个验「打得疼、死得掉」。
    ///
    /// 为什么必须有一个：<c>AddComponentData</c> 之类的问题在 Editor 里不报错，
    /// 而「杀光最后一个兵会重刷一整波」「一刀结算两次」这类 bug 只在特定时刻出现，
    /// 靠手玩十次不一定撞得上。这里把每条不变式钉成断言。
    ///
    /// 复用结论（来自 BoidsSmokeTest 的踩坑）：结论一律**走返回值**（<see cref="RunAndReport"/>），
    /// 不要指望从控制台捞——MCP 通道读不到 info 级日志。
    /// </summary>
    public static class CombatSmokeTest
    {
        private const float DeltaTime = 1f / 60f;
        private const string ScaleConfigPath = "Assets/_HanziRogue/Data/ScaleConfig.asset";
        private const string CombatConfigPath = "Assets/_HanziRogue/Data/CombatConfig.asset";

        /// <summary>按一次攻击键并保持这么多帧：足够跨过 Startup 进入 Active，又不够触发第二次。</summary>
        private const int AttackHoldFrames = 8;

        private delegate bool TestCase(out string detail);

        [MenuItem("HanziRogue/Combat Smoke Test")]
        public static void Run()
        {
            Debug.Log(RunAndReport());
        }

        public static string RunAndReport()
        {
            var report = new StringBuilder();
            int passed = 0;
            int failed = 0;

            report.AppendLine("[CombatSmokeTest] 战斗闭环自检");
            report.AppendLine(EnvironmentProbe());

            Check(report, "1 初始化：英雄 200 血 / 兵 10 血", InitHealth, ref passed, ref failed);
            Check(report, "2 突刺：正前方的兵掉血、背后的不掉", ThrustHitsFrontOnly, ref passed, ref failed);
            Check(report, "3 穿透上限：一枪最多命中 HeroPierceCap 个", PierceCap, ref passed, ref failed);
            Check(report, "4 两下致死：10 血 / 5 伤 → 第二下死亡", TwoHitsKill, ref passed, ref failed);
            Check(report, "5 兵反击：贴身的兵扣英雄血", EnemyCounterAttack, ref passed, ref failed);
            Check(report, "6 阵亡：英雄血量归零后 HeroDead 置位", HeroDeath, ref passed, ref failed);
            Check(report, "7 击杀计数 + 杀光后不重刷", KillCountAndNoRespawn, ref passed, ref failed);
            Check(report, "8 闪白：受击后置位，0.25 秒后归零", HitFlashDecay, ref passed, ref failed);
            Check(report, "9 贴身：贴身半径内的兵不看方向也挨打", PointBlankIgnoresFacing, ref passed, ref failed);

            report.AppendLine($"[CombatSmokeTest] PASS {passed} / FAIL {failed}");
            return report.ToString();
        }

        // ---------- 用例 ----------

        private static bool InitHealth(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                Step(world, 1, 1);

                float heroMax = ReadHeroHealth(em).Max;
                float enemyMax = ReadFirstEnemyHealth(em).Max;
                bool ok = Mathf.Approximately(heroMax, 200f) && Mathf.Approximately(enemyMax, 10f);

                detail = $"英雄 Max={heroMax:F0}（期望 200），兵 Max={enemyMax:F0}（期望 10），" +
                         $"实体数 英雄 {CountHeroes(em)} / 兵 {CountEnemies(em)}";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool ThrustHitsFrontOnly(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                PlaceEnemies(em, new float2(2.0f, 0f), new float2(-2.0f, 0f));
                SetHeroIntent(em, true);

                Step(world, AttackHoldFrames, 1);
                SetHeroIntent(em, false);
                Step(world, 2, AttackHoldFrames + 1);

                var healths = ReadEnemyHealths(em);
                float front = healths[0].Value;
                float back = healths[1].Value;
                bool ok = Mathf.Approximately(front, 5f) && Mathf.Approximately(back, 10f);

                detail = $"前方兵 {front:F1}（期望 5），背后兵 {back:F1}（期望 10，背后不该挨打）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool PierceCap(out string detail)
        {
            World world = CreateCombatWorld(8, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                // 五个兵一字排在枪线上，全都在攻击距离内
                PlaceEnemies(em,
                    new float2(1.0f, 0f), new float2(1.4f, 0f), new float2(1.8f, 0f),
                    new float2(2.2f, 0f), new float2(2.5f, 0f));

                SetHeroIntent(em, true);
                Step(world, AttackHoldFrames, 1);
                SetHeroIntent(em, false);
                Step(world, 2, AttackHoldFrames + 1);

                var healths = ReadEnemyHealths(em);
                int wounded = 0;
                for (int i = 0; i < 5 && i < healths.Length; i++)
                {
                    if (healths[i].Value < healths[i].Max)
                    {
                        wounded++;
                    }
                }

                bool ok = wounded == 3;
                detail = $"一枪命中 {wounded} 个（期望 3 = HeroPierceCap），枪线上共摆了 5 个";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool TwoHitsKill(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                PlaceEnemies(em, new float2(2.0f, 0f));

                int before = CountEnemies(em);

                SetHeroIntent(em, true);
                Step(world, AttackHoldFrames, 1);        // 第一下
                float afterFirst = ReadEnemyHealths(em)[0].Value;
                Step(world, 20, AttackHoldFrames + 1);   // 第二下（一个完整循环 18 帧）
                SetHeroIntent(em, false);
                Step(world, 2, AttackHoldFrames + 21);

                int after = CountEnemies(em);
                int kills = ReadProgress(em).KillCount;
                bool ok = Mathf.Approximately(afterFirst, 5f) && after == before - 1 && kills == 1;

                detail = $"第一下后剩 {afterFirst:F1} 血（期望 5），实体 {before} → {after}（期望 -1），" +
                         $"击杀计数 {kills}（期望 1）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool EnemyCounterAttack(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                // 三个兵贴在 1.0 米处：在 EnemyAttackRange（2.0）内，够得着英雄
                PlaceEnemies(em, new float2(1.0f, 0f), new float2(0f, 1.0f), new float2(-1.0f, 0f));

                float before = ReadHeroHealth(em).Value;
                Step(world, 90, 1);
                float after = ReadHeroHealth(em).Value;

                bool ok = after < before;
                detail = $"英雄血量 {before:F1} → {after:F1}（期望变少，贴身三个兵每秒一击）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool HeroDeath(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                PlaceEnemies(em, new float2(1.0f, 0f), new float2(0f, 1.0f));

                Entity hero = FindHero(em);
                Health health = em.GetComponentData<Health>(hero);
                health.Value = 2f;
                em.SetComponentData(hero, health);

                Step(world, 120, 1);

                BattleProgress progress = ReadProgress(em);
                float finalHealth = ReadHeroHealth(em).Value;
                bool ok = progress.HeroDead && finalHealth <= 0f;

                detail = $"HeroDead={progress.HeroDead}（期望 True），英雄血量 {finalHealth:F1}（期望 0）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool KillCountAndNoRespawn(out string detail)
        {
            World world = CreateCombatWorld(6, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                var enemies = EnemyEntities(em);
                int total = enemies.Length;

                // 直接灌满待结算伤害，一步把所有兵打死——这里验的是结算与清场，不是命中判定
                for (int i = 0; i < enemies.Length; i++)
                {
                    em.SetComponentData(enemies[i], new PendingDamage { Amount = 999f });
                }

                Step(world, 2, 1);
                int afterKilling = CountEnemies(em);
                int kills = ReadProgress(em).KillCount;

                // 关键断言：杀光之后再跑半秒，场上不该又冒出一批
                Step(world, 30, 3);
                int afterIdle = CountEnemies(em);

                bool ok = afterKilling == 0 && kills == total && afterIdle == 0;
                detail = $"杀光后实体 {afterKilling}（期望 0），击杀 {kills}（期望 {total}），" +
                         $"空场再跑 30 帧后 {afterIdle}（期望 0 —— 这条专门抓「杀光即重刷」）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        private static bool HitFlashDecay(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                PlaceEnemies(em, new float2(2.0f, 0f));

                SetHeroIntent(em, true);
                Step(world, AttackHoldFrames, 1);
                SetHeroIntent(em, false);

                float lit = ReadEnemyFlashes(em)[0].Timer;
                Step(world, 20, AttackHoldFrames + 1);
                float faded = ReadEnemyFlashes(em)[0].Timer;

                bool ok = lit > 0f && faded <= 0f;
                detail = $"受击瞬间 {lit:F3} 秒（期望 > 0），0.33 秒后 {faded:F3}（期望 0）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        /// <summary>
        /// 贴身判定：贴到身上的兵**不看方向**照样挨打。
        ///
        /// 这条断言存在的理由就是它抓到过的那个 bug：兵停在 StopRadius（1.5 米）处，
        /// 而「英雄」二字半宽 1.5 米——它们本来就画在英雄身上，却因为不在朝向上而完全免疫。
        /// 表现出来是「武器碰到小兵却不掉血」，整局击杀 0（2026-10-10 实测）。
        /// 站住不动时朝向由 HeroFacingSystem 保持为初始的 (1,0)，所以背后那个 1.2 米的兵
        /// 是纯靠贴身圆命中的——它掉血才说明这条规则生效。
        /// </summary>
        private static bool PointBlankIgnoresFacing(out string detail)
        {
            World world = CreateCombatWorld(4, out EntityManager em);
            try
            {
                ParkAllEnemies(em);
                // 一前一后各一个，都在贴身半径（1.8）内、都在「英雄」二字底下
                PlaceEnemies(em, new float2(-1.2f, 0f), new float2(1.2f, 0f));

                SetHeroIntent(em, true);
                Step(world, AttackHoldFrames, 1);
                SetHeroIntent(em, false);
                Step(world, 2, AttackHoldFrames + 1);

                var healths = ReadEnemyHealths(em);
                float behind = healths[0].Value;
                float front = healths[1].Value;
                bool ok = Mathf.Approximately(behind, 5f) && Mathf.Approximately(front, 5f);

                detail = $"贴身 1.2 米：身后兵 {behind:F1}、身前兵 {front:F1}（都期望 5，" +
                         "身后那个只能靠贴身圆命中）";
                return ok;
            }
            finally
            {
                world.Dispose();
            }
        }

        // ---------- 基础设施 ----------
        private static void Check(StringBuilder report, string name, TestCase test, ref int passed, ref int failed)
        {
            bool ok;
            string detail;

            // 一条用例自己抛异常时不能让整份报告陪葬：八条断言里只要有一条的底子没搭好
            // （实体没生成、组件没初始化），后面每一条都会以同一种方式炸，
            // 而你看不到第一条的失败原因，就只能从最后一条的堆栈反推——那是本末倒置。
            try
            {
                ok = test(out detail);
            }
            catch (Exception ex)
            {
                ok = false;
                detail = $"用例自身抛异常：{ex.GetType().Name} {ex.Message}";
            }

            if (ok)
            {
                passed++;
            }
            else
            {
                failed++;
            }

            report.AppendLine($"{(ok ? "PASS" : "FAIL")} {name} —— {detail}");
        }

        /// <summary>
        /// 环境探针：只挂 <c>EnemySpawnSystem</c> 的最小世界，先看「实体生没生成」。
        ///
        /// 为什么单独探一次：八条断言共用同一个建世界的函数，只要生成没发生，
        /// 后面每一条都会以「数组索引越界」的形式崩，真正的失败原因（生成系统没跑）
        /// 被埋在最后一条的堆栈里，排查时会先去怀疑命中判定——方向直接错了。
        /// 把「世界搭起来没有」和「战斗逻辑对不对」分开报，是这份自检能自我诊断的前提。
        /// </summary>
        private static string EnvironmentProbe()
        {
            var sb = new StringBuilder();
            var world = new World("CombatProbe");
            try
            {
                EntityManager em = world.EntityManager;
                Entity config = em.CreateEntity(typeof(GameConfig));
                em.SetComponentData(config, new GameConfig
                {
                    EnemyCount = 2,
                    FieldSize = 50f,
                    SpawnInnerRadius = 8f,
                    HeroSpeed = 14f,
                    EnemySpeed = 8f
                });

                var group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
                group.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpawnSystem>());

                int topLevel = 0;
                foreach (var handle in world.Systems)
                {
                    topLevel++;
                }

                world.SetTime(new Unity.Core.TimeData(0f, DeltaTime));
                world.Update();

                sb.Append($"[env] 只挂生成系统：英雄 {CountHeroes(em)} / 兵 {CountEnemies(em)}；")
                  .Append($"World 顶层系统 {topLevel}，组内托管系统 {group.ManagedSystems.Count}；")
                  .Append($"GameConfig 实体 {CountOf(em, typeof(GameConfig))}");
            }
            catch (Exception ex)
            {
                sb.Append($"[env] 探针自身抛异常：{ex.GetType().Name} {ex.Message}");
            }
            finally
            {
                world.Dispose();
            }

            return sb.ToString();
        }

        private static int CountOf(EntityManager em, ComponentType type)
        {
            EntityQuery query = em.CreateEntityQuery(type);
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }

        private static World CreateCombatWorld(int enemyCount, out EntityManager em)
        {
            var world = new World("CombatSmokeTest");
            em = world.EntityManager;

            ScaleConfig scale = LoadAssetOrDefault<ScaleConfig>(ScaleConfigPath);
            CombatConfig combat = LoadAssetOrDefault<CombatConfig>(CombatConfigPath);

            // 这里只填本自检真正用得到的字段：集群相关的系统（BoidsSystem）没挂，
            // 把整份 GameConfig 抄过来反而让人以为那些数值也在被测。
            Entity configEntity = em.CreateEntity(typeof(GameConfig));
            em.SetComponentData(configEntity, new GameConfig
            {
                EnemyCount = enemyCount,
                FieldSize = 50f,
                SpawnInnerRadius = 8f,
                HeroSpeed = 14f,
                EnemySpeed = 8f,
                SeparationRadius = scale.SeparationRadius,
                StopRadius = scale.StopRadius
            });

            Entity balanceEntity = em.CreateEntity(typeof(CombatBalance));
            em.SetComponentData(balanceEntity, new CombatBalance
            {
                HeroMaxHealth = combat.HeroMaxHealth,
                HeroAttackDamage = combat.HeroAttackDamage,
                HeroReach = combat.HeroReach,
                HeroAttackHalfWidth = combat.HeroAttackHalfWidth,
                HeroPointBlankRadius = combat.HeroPointBlankRadius,
                HeroStartupTime = combat.HeroStartupTime,
                HeroRecoverTime = combat.HeroRecoverTime,
                HeroPierceCap = combat.HeroPierceCap,
                HeroMoveScaleWhileAttacking = combat.HeroMoveScaleWhileAttacking,
                EnemyMaxHealth = combat.EnemyMaxHealth,
                EnemyAttackDamage = combat.EnemyAttackDamage,
                EnemyAttackInterval = combat.EnemyAttackInterval,
                EnemyAttackRange = combat.EnemyAttackRange,
                HitFlashDuration = combat.HitFlashDuration
            });

            em.CreateEntity(typeof(BattleProgress));

            var simulationGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            // 注意：本世界是**裁剪过**的，故意不挂 HeroMoveSystem / BoidsSystem。
            // 后果是 Entities 会丢掉 EnemySpawnSystem 的 [UpdateBefore(HeroMoveSystem)]
            // 并每次排序打一条「Ignoring invalid [UpdateBeforeAttribute]」——
            // 跑一次本自检刷 10 条，那是正常的，不是 bug（补挂 HeroMoveSystem 只会把
            // 警告挪到它 → BoidsSystem 那条上；补挂 BoidsSystem 又会把用例 3 里
            // 间距 0.4 米的五个兵用分离力推开，等于改了被测语义）。
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpawnSystem>());
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<CombatInitSystem>());
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<HeroFacingSystem>());
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<HeroAttackSystem>());
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<EnemyAttackSystem>());
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<ApplyDamageSystem>());
            simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<HitFlashDecaySystem>());

            // 死亡销毁靠它回放命令缓冲；不挂的话兵死了但实体还在，实体数断言会假失败
            simulationGroup.AddSystemToUpdateList(
                world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());

            world.SetTime(new Unity.Core.TimeData(0f, DeltaTime));
            world.Update();
            return world;
        }

        private static void Step(World world, int frames, int startFrame)
        {
            for (int i = 0; i < frames; i++)
            {
                int frame = startFrame + i;
                world.SetTime(new Unity.Core.TimeData(frame * DeltaTime, DeltaTime));
                world.Update();
            }
        }

        private static T LoadAssetOrDefault<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null ? asset : ScriptableObject.CreateInstance<T>();
        }

        private static NativeArray<Entity> EnemyEntities(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            query.Dispose();
            return entities;
        }

        private static void ParkAllEnemies(EntityManager em)
        {
            NativeArray<Entity> entities = EnemyEntities(em);
            var far = new Position2D { Value = new float2(500f, 500f) };
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], far);
            }

            entities.Dispose();
        }

        private static void PlaceEnemies(EntityManager em, params float2[] positions)
        {
            NativeArray<Entity> entities = EnemyEntities(em);
            for (int i = 0; i < positions.Length && i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new Position2D { Value = positions[i] });
            }

            entities.Dispose();
        }

        private static void SetHeroIntent(EntityManager em, bool attack)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(HeroIntent), typeof(HeroTag));
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new HeroIntent { Move = float2.zero, Attack = attack });
            }

            entities.Dispose();
            query.Dispose();
        }

        private static Entity FindHero(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(HeroTag));
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            Entity hero = entities.Length > 0 ? entities[0] : Entity.Null;
            entities.Dispose();
            query.Dispose();
            return hero;
        }

        private static Health ReadHeroHealth(EntityManager em)
        {
            Entity hero = FindHero(em);
            return hero == Entity.Null ? default : em.GetComponentData<Health>(hero);
        }

        private static int CountHeroes(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(HeroTag));
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }

        private static Health ReadFirstEnemyHealth(EntityManager em)
        {
            NativeArray<Health> healths = ReadEnemyHealths(em);
            return healths.Length > 0 ? healths[0] : default;
        }

        private static NativeArray<Health> ReadEnemyHealths(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(Health), typeof(EnemyTag));
            NativeArray<Health> healths = query.ToComponentDataArray<Health>(Allocator.Temp);
            query.Dispose();
            return healths;
        }

        private static NativeArray<HitFlash> ReadEnemyFlashes(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(HitFlash), typeof(EnemyTag));
            NativeArray<HitFlash> flashes = query.ToComponentDataArray<HitFlash>(Allocator.Temp);
            query.Dispose();
            return flashes;
        }

        private static BattleProgress ReadProgress(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(BattleProgress));
            NativeArray<BattleProgress> progresses = query.ToComponentDataArray<BattleProgress>(Allocator.Temp);
            BattleProgress progress = progresses.Length > 0 ? progresses[0] : default;
            progresses.Dispose();
            query.Dispose();
            return progress;
        }

        private static int CountEnemies(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(typeof(EnemyTag));
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }
    }
}
