using UnityEngine;

namespace HanziRogue.Core
{
    /// <summary>
    /// 战斗手感配置。宪法 §2.8：任何进入代码的数值都必须能回答「为什么是这个数」。
    /// 每个字段都把理由写在 Tooltip 里——Inspector 上悬停就能看到，不需要回头翻文档。
    /// 没想清楚的一律标 [PLACEHOLDER] 并写清验证路径（见 01-治理/数值预算表.md §5）。
    ///
    /// 与 <see cref="ScaleConfig"/> 的分野：那份管「规模与集群」，这份管「打人与挨打」。
    /// </summary>
    [CreateAssetMenu(menuName = "HanziRogue/Combat Config", fileName = "CombatConfig")]
    public class CombatConfig : ScriptableObject
    {
        [Header("英雄")]
        [Tooltip("英雄最大血量。用户指定：100 → 200（2026-10-10 翻倍）。")]
        public float HeroMaxHealth = 200f;

        [Tooltip("单次突刺伤害。用户指定：5（配合兵 10 血 = 两下一个）。")]
        public float HeroAttackDamage = 5f;

        [Tooltip("枪尖到英雄中心的距离（米）。**必须等于「武器」节点在突刺帧能伸到的最远端**" +
                 "（HeroWeaponView.thrustDistance + 刀身半长），否则会出现「刀压着字却不掉血」。\n" +
                 "当前对齐：thrustDistance 2.2 + 半长 1.4 = 3.6。[PLACEHOLDER]")]
        public float HeroReach = 3.6f;

        [Tooltip("攻击胶囊半宽（米）。取 1.1 = 一个「兵」字的完整宽度：\n" +
                 "兵停在 1.5 米环上、彼此间隔约 1.15 米，半宽 0.55 时判定带只覆盖 ±20°，" +
                 "字潮是离散密堆的，经常整条带里一个字都没有（实测击杀 0）。长枪就该扫到整条字列。[PLACEHOLDER]")]
        public float HeroAttackHalfWidth = 1.1f;

        [Tooltip("贴身半径（米）：进到这个距离内的兵不看方向一律算被碰到。\n" +
                 "兵停在 StopRadius（1.5）处，而「英雄」二字半宽 1.5——这些兵本来就画在英雄身上，" +
                 "只认正前方等于让贴着你的字免疫。取 1.8（> StopRadius，< 2.0 以保住「背后不挨打」的不变式）。")]
        public float HeroPointBlankRadius = 1.8f;

        [Tooltip("收枪蓄势时长（秒）。看不见这一段就没有「刺」的力量感，只有「字凭空掉血」。\n" +
                 "[PLACEHOLDER]")]
        public float HeroStartupTime = 0.10f;

        [Tooltip("收枪时长（秒）。它决定连戳的最短间隔（本作没有额外 CD，" +
                 "循环周期 = Startup + Recover）。0.20 约为每秒 3 下。[PLACEHOLDER]")]
        public float HeroRecoverTime = 0.20f;

        [Tooltip("单次攻击最多命中几个目标。枪是「穿」不是「扫」，超过 3 个就变成近战aoe了。\n" +
                 "枪变长、判定变宽后取 3：一串字扎穿三个才看得出「穿」。[PLACEHOLDER]")]
        public int HeroPierceCap = 3;

        [Tooltip("出枪期间英雄的移速系数。0 = 出枪时定住（很难受），1 = 出枪照常全速（看不出在出枪）。\n" +
                 "[PLACEHOLDER] 手感对照 0.4 / 0.6 / 1.0。")]
        public float HeroMoveScaleWhileAttacking = 0.6f;

        [Header("兵")]
        [Tooltip("兵的最大血量。用户指定：10。")]
        public float EnemyMaxHealth = 10f;

        [Tooltip("兵一次攻击的伤害。用户未指定，取 1：贴身约 10 个兵 → 约 10 dps → 100 血撑 10 秒。" +
                 "这是「被围住会死但不至于秒死」的量。[PLACEHOLDER]")]
        public float EnemyAttackDamage = 1f;

        [Tooltip("同一个兵两次出手的间隔（秒）。与上面的伤害共同决定「DPS = 伤害/间隔 × 贴身人数」。\n" +
                 "[PLACEHOLDER]")]
        public float EnemyAttackInterval = 1.0f;

        [Tooltip("兵能够到英雄的距离（米）。\n" +
                 "**必须大于 ScaleConfig.StopRadius（当前 1.5）**——兵停在 StopRadius 就不动了，" +
                 "攻击距离小于它等于永远打不到人。取 2.0 是给留给集群行为的一点抖动余量。[PLACEHOLDER]")]
        public float EnemyAttackRange = 2.0f;

        [Header("表现")]
        [Tooltip("受击染色的持续时间（秒）。闪白（兵）与闪红（英雄）共用。\n" +
                 "从 0.12 提到 0.16：0.12 在 60fps 下只有 7 帧，混在 1000 个字里肉眼几乎抓不住，\n" +
                 "玩家会得出「没打中」的结论。闪白是这套战斗唯一的即时反馈，宁可长一点。[PLACEHOLDER]")]
        public float HitFlashDuration = 0.16f;
    }
}
