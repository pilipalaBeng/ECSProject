using Unity.Entities;

namespace HanziRogue.Core
{
    /// <summary>英雄标记。用于输入系统与渲染层筛选。</summary>
    public struct HeroTag : IComponentData
    {
    }

    /// <summary>敌人（字潮单位）标记。</summary>
    public struct EnemyTag : IComponentData
    {
    }
}
