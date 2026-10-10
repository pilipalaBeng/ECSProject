namespace HanziRogue.Core
{
    /// <summary>
    /// 游戏模式。局内（Battle）与局外（Meta）是同一个场景的两种模式，不是两个场景。
    ///
    /// 判据：一个场景 = 一个 ECS World 的生命周期。两者共用同一个常驻 World，
    /// 所以本质是同一场景的两种形态。用两个普通 Scene 去表示它们，等于拿一把量不了 ECS 的尺子
    /// 去量 ECS 状态——场景卸载了、实体还在，「局内局外独立」这个前提根本不成立（ADR-0012）。
    /// </summary>
    public enum GameMode
    {
        /// <summary>局外：入口、构筑、图鉴。此模式下不生成战斗实体。</summary>
        Meta,

        /// <summary>局内：字潮战斗。进入时由 BattleSession 建立本局实体。</summary>
        Battle
    }
}
