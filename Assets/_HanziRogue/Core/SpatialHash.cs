using Unity.Mathematics;

namespace HanziRogue.Core
{
    /// <summary>
    /// 空间哈希工具。宪法 C1.8：邻近查询必须走空间分区，禁止 O(n^2) 全量遍历。
    /// 格子边长略大于感知半径时，邻域查询只需扫 3x3 共 9 格，复杂度 O(n*k)。
    /// </summary>
    public static class SpatialHash
    {
        /// <summary>大质数散列，避免负坐标与坐标分布不均导致的冲突。</summary>
        public static int CellKey(int2 cell)
        {
            return cell.x * 73856093 ^ cell.y * 19349663;
        }

        public static int2 CellOf(float2 pos, float cellSize)
        {
            return new int2(
                (int)math.floor(pos.x / cellSize),
                (int)math.floor(pos.y / cellSize));
        }
    }
}
