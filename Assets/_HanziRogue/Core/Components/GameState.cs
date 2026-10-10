using Unity.Entities;

namespace HanziRogue.Core
{
    /// <summary>
    /// 一局的进度 singleton。与 <see cref="GameConfig"/> 一样由 <c>BattleSession</c> 创建、随之销毁。
    ///
    /// <c>SpawnDone</c> 存在的理由很实在：<c>EnemySpawnSystem</c> 原本用「场上有没有兵」
    /// 判断本局是否已生成。引入死亡之后，玩家杀光最后一个兵的那一帧，
    /// 系统会把空场当成「还没生成过」，当场再刷一整波出来。
    /// 「本局生成过没有」与「场上还剩几个」是两件事，不能互相替代。
    ///
    /// <c>HeroDead</c> 同理——英雄阵亡要冻结伤害结算，但不销毁英雄实体：
    /// 销毁会让所有依赖 <see cref="HeroTag"/> 的查询当场失效，system 依赖链跟着抖。
    /// </summary>
    public struct BattleProgress : IComponentData
    {
        public bool SpawnDone;
        public bool HeroDead;
        public int KillCount;
    }
}
