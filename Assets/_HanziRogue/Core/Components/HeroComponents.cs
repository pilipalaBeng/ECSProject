using Unity.Entities;
using Unity.Mathematics;

namespace HanziRogue.Core
{
    /// <summary>
    /// 英雄这一帧的意图。由 <c>HeroIntentSystem</c> 每帧写入，后续多个 System 消费：
    /// 移动消费 <see cref="Move"/>，朝向消费 <see cref="Move"/>，攻击消费 <see cref="Attack"/>。
    ///
    /// 为什么是一个组件而不是三个：它们描述的是同一件事——「玩家这帧想让英雄干什么」。
    /// 拆成 Move / Attack 三个小组件会让每次加一个新意图（闪避、切字）都多一次查询与一次 seek，
    /// 而它们的生命周期完全一致。**边界画在「来源」上而不是「字段」上。**
    /// </summary>
    public struct HeroIntent : IComponentData
    {
        public float2 Move;

        /// <summary>是否按住攻击键。持续按住 = 连续突刺（拖动感的自然表达）。</summary>
        public bool Attack;
    }
}
