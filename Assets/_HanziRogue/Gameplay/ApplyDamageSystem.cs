using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 全工程**唯一**写 <see cref="Health"/> 的地方。
    ///
    /// 为什么必须独占：一旦出现第二处写血量，「吸血」「反伤」「护盾」「相克倍率」
    /// 这些跨伤口效果就都得在每个伤害源里各实现一遍，漏掉任何一处都是静默 bug
    /// （不报错，只是某个伤害来源不触发词条）。独占写入点是让后续系统能接进来的地基。
    ///
    /// 它只做四件事：扣血、置染色计时、判死、把死亡数交给进度 singleton。
    /// 伤害「是谁造成的」不由它关心——那是产生方的领域。
    ///
    /// 死亡用 EndSimulation 的命令缓冲销毁：job 里不允许做结构性变更，
    /// 而等到下一次 Render 之前销毁已经足够早，玩家看不出延迟。
    /// </summary>
    [UpdateBefore(typeof(HitFlashDecaySystem))]
    public partial struct ApplyDamageSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CombatBalance>();
            state.RequireForUpdate<BattleProgress>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            CombatBalance balance = SystemAPI.GetSingleton<CombatBalance>();

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
                .AsParallelWriter();

            if (!TryGetSingletonEntity(ref state, out Entity progressEntity))
            {
                return;
            }

            if (!TryGetHeroEntity(ref state, out Entity heroEntity))
            {
                return;
            }

            var deaths = new NativeArray<int>(1, Allocator.TempJob);

            JobHandle applyHandle = new ApplyPendingDamageJob
            {
                FlashDuration = math.max(0f, balance.HitFlashDuration),
                DeathCount = deaths,
                Commands = ecb,
                HeroTags = SystemAPI.GetComponentLookup<HeroTag>(true)
            }.ScheduleParallel(state.Dependency);

            JobHandle commitHandle = new CommitBattleProgressJob
            {
                DeathCount = deaths,
                Progress = SystemAPI.GetComponentLookup<BattleProgress>(),
                Healths = SystemAPI.GetComponentLookup<Health>(true),
                ProgressEntity = progressEntity,
                HeroEntity = heroEntity
            }.Schedule(applyHandle);

            state.Dependency = deaths.Dispose(commitHandle);
        }

        private bool TryGetHeroEntity(ref SystemState state, out Entity heroEntity)
        {
            heroEntity = Entity.Null;

            foreach (var (_, entity) in SystemAPI.Query<RefRO<HeroTag>>().WithEntityAccess())
            {
                heroEntity = entity;
            }

            return heroEntity != Entity.Null;
        }

        /// <summary>
        /// 取进度 singleton 实体。用具名查询而不是 <c>GetSingletonEntity&lt;T&gt;()</c>，
        /// 是为了在一局刚结束、实体正在被清的时候拿到 false 而不是抛异常——
        /// 那种时刻正是最容易看到满屏异常的时候。
        ///
        /// 查询不要 Dispose：<c>SystemAPI.QueryBuilder().Build()</c> 建出来的查询归系统所有，
        /// 手动 Dispose 会抛 <c>InvalidOperationException</c>。
        /// </summary>
        private bool TryGetSingletonEntity(ref SystemState state, out Entity entity)
        {
            entity = Entity.Null;

            EntityQuery query = SystemAPI.QueryBuilder().WithAll<BattleProgress>().Build();
            if (query.CalculateEntityCount() != 1)
            {
                return false;
            }

            entity = query.GetSingletonEntity();
            return true;
        }
    }

    /// <summary>
    /// 结算累加器。<c>pending.Amount</c> 每帧清零（不管有没有人打它），
    /// 这是「累加器」模型的关键纪律：读走即复位，避免上一帧的伤害被重复结算。
    /// </summary>
    [BurstCompile]
    public partial struct ApplyPendingDamageJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> DeathCount;
        public EntityCommandBuffer.ParallelWriter Commands;
        public float FlashDuration;

        /// <summary>
        /// 用来把英雄从「死亡即销毁」里摘出去。见 <see cref="Execute"/> 里的说明。
        /// </summary>
        [ReadOnly] public ComponentLookup<HeroTag> HeroTags;

        public void Execute(Entity entity, ref Health health, ref PendingDamage pending, ref HitFlash flash)
        {
            float amount = pending.Amount;
            pending.Amount = 0f;

            // 未初始化的实体（Max=0）一律不结算，也不判死。
            // 它们是「刚生成、还没轮到 CombatInitSystem 填血」的实体，血量 0 不代表死亡。
            // 少了这道守卫，开局第一帧英雄就会被判死——见 CombatRules.IsInitialized 的注释。
            if (amount <= 0f || !CombatRules.IsInitialized(in health))
            {
                return;
            }

            health.Value -= amount;
            if (health.Value < 0f)
            {
                health.Value = 0f;
            }

            flash.Timer = FlashDuration;

            if (health.Value > 0f)
            {
                return;
            }

            // 英雄**不销毁**，只把血量压到 0 并由 BattleProgress.HeroDead 冻结整条战斗链。
            // 销毁它的代价是对称性被打破：全场所有依赖 HeroTag 的查询（移动、朝向、攻击、
            // 若干个表现层读取）会在同一帧集体落空，相机跟随对象直接消失，
            // 画面跳一下不说，之后每一帧都要先判断「英雄还在不在」。
            // 留一个血量为 0 的英雄实体，比留一堆空引用判断便宜得多。
            if (HeroTags.HasComponent(entity))
            {
                return;
            }

            unsafe
            {
                int* deaths = (int*)DeathCount.GetUnsafePtr();
                Interlocked.Increment(ref deaths[0]);
            }

            // 排序键用实体序号而非 chunk 索引：同一实体永远得到同一个键，
            // 重放顺序稳定，避免「同一帧死亡顺序飘忽」让自检出现偶发差异。
            Commands.DestroyEntity(entity.Index, entity);
        }
    }

    /// <summary>
    /// 把死亡数与英雄存亡提交到 <see cref="BattleProgress"/>。
    /// 单线程 job——singleton 是全场唯一的，并行写它没有意义。
    /// </summary>
    [BurstCompile]
    public struct CommitBattleProgressJob : IJob
    {
        [ReadOnly] public NativeArray<int> DeathCount;
        public ComponentLookup<BattleProgress> Progress;
        [ReadOnly] public ComponentLookup<Health> Healths;
        public Entity ProgressEntity;
        public Entity HeroEntity;

        public void Execute()
        {
            BattleProgress progress = Progress[ProgressEntity];
            progress.KillCount += DeathCount[0];

            // 同样要过「已初始化」这道闸：英雄刚生成、血量还是 0 的那一帧不能被判死，
            // 否则 HeroDead 会在开局瞬间置位，整条战斗链还没开始就被永久冻结。
            if (HeroEntity != Entity.Null && Healths.HasComponent(HeroEntity))
            {
                Health heroHealth = Healths[HeroEntity];
                if (CombatRules.IsInitialized(in heroHealth) && heroHealth.Value <= 0f)
                {
                    progress.HeroDead = true;
                }
            }

            Progress[ProgressEntity] = progress;
        }
    }
}
