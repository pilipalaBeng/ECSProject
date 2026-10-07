using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 敌人集群行为：Boids 三规则（分离 / 聚合 / 对齐）+ 追击英雄。
    /// 每帧两个 Job：先重建空间哈希网格，再做 3x3 邻域查询与受力积分。
    /// 宪法 C1.8：邻域查询走空间分区，禁止 O(n^2)。
    /// </summary>
    public partial struct BoidsSystem : ISystem
    {
        private NativeParallelMultiHashMap<int, NeighborData> _grid;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameConfig>();
            _grid = new NativeParallelMultiHashMap<int, NeighborData>(1024, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_grid.IsCreated)
            {
                _grid.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            GameConfig config = SystemAPI.GetSingleton<GameConfig>();
            float dt = SystemAPI.Time.DeltaTime;

            float2 heroPos = float2.zero;
            foreach (var pos in SystemAPI.Query<RefRO<Position2D>>().WithAll<HeroTag>())
            {
                heroPos = pos.ValueRO.Value;
            }

            _grid.Clear();
            int wanted = math.max(1024, config.EnemyCount * 2);
            if (_grid.Capacity < wanted)
            {
                _grid.Capacity = wanted;
            }

            JobHandle buildHandle = new BuildGridJob
            {
                Writer = _grid.AsParallelWriter(),
                CellSize = config.CellSize
            }.ScheduleParallel(state.Dependency);

            JobHandle boidsHandle = new BoidsJob
            {
                Grid = _grid,
                CellSize = config.CellSize,
                DeltaTime = dt,
                Target = heroPos,
                SeparationRadius = config.SeparationRadius,
                NeighborRadius = config.NeighborRadius,
                SeparationWeight = config.SeparationWeight,
                CohesionWeight = config.CohesionWeight,
                AlignmentWeight = config.AlignmentWeight,
                SeekWeight = config.SeekWeight,
                TurnRate = config.TurnRate
            }.ScheduleParallel(buildHandle);

            state.Dependency = new IntegrateJob
            {
                DeltaTime = dt
            }.ScheduleParallel(boidsHandle);
        }
    }

    /// <summary>把敌人位置与速度写入空间哈希网格，供下一步邻域查询使用。</summary>
    [BurstCompile]
    public partial struct BuildGridJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, NeighborData>.ParallelWriter Writer;
        public float CellSize;

        public void Execute(in Position2D pos, in Velocity2D vel, in EnemyTag tag)
        {
            int key = SpatialHash.CellKey(SpatialHash.CellOf(pos.Value, CellSize));
            Writer.Add(key, new NeighborData { Pos = pos.Value, Vel = vel.Value });
        }
    }

    /// <summary>
    /// 字潮移动：追击 + 分离，方向以有限转向速率逼近期望方向，速度直接设定。
    /// 与经典 Boids 的两处关键差别（ADR-0008）：
    /// ① 不做加速度积分（vel += accel*dt）。加速度积分没有阻尼，敌人会冲过头再绕回来，
    ///    观感是「绕着玩家盘旋」而不是「扑上来」。这里直接设 vel = dir * speed。
    /// ② 分离力不归一化，按 (1 - d/r) 线性衰减。归一化意味着「只要有一个邻居就全力推开」，会来回抖。
    /// 聚合与对齐的权重默认为 0，字段保留只是为了让 A/B 对比成为可能。
    /// </summary>
    [BurstCompile]
    public partial struct BoidsJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, NeighborData> Grid;
        public float CellSize;
        public float DeltaTime;
        public float2 Target;
        public float SeparationRadius;
        public float NeighborRadius;
        public float SeparationWeight;
        public float CohesionWeight;
        public float AlignmentWeight;
        public float SeekWeight;
        public float TurnRate;

        public void Execute(in Position2D pos, in MoveSpeed moveSpeed, ref Velocity2D vel, in EnemyTag tag)
        {
            int2 cell = SpatialHash.CellOf(pos.Value, CellSize);

            float2 separation = float2.zero;
            float2 cohesionSum = float2.zero;
            float2 alignmentSum = float2.zero;
            int neighborCount = 0;

            // 聚合/对齐默认关闭，此时连累加都跳过——不为零成本的东西付账
            bool needFlock = CohesionWeight > 0f || AlignmentWeight > 0f;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int key = SpatialHash.CellKey(cell + new int2(dx, dy));
                    if (!Grid.TryGetFirstValue(key, out NeighborData n, out var iterator))
                    {
                        continue;
                    }

                    do
                    {
                        float dist = math.distance(pos.Value, n.Pos);
                        if (dist < 1e-4f)
                        {
                            continue;
                        }

                        if (dist < SeparationRadius)
                        {
                            // 越近推得越狠，到半径边缘衰减为 0——不带衰减会抖
                            separation += (pos.Value - n.Pos) / dist * (1f - dist / SeparationRadius);
                        }

                        if (needFlock && dist < NeighborRadius)
                        {
                            cohesionSum += n.Pos;
                            alignmentSum += n.Vel;
                            neighborCount++;
                        }
                    }
                    while (Grid.TryGetNextValue(out n, ref iterator));
                }
            }

            float2 desired = math.normalizesafe(Target - pos.Value) * SeekWeight;
            desired += separation * SeparationWeight;

            if (needFlock && neighborCount > 0)
            {
                float2 center = cohesionSum / neighborCount;
                desired += math.normalizesafe(center - pos.Value) * CohesionWeight;
                desired += math.normalizesafe(alignmentSum / neighborCount) * AlignmentWeight;
            }

            if (math.lengthsq(desired) < 1e-6f)
            {
                vel.Value = float2.zero;
                return;
            }

            desired = math.normalize(desired);

            // 有限转向速率：从当前朝向朝 desired 转，每秒最多 TurnRate 弧度。
            // 没有这一步，玩家一动，一千个字会整体抽搐式转向。
            float2 current = math.lengthsq(vel.Value) > 1e-6f ? math.normalize(vel.Value) : desired;
            vel.Value = RotateTowards(current, desired, TurnRate * DeltaTime) * moveSpeed.Value;
        }

        /// <summary>把 from 朝 to 旋转，单次最多 maxRadians 弧度。用 2D 叉积的符号决定往哪边转。</summary>
        private static float2 RotateTowards(float2 from, float2 to, float maxRadians)
        {
            float angle = math.acos(math.clamp(math.dot(from, to), -1f, 1f));
            if (angle <= maxRadians || angle < 1e-5f)
            {
                return to;
            }

            float cross = from.x * to.y - from.y * to.x;
            float step = cross >= 0f ? maxRadians : -maxRadians;
            float s = math.sin(step);
            float c = math.cos(step);
            return new float2(from.x * c - from.y * s, from.x * s + from.y * c);
        }
    }

    /// <summary>速度积分到位置。与受力分离，便于后续加边界约束或击退。</summary>
    [BurstCompile]
    public partial struct IntegrateJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(ref Position2D pos, in Velocity2D vel, in EnemyTag tag)
        {
            pos.Value += vel.Value * DeltaTime;
        }
    }
}
