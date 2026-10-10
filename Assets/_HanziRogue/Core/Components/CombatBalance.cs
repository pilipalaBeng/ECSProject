using Unity.Entities;

namespace HanziRogue.Core
{
    /// <summary>
    /// 战斗数值的运行时副本。由 <c>BattleSession</c> 从 <c>CombatConfig</c>（ScriptableObject）拷贝而来。
    /// 存在理由同 <see cref="GameConfig"/>：System 必须保持 unmanaged，不能持有 UnityEngine.Object。
    ///
    /// 为什么不并进 <see cref="GameConfig"/>：那份是「规模与集群手感」的载体，已经有 16 个字段；
    /// 战斗数值是另一个关注点（还会随着词条系统继续长），混在一起会让它变成一个配置垃圾桶。
    ///
    /// 所有取值的时间单位统一为秒，距离单位统一为米——跨系统的单位不一致是这个项目踩过的坑。
    /// </summary>
    public struct CombatBalance : IComponentData
    {
        public float HeroMaxHealth;

        public float HeroAttackDamage;

        /// <summary>枪尖到英雄中心的距离（米）。</summary>
        public float HeroReach;

        /// <summary>攻击胶囊的半宽（米）。约等于半个字宽，保证一枪主要扎到一个字。</summary>
        public float HeroAttackHalfWidth;

        /// <summary>
        /// 贴身半径（米）：进到这个距离内的兵**不看方向**一律算被碰到。
        ///
        /// 为什么必须有这一条：兵停在 <c>ScaleConfig.StopRadius</c>（1.5 米）处，
        /// 而「英雄」二字半宽就是 1.5 米——那些兵本来就画在英雄身上。
        /// 只认正前方会让「刀明明压在字上却不掉血」，这是 2026-10-10 实测击杀数为 0 的直接原因。
        /// 上限受集群数值约束：**必须小于 StopRadius + 一个字宽**，否则等于开了全向 AoE。
        /// </summary>
        public float HeroPointBlankRadius;

        public float HeroStartupTime;
        public float HeroRecoverTime;

        /// <summary>单次攻击最多命中几个目标。枪刺的「穿」，不是横扫。</summary>
        public int HeroPierceCap;

        /// <summary>出枪期间英雄的移速系数。停在原地出枪很难受，全速出枪又看不出在出枪。</summary>
        public float HeroMoveScaleWhileAttacking;

        public float EnemyMaxHealth;
        public float EnemyAttackDamage;

        /// <summary>同一个兵两次出手之间的间隔（秒）。</summary>
        public float EnemyAttackInterval;

        /// <summary>
        /// 兵能够到英雄的距离（米）。**必须大于 ScaleConfig.StopRadius（1.5）**——
        /// 兵在 StopRadius 处就停下了，攻击距离小于它等于永远打不到人。
        /// 这个跨域耦合是故意的：集群的两 location 值只有一份主源，这里只做「够得着」的余量。
        /// </summary>
        public float EnemyAttackRange;

        /// <summary>受击染色的持续时间（秒）。闪白与闪红共用。</summary>
        public float HitFlashDuration;
    }
}
