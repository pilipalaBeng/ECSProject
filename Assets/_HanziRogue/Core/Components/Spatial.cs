using Unity.Entities;
using Unity.Mathematics;

namespace HanziRogue.Core
{
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

    /// <summary>
    /// 空间哈希网格里存的邻居快照：位置 + 速度。Boids 三条规则都要用。
    /// 它不是 IComponentData——只是一份放入 NativeParallelMultiHashMap 的 payload，
    /// 与 SpatialHash 配套（哈希出 key，本结构作 value）。
    /// Index 必须有：它不是可选元数据，而是「这不是我自己」的唯一凭据。
    /// 曾经只有 Pos/Vel，结果每个兵在邻居互推时把自己也算成一个邻居——
    /// 网格里存的是帧初位置，计算时用的是帧末位置，两者相差本帧位移约 0.13 米，
    /// 于是自己给自己加了一记朝向自己运动方向的推力。
    /// 单帧推力上限只有半个字间距，这笔伪推力把预算吃干，
    /// 真正的邻居互推全被限幅砍掉，字间距怎么调都糊。
    /// </summary>
    public struct NeighborData
    {
        public float2 Pos;
        public float2 Vel;
        public int Index;
    }
}
