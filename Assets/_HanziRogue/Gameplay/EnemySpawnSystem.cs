using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 生成英雄与兵集群。等 GameConfig singleton 就位后执行一次。
    /// 排序：必须早于 HeroMoveSystem——首帧就得有英雄实体，移动与追击才有对象。
    ///
    /// **生成判据刻意留了两个版本**：
    /// ① 正常一局：看 <see cref="BattleProgress.SpawnDone"/>；
    /// ② 只有 <see cref="GameConfig"/> 的世界（早期的 BoidsSmokeTest 那类）：退回旧的「场上有没有兵」。
    ///
    /// 为什么不一步切成 ①：那会让所有只建了 GameConfig 的测试世界整套空转，
    /// 属于把迁移成本甩给别人。**引入死亡之后②在真实游戏里是错的**——杀光最后一波会重刷——
    /// 所以正常路径永远走 ①，② 只是旧测试的逃生口。
    ///
    /// 血量等战斗数值不在这里写：那是 <c>CombatInitSystem</c> 的活，
    /// 这样本 System 不需要 <see cref="CombatBalance"/> 就能工作。
    /// </summary>
    [UpdateBefore(typeof(HeroMoveSystem))]
    public partial struct EnemySpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!ShouldSpawn(ref state, out Entity progressEntity))
            {
                return;
            }

            GameConfig config = SystemAPI.GetSingleton<GameConfig>();
            EntityManager em = state.EntityManager;

            SpawnHero(em, config);
            SpawnEnemies(em, config);

            if (progressEntity == Entity.Null)
            {
                return;
            }

            BattleProgress progress = em.GetComponentData<BattleProgress>(progressEntity);
            progress.SpawnDone = true;
            em.SetComponentData(progressEntity, progress);
        }

        /// <summary>
        /// 本局是否已经生成过。两种判据的分支在这里，别处不要各写一份。
        ///
        /// 两条踩过的坑，改动前请先读完：
        /// ① 它是**实例方法**：<c>SystemAPI</c> 的源码生成器不允许在 static 方法里用（EA0006）。
        /// ② 这里的查询**不能 Dispose**。<c>SystemAPI.QueryBuilder().Build()</c> 走的是
        /// <c>state.GetEntityQuery()</c>，查出来的是系统持有的那份——手动 Dispose 会抛
        /// <c>InvalidOperationException</c>，而这个异常发生在生成之前，
        /// 表现是「场上一个实体都没有」却没有任何编译错误。
        /// （<c>EntityManager.CreateEntityQuery</c> 建的查询才是自己管，两边规矩相反。）
        /// </summary>
        private bool ShouldSpawn(ref SystemState state, out Entity progressEntity)
        {
            progressEntity = Entity.Null;
            EntityManager em = state.EntityManager;

            EntityQuery progressQuery = SystemAPI.QueryBuilder().WithAll<BattleProgress>().Build();
            bool hasProgress = progressQuery.CalculateEntityCount() == 1;
            if (hasProgress)
            {
                progressEntity = progressQuery.GetSingletonEntity();
            }

            if (hasProgress)
            {
                return !em.GetComponentData<BattleProgress>(progressEntity).SpawnDone;
            }

            return SystemAPI.QueryBuilder().WithAll<EnemyTag>().Build().CalculateEntityCount() == 0;
        }

        private static void SpawnHero(EntityManager em, in GameConfig config)
        {
            EntityArchetype archetype = em.CreateArchetype(
                typeof(Position2D), typeof(Velocity2D), typeof(MoveSpeed),
                typeof(HeroTag), typeof(HeroIntent), typeof(Facing2D), typeof(HeroAttackState),
                typeof(Health), typeof(PendingDamage), typeof(HitFlash));

            Entity hero = em.CreateEntity(archetype);
            em.SetComponentData(hero, new Position2D { Value = float2.zero });
            em.SetComponentData(hero, new Velocity2D { Value = float2.zero });
            em.SetComponentData(hero, new MoveSpeed { Value = config.HeroSpeed });
            em.SetComponentData(hero, new HeroIntent { Move = float2.zero, Attack = false });
            em.SetComponentData(hero, new Facing2D { Value = new float2(1f, 0f) });
            em.SetComponentData(hero, new HeroAttackState { Phase = AttackPhase.Idle, Timer = 0f });
            em.SetComponentData(hero, new Health { Value = 0f, Max = 0f });
            em.SetComponentData(hero, new PendingDamage { Amount = 0f });
            em.SetComponentData(hero, new HitFlash { Timer = 0f });
        }

        private static void SpawnEnemies(EntityManager em, in GameConfig config)
        {
            EntityArchetype archetype = em.CreateArchetype(
                typeof(Position2D), typeof(Velocity2D), typeof(MoveSpeed), typeof(EnemyTag),
                typeof(Health), typeof(PendingDamage), typeof(HitFlash), typeof(EnemyAttackState));

            int count = math.max(1, config.EnemyCount);
            NativeArray<Entity> entities = em.CreateEntity(archetype, count, Allocator.Temp);

            var rnd = new Random(20261007u);
            float inner = math.max(0f, config.SpawnInnerRadius);
            float outer = math.max(inner + 0.1f, config.FieldSize);
            float innerSq = inner * inner;
            float outerSq = outer * outer;

            float variance = math.clamp(config.SpeedVariance, 0f, 0.5f);

            for (int i = 0; i < entities.Length; i++)
            {
                float speed = config.EnemySpeed * (1f + rnd.NextFloat(-variance, variance));
                em.SetComponentData(entities[i], new Position2D { Value = SampleAnnulus(ref rnd, innerSq, outerSq) });
                em.SetComponentData(entities[i], new Velocity2D { Value = float2.zero });
                em.SetComponentData(entities[i], new MoveSpeed { Value = speed });
                em.SetComponentData(entities[i], new Health { Value = 0f, Max = 0f });
                em.SetComponentData(entities[i], new PendingDamage { Amount = 0f });
                em.SetComponentData(entities[i], new HitFlash { Timer = 0f });
                em.SetComponentData(entities[i], new EnemyAttackState { Cooldown = 0f });
            }

            entities.Dispose();
        }

        /// <summary>
        /// 在环形区域内取样。半径开方是为了让分布在面积上均匀——
        /// 直接对半径做线性取样会把单位挤到内圈，字潮一开始就糊成一团。
        /// rnd 必须按引用传：Unity.Mathematics.Random 是 struct，
        /// 按值传会把状态拷贝出去，每次调用都从同一起点取数，结果是 1000 个单位叠在同一点。
        /// </summary>
        private static float2 SampleAnnulus(ref Random rnd, float innerSq, float outerSq)
        {
            float angle = rnd.NextFloat(0f, math.PI * 2f);
            float radius = math.sqrt(rnd.NextFloat(innerSq, outerSq));
            return new float2(math.cos(angle), math.sin(angle)) * radius;
        }
    }
}
