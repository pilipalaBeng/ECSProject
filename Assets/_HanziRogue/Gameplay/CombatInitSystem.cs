using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 战斗组件的懒初始化。
    ///
    /// 为什么需要它：<c>EnemySpawnSystem</c> 的职责是「把单位摆到场上」，
    /// 让它顺带填血量会把 <see cref="CombatBalance"/> 变成它的必需依赖——
    /// 于是所有只用 <see cref="GameConfig"/> 的旧自检世界（批量 qemu 里那种）会因为缺配置而空转。
    /// 初始化放在自己的 System 里，缺 <see cref="CombatBalance"/> 时它根本不跑（<c>RequireForUpdate</c>），
    /// 而不是让整条战斗链因为拿不到配置而报错——两种缺配置的表现，前者才是能排查的。
    ///
    /// 判据用「<see cref="Health.Max"/> ≤ 0」表示「还没初始化」：这是一个**哨兵**，
    /// 不是魔法数字——它表达的含义是「血量系统的合法值永远 > 0」。
    /// 初始化完成后每个实体只多一次比较，十万实体量级约 0.02 ms/帧，可以忽略；
    /// 真到要抠这一点的那天，先给生成加一个 NeedsCombatInit 空 tag 做零成本过滤（那时再写）。
    ///
    /// 排序约束有三条，缺一条都会出事：
    /// ① **晚于 <see cref="EnemySpawnSystem"/>**：同帧生成、同帧初始化，中间不留空窗。
    /// ② 早于所有会扣血的系统。
    /// 这两条合起来才是完整的——只满足②的话，生成系统可能排在本系统之后，
    /// 于是「实体刚建出来、血量还是 0」的那一帧照样会被结算系统看到并判死。
    /// 那个 bug 的实际表现是开局第一帧英雄即死、<see cref="BattleProgress.HeroDead"/> 当场置位，
    /// 整条战斗链随之冻结，看起来像「攻击功能没实现」——排查方向会完全跑偏。
    /// </summary>
    [UpdateAfter(typeof(EnemySpawnSystem))]
    [UpdateBefore(typeof(HeroAttackSystem))]
    [UpdateBefore(typeof(EnemyAttackSystem))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct CombatInitSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CombatBalance>();
        }

        public void OnUpdate(ref SystemState state)
        {
            CombatBalance balance = SystemAPI.GetSingleton<CombatBalance>();

            JobHandle heroHandle = new InitHeroCombatJob
            {
                MaxHealth = math.max(1f, balance.HeroMaxHealth)
            }.ScheduleParallel(state.Dependency);

            JobHandle enemyHandle = new InitEnemyCombatJob
            {
                MaxHealth = math.max(1f, balance.EnemyMaxHealth),
                AttackInterval = math.max(0.001f, balance.EnemyAttackInterval)
            }.ScheduleParallel(heroHandle);

            state.Dependency = enemyHandle;
        }
    }

    /// <summary>英雄初始化：血量拉满，朝向给一个确定的初值。</summary>
    [BurstCompile]
    public partial struct InitHeroCombatJob : IJobEntity
    {
        public float MaxHealth;

        public void Execute(Entity entity, ref Health health, ref Facing2D facing, in HeroTag tag)
        {
            if (!CombatRules.IsInitialized(in health))
            {
                health.Max = MaxHealth;
                health.Value = MaxHealth;
            }

            // 朝向零向量会让攻击没有方向。兜底朝 +X，顺便让第一刀永远可见。
            if (math.lengthsq(facing.Value) < 1e-6f)
            {
                facing.Value = new float2(1f, 0f);
            }
        }
    }

    /// <summary>
    /// 兵初始化：血量拉满 + **把出手冷却错峰**。
    /// 错峰的理由不是「看起来随机」，是数值：全场冷却同相位时，十几个兵会在同一帧齐射，
    /// 英雄血条是一跳一跳的锯齿；错峰之后每秒的掉血变成连续的小口，读得清也更公平。
    /// 相位由实体序号决定，所以同一种子下每次运行结果一致（自检可复现）。
    /// </summary>
    [BurstCompile]
    public partial struct InitEnemyCombatJob : IJobEntity
    {
        public float MaxHealth;
        public float AttackInterval;

        public void Execute(Entity entity, ref Health health, ref EnemyAttackState attack, in EnemyTag tag)
        {
            if (!CombatRules.IsInitialized(in health))
            {
                health.Max = MaxHealth;
                health.Value = MaxHealth;

                var rnd = Random.CreateFromIndex((uint)entity.Index);
                attack.Cooldown = rnd.NextFloat(0f, AttackInterval);
            }
        }
    }
}
