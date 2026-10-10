using Unity.Entities;
using Unity.Mathematics;

namespace HanziRogue.Core
{
    /// <summary>
    /// 血量。<see cref="Value"/> 是 <c>float</c> 而不是 <c>int</c>——这不是随手选的：
    /// 宪法 §2.5 规定五行相克是唯一伤害规则，倍率 1.0 / 3.0 / 0.4 一旦接入，
    /// 「5 点伤害 × 0.4」得到的是 2、「7 点伤害 × 0.4」得到的是 2.8。
    /// 用 int 就得在接相克那一天改组件类型，而那会波及全部引用点（迁移成本远高于现在多占 4 字节）。
    /// </summary>
    public struct Health : IComponentData
    {
        public float Value;
        public float Max;
    }

    /// <summary>
    /// 本帧待结算的伤害累加器。结算完由 <c>ApplyDamageSystem</c> 清零。
    ///
    /// 为什么是「累加器」而不是事件队列：跨 System 共享 <c>NativeQueue</c> 需要额外的
    /// 生命周期管理与依赖排序（谁建谁销毁、谁先跑），而伤害源的目标集天然不相交——
    /// 英雄攻击只写兵的累加器，兵攻击只写英雄的累加器。
    /// 于是每个实体的累加器全局只有一个写入方，不需要原子操作，也不需要队列。
    ///
    /// **必须遵守的不变式**：任何新增的伤害系统都只能写一个「别人的」目标集，
    /// 两个 System 不得写同一个实体的 PendingDamage。若将来出现第三个伤害源，
    /// 先回来改这条不变式（那说明该上真正的事件总线了），不要就地绕过。
    /// </summary>
    public struct PendingDamage : IComponentData
    {
        public float Amount;
    }

    /// <summary>
    /// 受击后的染色剩余时间（秒）。表现层据此做闪白 / 闪红，
    /// 逻辑侧不解释它的含义——逻辑只负责把计时递减到 0。
    /// </summary>
    public struct HitFlash : IComponentData
    {
        public float Timer;
    }

    /// <summary>
    /// 英雄朝向（单位向量）。攻击判定的方向基准，武器动画 reads it too。
    /// 由 <c>HeroFacingSystem</c> 写；没有输入时保持上一帧的值。
    /// </summary>
    public struct Facing2D : IComponentData
    {
        public float2 Value;
    }

    /// <summary>英雄攻击状态机的当前相位与计时。</summary>
    public struct HeroAttackState : IComponentData
    {
        public byte Phase;
        public float Timer;
    }

    /// <summary>兵的攻击冷却剩余时间（秒）。归零且接触英雄时才能打出一击。</summary>
    public struct EnemyAttackState : IComponentData
    {
        public float Cooldown;
    }

    /// <summary>
    /// 战斗侧的共享判据。放在这里而不是散进各个 System，是因为这些判据一旦在两处写法不一致，
    /// 出的是**静默 bug**——不报错，只是某一帧的判定结果与另一帧不同。
    /// </summary>
    public static class CombatRules
    {
        /// <summary>
        /// 全局唯一的「已初始化」判据。<c>Max ≤ 0</c> 表示 <c>CombatInitSystem</c> 还没给它填过血量。
        ///
        /// 它不是魔法数字：血量系统的合法值恒 > 0，所以 0 是天然的「未赋值」哨兵。
        /// 必须在**三处一致地使用**，漏一处就会复现同一个 bug：
        /// ① 初始化（填血）；② 伤害结算（未初始化的实体不结算）；
        /// ③ 死亡判定（未初始化不等于死亡）。
        /// 曾因 ②③ 缺失导致开局第一帧英雄被判死、<c>BattleProgress.HeroDead</c> 当场置位，
        /// 于是整个战斗链从第一帧起就被冻结——表现出来是「攻击完全无效」，排查方向极易跑偏。
        /// </summary>
        public static bool IsInitialized(in Health health)
        {
            return health.Max > 0f;
        }
    }

    /// <summary>
    /// 攻击相位常量。用 <c>byte</c> 常量而不是 <c>enum</c>，
    /// 是为了让 job 里的比较免去装箱/转型噪音，也让序列化取值一目了然。
    /// </summary>
    public static class AttackPhase
    {
        public const byte Idle = 0;

        /// <summary>收枪蓄势。看得见这一段才有「刺出去」的力量感。</summary>
        public const byte Startup = 1;

        /// <summary>突刺帧。只存在一帧——命中判定就发生在这一帧，天然幂等。</summary>
        public const byte Active = 2;

        /// <summary>收枪。它的长度决定连戳节奏。</summary>
        public const byte Recover = 3;
    }
}
