using Unity.Entities;

namespace HanziRogue.Core
{
    /// <summary>
    /// 全局运行时配置。由 BattleSession 从 ScaleConfig (ScriptableObject) 拷贝而来。
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

        /// <summary>
        /// 空间网格格子边长。由 ScaleConfig 从 `SeparationRadius × CellSizeFactor` 推导（ADR-0011），
        /// **不再手填**。硬约束：(1 + 0.5) × CellSize ≥ SeparationRadius，否则 3×3 扫描会漏邻居。
        /// </summary>
        public float CellSize;

        /// <summary>空间网格覆盖的世界半边长（米）。比 FieldSize 大一圈，容纳被挤出场地边缘的字。</summary>
        public float GridHalfExtent;

        /// <summary>字间距（米）。由位置层硬约束保证，不再走速度层的分离力。</summary>
        public float SeparationRadius;
        public float NeighborRadius;
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

        /// <summary>
        /// 贴身停驻半径：兵的中心进入这个距离就完全停住，不再推进。
        /// 没有它，追击会退化成绕圈——转向半径 = 速度 / 转向率 = 8/5 = 1.6 米，
        /// 兵冲过英雄后掉不了头，只能在英雄边上画圆（见 ADR-0010）。
        /// </summary>
        public float StopRadius;

        /// <summary>抵达减速区外缘。进入这个距离开始线性收速，到 StopRadius 收为 0。</summary>
        public float ArriveRadius;

        /// <summary>
        /// 英雄碰撞硬核半径（米）：兵中心绝对不许进到这个距离以内。
        /// 必须明显小于 StopRadius——两者之间那段是位置解算的活动空间。
        /// 压成同一个值会死锁：内层兵被外层挤压、推力合力向内，
        /// 一帧就被投影拉回环上，越挤越糊（实测 254 个兵叠在 2 米内、字间距 0.00）。
        /// </summary>
        public float HeroCoreRadius;
    }
}
