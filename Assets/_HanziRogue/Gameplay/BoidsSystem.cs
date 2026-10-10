using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 字潮行为。每帧六个段落，按顺序依赖：
    /// ① GridClearJob —— 清零格子计数缓冲；
    /// ② GridCountJob —— 并行数出每格几个实体（原子自增）；
    /// ③ GridPrefixSumJob —— 前缀和算出每格起点，并拷一份填充游标；
    /// ④ GridFillJob —— 并行按原子游标把实体填进连续槽位；
    /// ⑤ BoidsJob —— 算期望方向与速度。**只做「往哪走」**：抵达系数 × 局部密度节流；
    /// ⑥ IntegrateJob → ResolveOverlapJob —— 速度积分到位置，再做位置层硬约束（邻居互推 + 英雄硬核投影）。
    ///
    /// 区间 ①②③④ 是 ADR-0011 的「排序网格」：把「按格子分组」做成排序而不是散列，
    /// 让同一格子的实体在内存里物理相邻。邻域查询因此退化成连续区间扫描，没有哈希随机访存。
    ///
    /// 「分离」这条经典 Boids 规则不在速度层：软约束在高密度下会被压穿（实测最近 0.03 米），
    /// 间距交给 ⑥ 的位置层硬约束（ADR-0010）。聚合 / 对齐默认权重 0，字段保留只为 A/B 对比。
    /// 宪法 C1.8：邻域查询走空间分区，禁止 O(n^2)。
    /// </summary>
    public partial struct BoidsSystem : ISystem
    {
        private SpatialGrid _grid;

        /// <summary>
        /// 每帧位置解算的松弛遍数。
        /// 两遍近似 Gauss-Seidel——第二遍读到的已经是别人更新过又写回的位置，
        /// 能在同一帧里把接触链上的压力往外多传一层。
        /// 1 遍也能过自检，但质量差一档：贴身区最小间距 0.85 米 vs 1.13 米（设定值 1.15），
        /// 英雄静止两秒的漂移 0.72 米 vs 0.18 米。多这一遍买的就是「字不糊」和「咬死不动」。
        /// </summary>
        private const int SolveIterations = 2;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameConfig>();
        }

        public void OnDestroy(ref SystemState state)
        {
            _grid.Dispose();
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

            // 容量按实际实体数申请，不用 config.EnemyCount——将来加了击杀/增援，
            // 实际数会偏离配置值，而 Capacity 一旦偏小，填充就会被截断（邻域漏邻居）。
            int enemyCount = SystemAPI.QueryBuilder().WithAll<EnemyTag>().Build().CalculateEntityCount();
            _grid.EnsureSize(config.GridHalfExtent, config.CellSize, enemyCount);

            // ① 清零 + ② 计数 + ③ 前缀和 + ④ 填充
            JobHandle clearHandle = new GridClearJob
            {
                Counts = _grid.CellStart
            }.Schedule(state.Dependency);

            JobHandle countHandle = new GridCountJob
            {
                Counts = _grid.CellStart,
                Origin = _grid.Origin,
                Dim = _grid.Dim,
                CellSize = _grid.CellSize
            }.ScheduleParallel(clearHandle);

            JobHandle prefixHandle = new GridPrefixSumJob
            {
                Starts = _grid.CellStart,
                Cursors = _grid.CellCursor
            }.Schedule(countHandle);

            JobHandle fillHandle = new GridFillJob
            {
                Cursors = _grid.CellCursor,
                Entries = _grid.Entries,
                Origin = _grid.Origin,
                Dim = _grid.Dim,
                CellSize = _grid.CellSize,
                Capacity = _grid.Capacity
            }.ScheduleParallel(prefixHandle);

            // ⑤ 期望方向与速度
            JobHandle boidsHandle = new BoidsJob
            {
                CellStart = _grid.CellStart,
                CellEnd = _grid.CellCursor,
                Entries = _grid.Entries,
                Origin = _grid.Origin,
                Dim = _grid.Dim,
                CellSize = _grid.CellSize,
                DeltaTime = dt,
                Target = heroPos,
                SeparationRadius = config.SeparationRadius,
                NeighborRadius = config.NeighborRadius,
                CohesionWeight = config.CohesionWeight,
                AlignmentWeight = config.AlignmentWeight,
                SeekWeight = config.SeekWeight,
                TurnRate = config.TurnRate,
                StopRadius = config.StopRadius,
                ArriveRadius = config.ArriveRadius
            }.ScheduleParallel(fillHandle);

            // ⑥ 积分 → 位置解算
            JobHandle moveHandle = new IntegrateJob
            {
                DeltaTime = dt
            }.ScheduleParallel(boidsHandle);

            JobHandle resolveHandle = moveHandle;
            for (int i = 0; i < SolveIterations; i++)
            {
                resolveHandle = new ResolveOverlapJob
                {
                    CellStart = _grid.CellStart,
                    CellEnd = _grid.CellCursor,
                    Entries = _grid.Entries,
                    Origin = _grid.Origin,
                    Dim = _grid.Dim,
                    CellSize = _grid.CellSize,
                    SeparationRadius = config.SeparationRadius,
                    HeroCenter = heroPos,
                    HeroPushRadius = config.HeroCoreRadius
                }.ScheduleParallel(resolveHandle);
            }

            state.Dependency = resolveHandle;
        }
    }

    /// <summary>清零格子计数缓冲。串行 IJob——缓冲只有格数大小（约两万个 int），比并行调度更划算。
    /// 不用 NativeArray.Clear()：这个 Collections 版本没提供该成员，手写循环 Burst 会向量化成 memset。</summary>
    [BurstCompile]
    public struct GridClearJob : IJob
    {
        public NativeArray<int> Counts;

        public void Execute()
        {
            for (int i = 0; i < Counts.Length; i++)
            {
                Counts[i] = 0;
            }
        }
    }

    /// <summary>
    /// 并行数出每个格子有几个实体。原子自增是必须的——多个线程会同时命中同一格。
    /// 竞争程度取决于每格平均实体数：CellSize 由 SeparationRadius 推导后，
    /// 密堆时每格也就个位数，原子开销远小于原来哈希表的探查成本。
    /// </summary>
    [BurstCompile]
    public partial struct GridCountJob : IJobEntity
    {
        // Counts 在「任意下标」被写：每个实体自增的是自己所属格子，与实体自身下标无关。
        // Unity 安全系统的并行写入限制默认只允许写「job 自己那个下标」，
        // IJobEntity 根本没有「自己那个下标」这个概念，限制区间会塌成空 [0...-1]，
        // 于是任何写入都抛 IndexOutOfRangeException（开着 Burst 时托管检查被剥离，反而看不到）。
        // 线程安全由 Interlocked 原子自增保证，所以显式关掉按下标的并行限制。
        [NativeDisableParallelForRestriction] public NativeArray<int> Counts;
        public int2 Origin;
        public int2 Dim;
        public float CellSize;

        public void Execute(in Position2D pos, in EnemyTag tag)
        {
            int cell = SpatialGrid.CellOf(pos.Value, Origin, Dim, CellSize);
            unsafe
            {
                int* counts = (int*)Counts.GetUnsafePtr();
                Interlocked.Increment(ref counts[cell]);
            }
        }
    }

    /// <summary>
    /// 前缀和：把「每格计数」原地变成「每格起点」，同时拷一份给填充游标。
    /// 必须串行——数组扫描本身有先后依赖。格数由 (2·GridHalfExtent / CellSize)² 决定，
    /// 当前规模约两万格，串行一趟在微秒量级；若 FieldSize 放到 150 米以上需改两段式并行扫描。
    /// </summary>
    [BurstCompile]
    public struct GridPrefixSumJob : IJob
    {
        /// <summary>输入计数，输出 exclusive 起点。</summary>
        public NativeArray<int> Starts;

        /// <summary>填充游标，初值等于 Starts。</summary>
        public NativeArray<int> Cursors;

        public void Execute()
        {
            int running = 0;
            for (int i = 0; i < Starts.Length; i++)
            {
                int count = Starts[i];
                Starts[i] = running;
                Cursors[i] = running;
                running += count;
            }
        }
    }

    /// <summary>
    /// 并行填充：每个实体用原子游标从自己所属格子里领一个槽位，把快照写进去。
    /// 填完之后 Entries 里同一格子的实体物理相邻，CellCursor[cell] 就是该格区间的终点。
    /// 这就是排序网格相对哈希表的全部价值——邻域查询从随机访存变成顺序扫描。
    /// </summary>
    [BurstCompile]
    public partial struct GridFillJob : IJobEntity
    {
        // 同 GridCountJob：Cursors 与 Entries 都在任意下标读写（槽位由原子游标分配，与自身下标无关），
        // 必须关掉按下标的并行限制，否则每帧抛 IndexOutOfRangeException。
        // 线程安全由 Interlocked 原子游标保证——同一格子的实体领到的 slot 互不重叠。
        [NativeDisableParallelForRestriction] public NativeArray<int> Cursors;
        [NativeDisableParallelForRestriction] public NativeArray<NeighborData> Entries;
        public int2 Origin;
        public int2 Dim;
        public float CellSize;

        /// <summary>Entries 容量。容量足够时永远不会触发截断；触发说明申请逻辑有 bug。</summary>
        public int Capacity;

        public void Execute(Entity entity, in Position2D pos, in Velocity2D vel, in EnemyTag tag)
        {
            int cell = SpatialGrid.CellOf(pos.Value, Origin, Dim, CellSize);

            int slot;
            unsafe
            {
                int* cursors = (int*)Cursors.GetUnsafePtr();
                slot = Interlocked.Increment(ref cursors[cell]) - 1;
            }

            // 截断保护：宁可丢掉这一个邻居，也不能越界写坏内存。
            // 注意此时 Cursor 已经推进，区间里会留一个未初始化的洞——
            // 所以这是「不该发生」的分支，Capacity 必须由 EnsureSize 保证足够。
            if (slot >= Capacity)
            {
                return;
            }

            Entries[slot] = new NeighborData
            {
                Pos = pos.Value,
                Vel = vel.Value,
                Index = entity.Index
            };
        }
    }

    /// <summary>
    /// 字潮移动：只负责「往哪走、走多快」，方向以有限转向速率逼近期望方向，速度直接设定。
    /// 与经典 Boids 的三处关键差别：
    /// ① 不做加速度积分（vel += accel*dt）。加速度积分没有阻尼，敌人会冲过头再绕回来，
    ///    观感是「绕着玩家盘旋」而不是「扑上来」。这里直接设 vel = dir * speed（ADR-0008）。
    /// ② 速度乘抵达系数，贴到 StopRadius 后推进力消失——绕圈是「力没消失」的结果：
    ///    转向半径 = 速度/转向率 = 8/5 = 1.6 米，光靠转永远转不过来（ADR-0010）。
    /// ③ 再乘局部密度系数：身边没空位就停下，不让 1000 个兵同时往英雄身上压（ADR-0010）。
    /// ④ 分离不在这里做，它是速度层的软约束，密度一高就被压穿（实测最近 0.03 米、字糊成块）。
    ///    间距交给 ResolveOverlapJob 的位置层硬约束。聚合/对齐默认 0，字段保留只为 A/B 对比。
    /// </summary>
    [BurstCompile]
    public partial struct BoidsJob : IJobEntity
    {
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> CellEnd;
        [ReadOnly] public NativeArray<NeighborData> Entries;

        public int2 Origin;
        public int2 Dim;
        public float CellSize;
        public float DeltaTime;
        public float2 Target;
        public float SeparationRadius;
        public float NeighborRadius;
        public float CohesionWeight;
        public float AlignmentWeight;
        public float SeekWeight;
        public float TurnRate;
        public float StopRadius;
        public float ArriveRadius;

        /// <summary>
        /// 二维六方密堆时每个单位身边的邻居数：装到这个数就说明「我这块地方已经满了」。
        /// 这个数决定了最终能贴到英雄身上的兵有多少，是几何硬约束，不是手感参数——
        /// 英雄硬核半径 0.9 米，一圈周长 5.65 米，按字间距 1.15 米算只站得下 5 个兵。
        /// 实测不设这个上限时会挤进 127 个，字间距被压到 0.01 米、糊成一块实心色块。
        /// </summary>
        private const float PackCapacity = 6f;

        public void Execute(Entity entity, in Position2D pos, in MoveSpeed moveSpeed, ref Velocity2D vel, in EnemyTag tag)
        {
            float2 toTarget = Target - pos.Value;
            float dist = math.length(toTarget);

            float arrive = ArrivalFactor(dist);
            float2 seekDir = math.normalizesafe(toTarget);

            // 第二个因子是「我身边还有没有空位」。少了它，1000 个兵会全部往英雄身上压：
            // 位置解算每帧最多推开 0.575 米看着比向心的 0.133 米强，但密堆时一个兵周围几十个
            // 邻居的推力互相抵消、净推力趋近 0，而向内的驱动永不停歇——实测被压到间距 0.01 米。
            // 乘起来（不是取小）是为了让它随地平滑收油：八个空位进八个兵，第十个自己停下来。
            float room = LocalRoom(pos.Value, entity.Index);
            float approach = arrive * room;

            float2 desired = seekDir * (SeekWeight * approach);

            if (NeedFlock)
            {
                SampleFlock(pos.Value, out float2 cohesionOffset, out float2 alignmentAvg);
                desired += math.normalizesafe(cohesionOffset) * CohesionWeight
                           + math.normalizesafe(alignmentAvg) * AlignmentWeight;
            }

            if (math.lengthsq(desired) < 1e-6f)
            {
                vel.Value = float2.zero;
                return;
            }

            desired = math.normalize(desired);
            float2 facing = math.lengthsq(vel.Value) > 1e-6f ? math.normalize(vel.Value) : desired;
            vel.Value = RotateTowards(facing, desired, TurnStep(arrive)) * moveSpeed.Value * approach;
        }

        /// <summary>
        /// 身边还剩多少空位：1 = 完全空旷，0 = 已经塞到密堆上限。
        /// 用「半径 SeparationRadius 内的邻居数」而不是「正前方有没有人」——后者是二值的，
        /// 前面站一个人就急停，后排跟着停，连锁反应一路冻到 20 米外（实测平均距离卡在 22.7 米）。
        /// 邻居数则是连续的，随密度上升平滑收油。
        /// 计数半径取 SeparationRadius：六方密堆时刚好就是 6 个邻居，密度一到位 room 自然归零。
        /// </summary>
        private float LocalRoom(float2 self, int selfIndex)
        {
            int neighbors = 0;
            float r2 = SeparationRadius * SeparationRadius;
            int2 center = SpatialGrid.CellCoordOf(self, Origin, CellSize);

            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = center.x + dx;
                if ((uint)cx >= (uint)Dim.x) continue;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int cy = center.y + dy;
                    if ((uint)cy >= (uint)Dim.y) continue;

                    int cell = cy * Dim.x + cx;
                    int start = CellStart[cell];
                    int end = CellEnd[cell];

                    for (int i = start; i < end; i++)
                    {
                        NeighborData n = Entries[i];
                        if (n.Index != selfIndex && math.distancesq(self, n.Pos) < r2)
                        {
                            neighbors++;
                        }
                    }
                }
            }

            return math.saturate(1f - neighbors / PackCapacity);
        }

        /// <summary>聚合/对齐默认权重为 0，此时连邻居扫描都跳过——不为用不到的东西付账。</summary>
        private bool NeedFlock => CohesionWeight > 0f || AlignmentWeight > 0f;

        /// <summary>
        /// 聚合/对齐的邻居采样。间距约束不在这里，那是 ResolveOverlapJob 的活。
        /// 感知半径可能远大于 SeparationRadius（当前 2.5 vs 1.15），所以扫描范围按 NeighborRadius
        /// 单独算，而不是复用 LocalRoom 的 3×3。
        /// span 要取 1 + ceil(NR/CellSize)：正方向的格偏移 sup 是 1 + NR/CellSize，
        /// 只取 ceil 会漏掉最外那一圈（理由同 ScaleConfig.CellSizeFactor 的推导）。
        /// </summary>
        private void SampleFlock(float2 self, out float2 cohesionOffset, out float2 alignmentAvg)
        {
            float2 cohesionSum = float2.zero;
            float2 alignmentSum = float2.zero;
            int neighborCount = 0;

            int span = 1 + (int)math.ceil(NeighborRadius / CellSize);
            int2 center = SpatialGrid.CellCoordOf(self, Origin, CellSize);

            for (int dx = -span; dx <= span; dx++)
            {
                int cx = center.x + dx;
                if ((uint)cx >= (uint)Dim.x) continue;

                for (int dy = -span; dy <= span; dy++)
                {
                    int cy = center.y + dy;
                    if ((uint)cy >= (uint)Dim.y) continue;

                    int cell = cy * Dim.x + cx;
                    int start = CellStart[cell];
                    int end = CellEnd[cell];

                    for (int i = start; i < end; i++)
                    {
                        NeighborData n = Entries[i];
                        if (math.distance(self, n.Pos) < NeighborRadius)
                        {
                            cohesionSum += n.Pos;
                            alignmentSum += n.Vel;
                            neighborCount++;
                        }
                    }
                }
            }

            cohesionOffset = neighborCount > 0 ? cohesionSum / neighborCount - self : float2.zero;
            alignmentAvg = neighborCount > 0 ? alignmentSum / neighborCount : float2.zero;
        }

        /// <summary>抵达系数：1 = 全速，0 = 已贴进 StopRadius（完全停住）。</summary>
        private float ArrivalFactor(float dist)
        {
            float span = math.max(0.001f, ArriveRadius - StopRadius);
            return math.saturate((dist - StopRadius) / span);
        }

        /// <summary>
        /// 本帧允许的转向弧度。远处迟钝（给玩家拉扯空间），越贴近越灵活。
        /// 贴身区直接放开到 π——也就是「一帧内允许掉头」：那里目标就在身边、方向每帧都在变，
        /// 留任何转向限制都只会让兵沿环滑动，那正是「在英雄旁边转悠」的来源。
        /// </summary>
        private float TurnStep(float arrive)
        {
            float far = TurnRate * DeltaTime;
            return math.lerp(math.PI, far, arrive);
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

    /// <summary>
    /// 位置层重叠解算（Jacobi 一遍，松弛系数 0.5）。
    /// 为什么不能只靠速度层的分离力：那是软约束，单位密度一高就会被外圈的压力压穿，
    /// 实测 1000 个兵时贴身区最近两个字只隔 0.03 米——字形彻底糊成一块实心色块。
    /// 位置约束是硬的，压不穿。
    ///
    /// 邻居位置读的是本帧开头的网格快照，所有实体基于同一份数据各算各的，因此无写竞争、可并行。
    /// </summary>
    [BurstCompile]
    public partial struct ResolveOverlapJob : IJobEntity
    {
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> CellEnd;
        [ReadOnly] public NativeArray<NeighborData> Entries;

        public int2 Origin;
        public int2 Dim;
        public float CellSize;
        public float SeparationRadius;
        public float2 HeroCenter;
        public float HeroPushRadius;

        /// <summary>
        /// 先把 NeighborPush 的邻居互推做完，再投影出英雄硬核。
        /// 顺序不能反：先投影再互推，互推会把兵重新按回英雄字底下。
        /// </summary>
        public void Execute(Entity entity, ref Position2D pos, in EnemyTag tag)
        {
            pos.Value = PushOutOfHero(pos.Value + NeighborPush(pos.Value, entity.Index));
        }

        /// <summary>与邻居重叠的部分各让一半。松弛给满（1.0）会过冲——贴身区几十个邻居的推力叠加，
        /// 兵会像弹球一样被弹飞、整片字潮被炸散（实测全群平均 2 秒跑出 24 米，比速度上限还快）。
        /// 0.5 再限幅才稳。收敛慢一点没关系，稳定性优先。
        ///
        /// **但不能只有求和。** 成对推力求和有个固有失效模式：均匀密实介质里，一个兵周围的邻居
        /// 大致对称分布，矢量和趋于零——于是"被压到极近的两个兵"这一笔也会被别的邻居抵消掉。
        /// 实测：1 万实体（全场 1.42 倍超容量）时出现最小间距 0.015 米的全重合锁死对，
        /// 而按 1.42 倍超容量算，六方密堆的均衡间距应该是 0.95 米。差 63 倍——这就是求和抵消。
        /// 兜底分支（dist &lt; 1e-4）救不了它：0.015 米远大于 1e-4，方向是有效的、只是被抵掉了。
        ///
        /// 所以把「穿透最深的那一对」单独拎出来，与平滑项取更优者：
        /// 稀疏区（0~2 个邻居）两者恒等，行为不变；密实区保证最深的一对必定被解开。
        /// </summary>
        private float2 NeighborPush(float2 self, int selfIndex)
        {
            float2 push = float2.zero;
            float worstPenetration = 0f;
            float2 worstDir = float2.zero;

            int2 center = SpatialGrid.CellCoordOf(self, Origin, CellSize);

            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = center.x + dx;
                if ((uint)cx >= (uint)Dim.x) continue;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int cy = center.y + dy;
                    if ((uint)cy >= (uint)Dim.y) continue;

                    int cell = cy * Dim.x + cx;
                    int start = CellStart[cell];
                    int end = CellEnd[cell];

                    for (int i = start; i < end; i++)
                    {
                        NeighborData n = Entries[i];

                        // 跳过自己。漏掉这一行，每个兵都会把自己当成最近的邻居推一记，
                        // 那笔伪推力会吃光单帧推力预算，邻居互推反而被限幅砍掉。
                        if (n.Index == selfIndex)
                        {
                            continue;
                        }

                        float2 delta = self - n.Pos;
                        float dist = math.length(delta);
                        if (dist >= SeparationRadius)
                        {
                            continue;
                        }

                        float2 dir;
                        if (dist < 1e-4f)
                        {
                            // 完全重合时没有方向可算。兜底方向必须用「实体身份」而不是位置：
                            // 位置相同的两个兵算出的是同一个方向，等于没岔开。实体序号才是真正互异的。
                            dir = FallbackDir(selfIndex);
                        }
                        else
                        {
                            dir = delta / dist;
                        }

                        float penetration = SeparationRadius - dist;
                        push += dir * (penetration * 0.5f);

                        if (penetration > worstPenetration)
                        {
                            worstPenetration = penetration;
                            worstDir = dir;
                        }
                    }
                }
            }

            // 两项都天然 ≤ SeparationRadius * 0.5（单对位移上限就是各让一半），所以取更优者不会过冲。
            float2 smooth = ClampPush(push);
            float2 worst = worstPenetration > SeparationRadius * DeepPenetrationFactor
                ? worstDir * (worstPenetration * 0.5f)
                : float2.zero;
            return math.lengthsq(worst) > math.lengthsq(smooth) ? worst : smooth;
        }

        /// <summary>单帧推离量限幅。贴身区可能有二三十个邻居同时挤压，不限幅就是一次爆炸。</summary>
        private float2 ClampPush(float2 push)
        {
            float len = math.length(push);
            float cap = SeparationRadius * 0.5f;
            return len > cap ? push * (cap / len) : push;
        }

        /// <summary>
        /// 「最深穿透对」项的启动门槛，单位是 SeparationRadius 的比例。
        ///
        /// 为什么必须有门槛：健康配置下（实测 1K，邻居间距 1.08 米）穿透量只有 0.07 米，
        /// 而平滑项在对称分布下趋于零——不设门槛的话「最深对」项会恒常接管，
        /// 给每个兵塞一笔常驻小推力，整个贴身环带持续扰动：
        /// 实测静止漂移从 0.25 米涨到 1.03 米、贴身最小间距从 1.09 掉到 0.75 米。
        ///
        /// 0.4 的含义：只有当两个字重叠超过 40%（间距 &lt; 0.69 米、字宽 1.1 米，
        /// 肉眼已经糊在一起）时才认为这是病理状态，需要强行解开。低于它交给平滑项。
        /// </summary>
        private const float DeepPenetrationFactor = 0.4f;

        /// <summary>重合时的兜底方向：黄金角按实体序号散布，两个重合的兵各走各的。</summary>
        private static float2 FallbackDir(int index)
        {
            float angle = index * 2.39996323f;
            return new float2(math.cos(angle), math.sin(angle));
        }

        /// <summary>英雄是硬核：不许钻进「英雄」二字底下，否则字压住英雄，看不清谁是谁。
        /// 这里直接投影到硬核表面（而不是施加一个推力）——推力会被邻居合力抵消，投影不会。</summary>
        private float2 PushOutOfHero(float2 self)
        {
            float2 delta = self - HeroCenter;
            float dist = math.length(delta);
            if (dist >= HeroPushRadius)
            {
                return self;
            }

            float2 dir = dist < 1e-4f ? new float2(1f, 0f) : delta / dist;
            return HeroCenter + dir * HeroPushRadius;
        }
    }
}
