using Unity.Entities;
using Unity.Mathematics;

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

    /// <summary>战场平面坐标。XZ 平面映射到 float2，Y 恒为 0。</summary>
    public struct Position2D : IComponentData
    {
        public float2 Value;
    }

    public struct Velocity2D : IComponentData
    {
        public float2 Value;
    }

    public struct MoveSpeed : IComponentData
    {
        public float Value;
    }

    /// <summary>英雄输入意图。由 HeroInputSystem 每帧写入，HeroMoveSystem 消费。</summary>
    public struct HeroInput : IComponentData
    {
        public float2 Move;
    }

    /// <summary>
    /// 全局运行时配置。由 BattleBootstrap 从 ScaleConfig (ScriptableObject) 拷贝而来。
    /// 存在原因：System 必须保持 unmanaged，不能直接持有 UnityEngine.Object。
    /// </summary>
    public struct GameConfig : IComponentData
    {
        public int EnemyCount;

        /// <summary>竞技场半边长。英雄被约束在这个正方形内，敌人初始也铺在这个范围内。</summary>
        public float FieldSize;

        /// <summary>敌人初始生成环的内径。留出中心空地，字潮才会有「从外向内涌来」的观感。</summary>
        public float SpawnInnerRadius;

        public float HeroSpeed;
        public float EnemySpeed;
        public float CellSize;
        public float SeparationRadius;
        public float NeighborRadius;
        public float SeparationWeight;
        public float CohesionWeight;
        public float AlignmentWeight;
        public float SeekWeight;

        /// <summary>
        /// 转向速率（弧度/秒）。方向不是一帧掰到位，而是每秒最多转这么多。
        /// 没有它，玩家一移动，一千个字会集体抽搐式转向（见 ADR-0008）。
        /// </summary>
        public float TurnRate;

        /// <summary>
        /// 每个兵的速度随机幅度（±比例）。全场同速会像一块刚性板整体平移，
        /// 看不到「字潮」的层次；差异化后前排快、后排慢，自然形成浪。
        /// </summary>
        public float SpeedVariance;
    }

    /// <summary>空间哈希网格里存的邻居快照：位置 + 速度。Boids 三条规则都要用。</summary>
    public struct NeighborData
    {
        public float2 Pos;
        public float2 Vel;
    }
}
