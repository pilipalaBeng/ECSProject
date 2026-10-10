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
    /// 兵的反击：够得着英雄且冷却到了，就打出一击。
    ///
    /// 为什么用「计数 × 伤害」而不是「每个兵各写一记伤害」：
    /// 成千个兵同时写英雄一个实体的组件是并行写冲突，要用 Interlocked 做浮点原子加，
    /// 那是把简单问题换成难问题。而本作所有兵的伤害是同一个数值，
    /// 「有几个兵打中了」的整数计数用 Interlocked 原子自增就够了，最后乘一次即可。
    ///
    /// **代价要写清楚**：这个写法隐含「所有兵伤害相同」。将来要做「精锐兵伤害更高」，
    /// 要么给它单独一个系统，要么回到这里改成多计数槽（不是无解，只是那时才值得付这笔复杂度）。
    /// </summary>
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct EnemyAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CombatBalance>();
            state.RequireForUpdate<BattleProgress>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<BattleProgress>().HeroDead)
            {
                return;
            }

            if (!TryGetHero(ref state, out Entity heroEntity, out float2 heroPosition))
            {
                return;
            }

            CombatBalance balance = SystemAPI.GetSingleton<CombatBalance>();
            float attackRange = math.max(0f, balance.EnemyAttackRange);

            var landings = new NativeArray<int>(1, Allocator.TempJob);

            JobHandle contactHandle = new EnemyContactAttackJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                HeroPosition = heroPosition,
                RangeSq = attackRange * attackRange,
                Interval = math.max(0.001f, balance.EnemyAttackInterval),
                LandingCount = landings
            }.ScheduleParallel(state.Dependency);

            JobHandle heroHandle = new ApplyHeroDamageJob
            {
                LandingCount = landings,
                Damage = balance.EnemyAttackDamage,
                PendingDamages = SystemAPI.GetComponentLookup<PendingDamage>(),
                HeroEntity = heroEntity
            }.Schedule(contactHandle);

            state.Dependency = landings.Dispose(heroHandle);
        }

        /// <summary>
        /// 取英雄实体与本帧位置。用 <c>SystemAPI.Query</c> 遍历而不是 <c>EntityManager.GetComponentData</c>——
        /// 后者是一次主线程随机读，此时的进场 job 可能还在飞；Query 会先把依赖接上再读。
        /// 英雄只有一个，遍历成本就是一次解压 chunk 首元素的代价。
        /// </summary>
        private bool TryGetHero(ref SystemState state, out Entity heroEntity, out float2 heroPosition)
        {
            heroEntity = Entity.Null;
            heroPosition = float2.zero;

            foreach (var (pos, entity) in SystemAPI.Query<RefRO<Position2D>>().WithAll<HeroTag>().WithEntityAccess())
            {
                heroEntity = entity;
                heroPosition = pos.ValueRO.Value;
            }

            return heroEntity != Entity.Null;
        }
    }

    /// <summary>
    /// 每个兵各自维护自己的出手冷却，出手时把全局计数 +1。
    /// 冷却是**错峰初始化**的（见 <c>CombatInitSystem</c>）——全场同相位的话，
    /// 十几个兵会整齐地在同一帧扣一次血，血条是一跳一跳的锯齿，而不是连续掉血。
    /// </summary>
    [BurstCompile]
    public partial struct EnemyContactAttackJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> LandingCount;

        public float DeltaTime;
        public float2 HeroPosition;
        public float RangeSq;
        public float Interval;

        public void Execute(ref EnemyAttackState attack, in Position2D pos, in EnemyTag tag)
        {
            float cooldown = math.max(0f, attack.Cooldown - DeltaTime);

            if (cooldown <= 0f && math.distancesq(pos.Value, HeroPosition) <= RangeSq)
            {
                cooldown = Interval;

                unsafe
                {
                    int* count = (int*)LandingCount.GetUnsafePtr();
                    Interlocked.Increment(ref count[0]);
                }
            }

            attack.Cooldown = cooldown;
        }
    }

    /// <summary>把本帧「打中了几下」折算成英雄待结算的伤害。单线程，因为它是英雄血的唯一入口。</summary>
    [BurstCompile]
    public struct ApplyHeroDamageJob : IJob
    {
        [ReadOnly] public NativeArray<int> LandingCount;
        public ComponentLookup<PendingDamage> PendingDamages;
        public Entity HeroEntity;
        public float Damage;

        public void Execute()
        {
            int hits = LandingCount[0];
            if (hits <= 0 || !PendingDamages.HasComponent(HeroEntity))
            {
                return;
            }

            PendingDamage pending = PendingDamages[HeroEntity];
            pending.Amount += Damage * hits;
            PendingDamages[HeroEntity] = pending;
        }
    }
}
