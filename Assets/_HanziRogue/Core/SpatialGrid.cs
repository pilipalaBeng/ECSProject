using System;
using Unity.Collections;
using Unity.Mathematics;

namespace HanziRogue.Core
{
    /// <summary>
    /// 排序网格（counting-sort uniform grid）。ADR-0011 的空间分区实现。
    ///
    /// 为什么不用 NativeParallelMultiHashMap（原实现）：
    /// 哈希表的邻域查询是**随机访存**，cache miss 率极高。Burst 优化的是算术与 SIMD，
    /// 救不了 cache miss——那是访问模式问题，不是算力问题。
    /// 实测推算（10 万实体 / 50m 场地）：每帧约 3.2 亿次哈希探查，帧时间崩到百毫秒量级。
    ///
    /// 本结构把「按格子分组」这件事做成**排序**而不是**散列**：
    /// 先数每格几个（GridCountJob，原子自增）→ 前缀和算出每格起点（GridPrefixSumJob）
    /// → 再按原子游标把实体填进连续槽位（GridFillJob）。
    /// 结果 Entries 里同一格子的实体**物理相邻**，邻域查询退化成区间顺序扫描。
    ///
    /// 关键：CellSize 必须由 SeparationRadius 推导（见 ScaleConfig.CellSizeFactor），
    /// 使 3×3 扫描刚好覆盖查询半径。这样「每实体候选邻居数」才与总体密度解耦。
    /// </summary>
    public struct SpatialGrid : IDisposable
    {
        /// <summary>每格实体区间起点。前缀和 job 写入；查询时与 CellCursor 配对形成 [start, end)。</summary>
        public NativeArray<int> CellStart;

        /// <summary>填充游标：初始等于 CellStart，GridFillJob 原子自增推进，填完后即为区间终点。</summary>
        public NativeArray<int> CellCursor;

        /// <summary>按格子连续排列的实体快照。复用 NeighborData（Pos / Vel / Index）。</summary>
        public NativeArray<NeighborData> Entries;

        /// <summary>格子 (0,0) 对应的世界格坐标（floor(pos / CellSize) 的下界）。</summary>
        public int2 Origin;

        /// <summary>网格维度（两轴等长，因为场地是正方形）。</summary>
        public int2 Dim;

        public float CellSize;

        /// <summary>Entries 的已分配容量。必须 ≥ 当帧实体数，否则填充会截断。</summary>
        public int Capacity;

        public bool IsCreated => CellStart.IsCreated;

        public int CellTotal => Dim.x * Dim.y;

        /// <summary>
        /// 由场地半边长与格子尺寸推导网格维度。
        /// 覆盖世界坐标 [-halfExtent, +halfExtent]，两端各多留一格防止边界实体被 clamp 挤压。
        /// </summary>
        public static void ComputeDims(float halfExtent, float cellSize, out int2 origin, out int2 dim)
        {
            float inv = 1f / cellSize;
            int lo = (int)math.floor(-halfExtent * inv) - 1;
            int hi = (int)math.floor(halfExtent * inv) + 1;
            origin = new int2(lo, lo);
            int n = math.max(1, hi - lo + 1);
            dim = new int2(n, n);
        }

        /// <summary>
        /// 世界坐标 → 格坐标（**不** clamp）。邻居扫描中心用它，因为越界格要跳过而不是挤到边缘。
        /// </summary>
        public static int2 CellCoordOf(float2 pos, int2 origin, float cellSize)
        {
            float inv = 1f / cellSize;
            return new int2(
                (int)math.floor(pos.x * inv) - origin.x,
                (int)math.floor(pos.y * inv) - origin.y);
        }

        /// <summary>
        /// 世界坐标 → 线性格下标（clamp 到网格内）。
        /// 实体归类用它：即使被位置解算推出场地边缘，也只落到最外圈格子，不会越界写坏内存。
        /// </summary>
        public static int CellOf(float2 pos, int2 origin, int2 dim, float cellSize)
        {
            int2 c = math.clamp(CellCoordOf(pos, origin, cellSize), int2.zero, dim - 1);
            return c.y * dim.x + c.x;
        }

        public static int CellIndexOf(int2 coord, int2 dim) => coord.y * dim.x + coord.x;

        /// <summary>
        /// 按场地尺寸与实体数申请/复用缓冲。形状不变则不重新分配（每帧调用是廉价的）。
        /// Entries 容量按 2 倍增长，避免规模微调时反复重分配。
        /// </summary>
        public void EnsureSize(float halfExtent, float cellSize, int requiredEntries)
        {
            ComputeDims(halfExtent, cellSize, out int2 origin, out int2 dim);

            bool sameShape = CellStart.IsCreated
                             && origin.Equals(Origin)
                             && dim.Equals(Dim)
                             && math.abs(CellSize - cellSize) < 1e-5f;

            if (!sameShape)
            {
                if (CellStart.IsCreated) CellStart.Dispose();
                if (CellCursor.IsCreated) CellCursor.Dispose();

                int cells = math.max(1, dim.x * dim.y);
                CellStart = new NativeArray<int>(cells, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                CellCursor = new NativeArray<int>(cells, Allocator.Persistent, NativeArrayOptions.ClearMemory);

                Origin = origin;
                Dim = dim;
                CellSize = cellSize;
            }

            if (Entries.IsCreated && Capacity >= requiredEntries)
            {
                return;
            }

            int newCapacity = math.max(1024, requiredEntries * 2);
            if (Entries.IsCreated)
            {
                Entries.Dispose();
            }

            Entries = new NativeArray<NeighborData>(
                newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Capacity = newCapacity;
        }

        public void Dispose()
        {
            if (CellStart.IsCreated) CellStart.Dispose();
            if (CellCursor.IsCreated) CellCursor.Dispose();
            if (Entries.IsCreated) Entries.Dispose();

            Dim = int2.zero;
            Capacity = 0;
        }
    }
}
