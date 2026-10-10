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
    /// 英雄攻击：推进三段相位状态机，并在 Active 那一帧结算命中的兵。
    ///
    /// 三段式的意义：<c>Startup</c>（收枪蓄势）让「刺出去」有力量感，
    /// <c>Active</c>（突刺帧）是唯一真正判定命中的一帧，
    /// <c>Recover</c>（收枪）决定连戳节奏——本作没有额外 CD，循环周期就是 Startup + Recover。
    ///
    /// **为什么命中不需要「边沿检测」**：Active 只存在一帧，下一帧相位已经离开，
    /// 所以每一刀天然只结算一次。如果当初把命中写成「每帧都判定」,就得额外维护
    /// 「这一刀打过谁」的集合来去重，那是自找的三倍的复杂度。
    ///
    /// 英雄读取走只读 ComponentLookup，写入方（相位 job）与读取方按依赖串成一条链，
    /// 因此不存在「读一半改一半」的竞争。
    /// </summary>
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct HeroAttackSystem : ISystem
    {
        /// <summary>
        /// 单次攻击的候选目标上限。超出部分直接丢弃：往英雄身上挤的兵再多，
        /// 一枪能穿透的也只是枪线穿过的一小段。**这个常量保护的是疯长的候选集，
        /// 不是玩法上限**——真正决定上限的是 <see cref="CombatBalance.HeroPierceCap"/>。
        /// </summary>
        private const int MaxHitCandidates = 64;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CombatBalance>();
            state.RequireForUpdate<BattleProgress>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 英雄阵亡后冻结攻击。实体不销毁（销毁会让依赖 HeroTag 的查询失效、依赖链抖动），
            // 只用 BattleProgress 这一个开关停掉一切伤害产生源。
            if (SystemAPI.GetSingleton<BattleProgress>().HeroDead)
            {
                return;
            }

            if (!TryGetHeroEntity(ref state, out Entity heroEntity))
            {
                return;
            }

            CombatBalance balance = SystemAPI.GetSingleton<CombatBalance>();
            float dt = SystemAPI.Time.DeltaTime;

            var candidates = new NativeArray<HitCandidate>(MaxHitCandidates, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var candidateCount = new NativeArray<int>(1, Allocator.TempJob);

            JobHandle phaseHandle = new HeroAttackPhaseJob
            {
                DeltaTime = dt,
                StartupTime = balance.HeroStartupTime,
                RecoverTime = balance.HeroRecoverTime
            }.Schedule(state.Dependency);

            // 命中判定自己读英雄的相位 / 位置 / 朝向，而不是由主线程问出来再传进来。
            // 主线程要知道「这帧进没进 Active」，就得先把相位 job 完成、形成一次同步点；
            // 让 job 通过只读 ComponentLookup 自己去拿，依赖链天然串起来，主线程零同步。
            JobHandle collectHandle = new CollectHeroHitsJob
            {
                Candidates = candidates,
                CandidateCount = candidateCount,
                MaxCandidates = MaxHitCandidates,
                HeroStates = SystemAPI.GetComponentLookup<HeroAttackState>(true),
                HeroPositions = SystemAPI.GetComponentLookup<Position2D>(true),
                HeroFacings = SystemAPI.GetComponentLookup<Facing2D>(true),
                HeroEntity = heroEntity,
                Reach = balance.HeroReach,
                HalfWidth = balance.HeroAttackHalfWidth,
                PointBlankRadiusSq = PointBlankRadiusSq(balance.HeroPointBlankRadius)
            }.ScheduleParallel(phaseHandle);

            JobHandle applyHandle = new ApplyHeroHitsJob
            {
                Candidates = candidates,
                CandidateCount = candidateCount,
                PendingDamages = SystemAPI.GetComponentLookup<PendingDamage>(),
                Damage = balance.HeroAttackDamage,
                PierceCap = math.max(1, balance.HeroPierceCap)
            }.Schedule(collectHandle);

            // 两个临时缓冲要等消费 job 跑完才释放，交给 Dispose(jobHandle) 串进依赖链。
            JobHandle disposeHandle = candidates.Dispose(applyHandle);
            state.Dependency = candidateCount.Dispose(disposeHandle);
        }

        /// <summary>
        /// 贴身半径的平方。负值一律当 0（= 关掉贴身判定），而不是让 <c>lengthsq</c> 去比一个负数——
        /// 那种写法不会报错，只会让判定悄悄失效，是最难查的一类。
        /// </summary>
        private static float PointBlankRadiusSq(float radius)
        {
            float clamped = math.max(0f, radius);
            return clamped * clamped;
        }

        /// <summary>
        /// 取出唯一的英雄实体。没有就直接 return —— 例如一局刚结束、实体还在清。
        /// 必须写成实例方法：<c>SystemAPI</c> 的源码生成器只认 OnUpdate 所在的那条调用链，
        /// 放在 <c>static</c> 里会直接报 EA0006（这是编译期错误，不是警告）。
        /// </summary>
        private bool TryGetHeroEntity(ref SystemState state, out Entity heroEntity)
        {
            heroEntity = Entity.Null;

            foreach (var (_, entity) in SystemAPI.Query<RefRO<HeroTag>>().WithEntityAccess())
            {
                heroEntity = entity;
            }

            return heroEntity != Entity.Null;
        }
    }

    /// <summary>
    /// 一次命中候选。按「离英雄的距离」排序，最近的先挨扎。
    /// 用直线距离而不是「沿枪线的投影」：贴身那一档本来就不分方向，
    /// 投影会出现负值，混进排序里会让背后的字抢到最前面的槽位。
    /// </summary>
    public struct HitCandidate
    {
        public Entity Target;
        public float Distance;
    }

    /// <summary>
    /// 推进攻击相位。一次 Execute 最多跨一个相位：
    /// 用 if/else-if 而不是 while(dt)，否则掉帧时一帧会连穿多个相位、看起来像凭空连挥两下。
    /// </summary>
    [BurstCompile]
    public partial struct HeroAttackPhaseJob : IJobEntity
    {
        public float DeltaTime;
        public float StartupTime;
        public float RecoverTime;

        public void Execute(in HeroIntent intent, ref HeroAttackState attack)
        {
            if (attack.Phase == AttackPhase.Idle)
            {
                if (intent.Attack)
                {
                    attack.Phase = AttackPhase.Startup;
                    attack.Timer = 0f;
                }

                return;
            }

            attack.Timer += DeltaTime;

            if (attack.Phase == AttackPhase.Startup)
            {
                if (attack.Timer >= StartupTime)
                {
                    attack.Phase = AttackPhase.Active;
                    attack.Timer = 0f;
                }

                return;
            }

            if (attack.Phase == AttackPhase.Active)
            {
                attack.Phase = AttackPhase.Recover;
                attack.Timer = 0f;
                return;
            }

            if (attack.Timer >= RecoverTime)
            {
                attack.Phase = AttackPhase.Idle;
                attack.Timer = 0f;
            }
        }
    }

    /// <summary>
    /// 收集枪线上的兵。命中形状是**两段的**：
    ///
    /// ① 「枪身段」：从英雄中心沿朝向延伸 <c>Reach</c> 米、半宽 <c>HalfWidth</c> 的胶囊。
    ///    先把敌人位置投影到朝向上（<c>along</c>），再看它到这条射线的垂距。
    ///    用胶囊而不是扇形，是因为枪是「一条线」而不是「一片面」。
    /// ② 「贴身段」：圆心在英雄、半径 <c>PointBlankRadius</c> 的圆，**不看方向**。
    ///
    /// 为什么必须有第二段：兵停在 <c>ScaleConfig.StopRadius</c>（1.5 米）处，
    /// 而「英雄」二字半宽就是 1.5 米——这些兵本来就画在英雄身上。
    /// 只留 ① 的时候，明明压在字上的兵因为不在朝向上而完全免疫：
    /// 2026-10-10 实测整局击杀数为 0，玩家看到的是「武器碰到小兵却不掉血」。
    /// 贴身段的物理直觉也站得住：枪托、枪身、胳膊都在那个半径里。
    ///
    /// why-per-entity-check-phase: 相位判断被复制到了每个实体上（而不是主线程判一次再决定要不要调度），
    /// 这是为了让主线程不必等相位 job 完成。分支是常量折叠友好的，代价远小于一次同步点。
    /// </summary>
    [BurstCompile]
    public partial struct CollectHeroHitsJob : IJobEntity
    {
        // 这两个数组在「任意下标」被写：槽位由原子游标分配，与实体自身下标无关，
        // 必须关掉按下标的并行限制，否则每帧抛 IndexOutOfRangeException（本项目的老坑，见 ADR-0011 §4）。
        [NativeDisableParallelForRestriction] public NativeArray<HitCandidate> Candidates;
        [NativeDisableParallelForRestriction] public NativeArray<int> CandidateCount;

        [ReadOnly] public ComponentLookup<HeroAttackState> HeroStates;
        [ReadOnly] public ComponentLookup<Position2D> HeroPositions;
        [ReadOnly] public ComponentLookup<Facing2D> HeroFacings;

        public Entity HeroEntity;
        public int MaxCandidates;
        public float Reach;
        public float HalfWidth;

        /// <summary>贴身半径的平方。0 = 关掉贴身判定。</summary>
        public float PointBlankRadiusSq;

        public void Execute(Entity entity, in Position2D pos, in EnemyTag tag)
        {
            if (HeroStates[HeroEntity].Phase != AttackPhase.Active)
            {
                return;
            }

            float2 origin = HeroPositions[HeroEntity].Value;
            float2 facing = math.normalizesafe(HeroFacings[HeroEntity].Value, new float2(1f, 0f));
            float2 toEnemy = pos.Value - origin;

            if (!IsHittable(toEnemy, facing))
            {
                return;
            }

            int slot;
            unsafe
            {
                int* counter = (int*)CandidateCount.GetUnsafePtr();
                slot = Interlocked.Increment(ref counter[0]) - 1;
            }

            // 候选集满了就丢弃。这是「保护越来越挤的字潮」的安全阀，不是玩法限制。
            if (slot >= MaxCandidates)
            {
                return;
            }

            Candidates[slot] = new HitCandidate
            {
                Target = entity,
                Distance = math.length(toEnemy)
            };
        }

        /// <summary>贴身圆 ∪ 枪身胶囊。两段是「或」的关系，满足任一即命中。</summary>
        private bool IsHittable(float2 toEnemy, float2 facing)
        {
            if (math.lengthsq(toEnemy) <= PointBlankRadiusSq)
            {
                return true;
            }

            float along = math.dot(toEnemy, facing);
            if (along < 0f || along > Reach)
            {
                return false;
            }

            float2 perpendicular = toEnemy - facing * along;
            return math.lengthsq(perpendicular) <= HalfWidth * HalfWidth;
        }
    }

    /// <summary>
    /// 从候选里挑出最近的 <c>PierceCap</c> 个扣血。
    /// 单线程 job：它写 <see cref="PendingDamage"/>，而这是唯一一个会写别人组件的伤害入口。
    /// 只选最近的几个，是为了保留「枪扎出去串了一串」而不是「一枪扫掉一整排」的观感。
    /// </summary>
    [BurstCompile]
    public struct ApplyHeroHitsJob : IJob
    {
        [NativeDisableParallelForRestriction] public NativeArray<HitCandidate> Candidates;
        [ReadOnly] public NativeArray<int> CandidateCount;
        public ComponentLookup<PendingDamage> PendingDamages;
        public float Damage;
        public int PierceCap;

        public void Execute()
        {
            int total = math.min(CandidateCount[0], Candidates.Length);
            int limit = math.min(PierceCap, total);
            if (limit <= 0)
            {
                return;
            }

            // 选择排序：只挑前 PierceCap 小的，不必整体排序。
            for (int picked = 0; picked < limit; picked++)
            {
                int best = picked;
                for (int i = picked + 1; i < total; i++)
                {
                    if (Candidates[i].Distance < Candidates[best].Distance)
                    {
                        best = i;
                    }
                }

                Swap(picked, best);
                WriteCandidate(picked);
            }
        }

        private void WriteCandidate(int index)
        {
            Entity target = Candidates[index].Target;
            if (!PendingDamages.HasComponent(target))
            {
                return;
            }

            PendingDamage pending = PendingDamages[target];
            pending.Amount += Damage;
            PendingDamages[target] = pending;
        }

        private void Swap(int a, int b)
        {
            HitCandidate tmp = Candidates[a];
            Candidates[a] = Candidates[b];
            Candidates[b] = tmp;
        }
    }
}
