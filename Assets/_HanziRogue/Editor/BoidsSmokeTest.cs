using System;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;
using HanziRogue.Gameplay;

namespace HanziRogue.Editor
{
    /// <summary>
    /// 无头自检：不进 PlayMode，直接建一个 World 把「千兵追英雄」跑起来。
    /// 验证五件事：实体确实生成了、字潮从外圈起步、位置没算成 NaN、
    /// 敌人平均距离朝英雄下降、英雄被场地边界拦住跑不出去。
    /// 宪法 §4.3：行为与性能结论必须实测，禁止凭代码观感下结论。
    /// </summary>
    public static class BoidsSmokeTest
    {
        private const int Frames = 240;

        /// <summary>
        /// 合围帧数。位置层解算每帧只让开一半重叠（松一半才稳），1000 个兵从 8 米外堆成密堆团
        /// 需要时间——300 帧不够，量出来的是「还没堆完」的中间态。给到 600 帧等它进入稳态。
        /// </summary>
        private const int ConvergeFrames = 600;

        private const int SettleFrames = 120;
        private const float DeltaTime = 1f / 60f;

        /// <summary>
        /// 「贴身」判定半径。略大于 StopRadius（1.5），因为兵会停在 StopRadius 附近而非正好压线。
        /// 绕圈式追击下这个数会一直是 0，专门用来抓「追而不咬」。
        /// </summary>
        private const float CloseRadius = 2f;

        /// <summary>
        /// 静止漂移上限（米/2 秒）。英雄站着不动时，已经咬住的兵还应该几乎不动。
        /// 绕圈的话速度 8 m/s 跑两秒能画出接近 16 米的位移；贴住则接近 0。
        /// 这是唯一能定量抓住「在英雄旁边转悠」的判据。
        /// </summary>
        private const float DriftLimit = 1.5f;

        /// <summary>
        /// 贴身区内两兵最小间距下限。字宽 1.1 米，压到 0 就是字形糊成实心色块。
        /// 留 0.7 的余量：允许密集时轻微压一点，但不允许完全重叠。
        /// </summary>
        private const float CloseMinGapLimit = 0.7f;

        /// <summary>
        /// 无头自检入口（batchmode -executeMethod 用）。整份报告作为一条日志写进控制台。
        /// </summary>
        public static void Run()
        {
            Debug.Log(Execute(0));
        }

        /// <summary>
        /// 同 Run，但把整份报告当返回值交给调用方。
        /// 存在的理由：MCP 通道读控制台拿不到 info 级日志（Debug.Log 读不出来，只有警告/错误能读），
        /// 所以自动化验证只能走返回值，去控制台捞是捞不到的。
        /// </summary>
        public static string RunSummary()
        {
            return Execute(0);
        }

        /// <summary>覆盖敌人数跑一档，用于多档规模实测（1 万 / 5 万 / 10 万）。</summary>
        public static string RunSummary(int enemyCountOverride)
        {
            return Execute(enemyCountOverride);
        }

        private static string Execute(int enemyCountOverride)
        {
            var report = new System.Text.StringBuilder();
            var world = new World("BoidsSmokeTest");
            try
            {
                EntityManager em = world.EntityManager;
                GameConfig config = LoadConfig();
                if (enemyCountOverride > 0)
                {
                    config.EnemyCount = enemyCountOverride;
                }

                Entity configEntity = em.CreateEntity(typeof(GameConfig));
                em.SetComponentData(configEntity, config);

                // GetOrCreateSystem 只创建系统，不会自动挂进所属 group 的 update list，
                // 因此这里显式挂载——否则 world.Update() 只会空转顶层 group。
                // HeroIntentSystem 故意不挂：无头环境没有键盘，改为直接驱动 HeroIntent 组件，
                // 这样验证的是 HeroMoveSystem 的移动与边界，而不是输入读取。
                var simulationGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
                simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpawnSystem>());
                simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<HeroMoveSystem>());
                simulationGroup.AddSystemToUpdateList(world.GetOrCreateSystem<BoidsSystem>());

                int systemCount = 0;
                foreach (var handle in world.Systems)
                {
                    systemCount++;
                }

                Emit(report, $"[SmokeTest] World 顶层系统数 = {systemCount}，" +
                              $"GameConfig 实体数 = {em.CreateEntityQuery(typeof(GameConfig)).CalculateEntityCount()}");

                world.SetTime(new TimeData(0f, DeltaTime));
                world.Update();

                int spawned = em.CreateEntityQuery(typeof(EnemyTag)).CalculateEntityCount();
                float spawnInnerGap = MinDistanceToHero(em);
                float spawnMinPair = MinPairwiseDistance(em);

                // 阶段一：英雄一路往 +X 跑。场地半边长 50，速度 14，4 秒足以撞到边界。
                // 这一段只验证「边界拦得住」——英雄比敌人快，被追期间平均距离上升是正确行为，
                // 所以不能拿这一段判断收敛。
                SetHeroIntent(em, new float2(1f, 0f));

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 1; i <= Frames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                float2 heroPos = ReadHeroPosition(em);
                bool bounded = math.abs(heroPos.x) <= config.FieldSize + 0.01f
                               && math.abs(heroPos.y) <= config.FieldSize + 0.01f;

                // 阶段二：英雄停下，看字潮是否真的扑上来
                SetHeroIntent(em, float2.zero);
                float before = AverageDistanceToHero(em);

                for (int i = Frames + 1; i <= Frames + ConvergeFrames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                float after = AverageDistanceToHero(em);
                float finalGap = MinDistanceToHero(em);
                int closeCount = CountWithinRadius(em, CloseRadius);

                // 阶段三：英雄继续站着不动，再看 2 秒。
                // 只看「这一刻已经贴身的兵」的位移——全群平均没有意义，
                // 外圈的兵本来就还在往里赶路，它们动是应该的（这个数另作参考打印）。
                // 绕圈恰恰发生在贴身的那批身上：它们会沿半径 1.6 米的圆一直跑。
                float[] settleBefore = SnapshotPositions(em);
                int[] closeIndices = IndicesWithinRadius(em, CloseRadius);
                for (int i = Frames + ConvergeFrames + 1; i <= Frames + ConvergeFrames + SettleFrames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                stopwatch.Stop();

                float drift = AverageDisplacement(em, settleBefore, closeIndices);
                float swarmDrift = AverageDisplacement(em, settleBefore, null);
                float closeMinGap = MinPairwiseDistanceWithinRadius(em, CloseRadius);
                float msPerFrame = stopwatch.ElapsedMilliseconds
                                   / (float)(Frames + ConvergeFrames + SettleFrames);

                bool pass = spawned == config.EnemyCount
                            && !float.IsNaN(before)
                            && !float.IsNaN(after)
                            && spawnInnerGap >= config.SpawnInnerRadius - 0.5f
                            && spawnMinPair > 0.05f
                            && after < before - 5f
                            && finalGap < CloseRadius
                            && closeCount >= 1
                            && drift < DriftLimit
                            && closeMinGap > CloseMinGapLimit
                            && bounded;

                Emit(report, $"[SmokeTest] 生成敌人 {spawned}/{config.EnemyCount}");
                Emit(report, $"[SmokeTest] 起始最近敌人距离 {spawnInnerGap:F2}（应 >= 内径 {config.SpawnInnerRadius}）");
                Emit(report, $"[SmokeTest] 起始最近两个敌人间距 {spawnMinPair:F3}（应 > 0.05，防止「全部叠在同一点」）");
                Emit(report, $"[SmokeTest] 英雄最终位置 ({heroPos.x:F2}, {heroPos.y:F2})，" +
                              $"边界 ±{config.FieldSize}，{(bounded ? "被拦在场内" : "跑出场地")}");
                Emit(report, $"[SmokeTest] 英雄停下后：到英雄平均距离 {before:F2} -> {after:F2}（{ConvergeFrames} 帧），" +
                              $"最近敌人 {finalGap:F2}");
                Emit(report, $"[SmokeTest] 贴身（< {CloseRadius}m）敌人数 {closeCount}——" +
                              $"绕圈式追击这个数会是 0，直扑才会大于 0");
                Emit(report, $"[SmokeTest] 贴身区两兵最小间距 {closeMinGap:F2} 米" +
                              $"（应 > {CloseMinGapLimit}；掉到 0 就是字互相压死、糊成一块色块）");
                Emit(report, $"[SmokeTest] 英雄静止后 2 秒：贴身的 {closeIndices.Length} 个兵平均只挪了 {drift:F2} 米" +
                              $"（应 < {DriftLimit}，大了就是在英雄身边绕圈）；" +
                              $"全群平均 {swarmDrift:F2} 米（外圈还在往里挤，动是正常的）");
                Emit(report, $"[SmokeTest] 单帧逻辑耗时 {msPerFrame:F2} ms（Editor 内实测；" +
                              $"不是真机性能，只用于横向比档与防退化）");
                Emit(report, pass
                    ? $"[SmokeTest] PASS：环形起步、无重叠、英雄被边界拦住、字潮扑到 {CloseRadius} 米内并贴住不动"
                    : "[SmokeTest] FAIL：见上方数值定位问题");
            }
            catch (Exception e)
            {
                // 异常也进报告：MCP 通道下 LogError 能读，但报告要能独立说明发生了什么。
                report.AppendLine("[SmokeTest] 异常：" + e);
                Debug.LogError("[SmokeTest] 异常：" + e);
            }
            finally
            {
                world.Dispose();
            }

            return report.ToString();
        }

        /// <summary>同时写日志和报告。报告是自动化验证的唯一可信通道，日志只给人看。</summary>
        private static void Emit(System.Text.StringBuilder report, string line)
        {
            Debug.Log(line);
            report.AppendLine(line);
        }

        /// <summary>
        /// 多档规模实测入口：只回答「这个规模跑得动吗、单帧多久」，不做行为断言。
        /// 与 Execute 的硬差别：**跳过起始最小间距检查**——那是 O(n²)，
        /// 1 万实体 5×10^7 次、10 万实体 5×10^9 次距离计算，会把自检直接挂死。
        ///
        /// 计时口径：英雄全程静止（不写 HeroIntent 即为零），字潮合围压实后再计时——
        /// 这是每实体候选邻居数最高、也就是最贵的状态。
        /// Editor 未开 Burst，绝对值只用于横向比档，不能当真机性能（宪法 §4.3）。
        /// </summary>
        public static string Measure(int enemyCount, int warmupFrames, int measureFrames)
        {
            return Measure(enemyCount, warmupFrames, measureFrames, 0f);
        }

        /// <summary>
        /// 同上，另可覆盖场地半边长。用来做**恒定密度对照**：
        /// 复杂度是 O(n·k)，k = 密度 × 9 × CellSize²。同场地加大 n 会同时抬高密度和 k，
        /// 于是总共退化成 O(n²)——那测出来的超线性是物理必然，不是实现缺陷。
        /// 把场地按 √n 放大，密度回到设计值，成本就该回到线性。这两条曲线合起来才能下结论。
        /// </summary>
        public static string Measure(int enemyCount, int warmupFrames, int measureFrames, float fieldSizeOverride)
        {
            return Measure(enemyCount, warmupFrames, measureFrames, fieldSizeOverride, 0f);
        }

        /// <summary>
        /// 同上，另可单独覆盖网格半边长（不动 FieldSize）。
        /// 用来验证「超容量时实体扩散超出网格范围、被 CellOf 全 clamp 进最外圈格子」这条病理：
        /// 场地仍是 ±50（英雄边界与生成环不变），只把网格放大，看单帧是否显著下降。
        /// </summary>
        public static string Measure(int enemyCount, int warmupFrames, int measureFrames,
                                     float fieldSizeOverride, float gridHalfExtentOverride)
        {
            var report = new System.Text.StringBuilder();
            var world = new World("BoidsMeasure");
            try
            {
                EntityManager em = world.EntityManager;
                GameConfig config = LoadConfig();
                config.EnemyCount = enemyCount;
                if (fieldSizeOverride > 0f)
                {
                    config.FieldSize = fieldSizeOverride;
                    config.GridHalfExtent = fieldSizeOverride + config.SeparationRadius * 8f;
                }

                if (gridHalfExtentOverride > 0f)
                {
                    config.GridHalfExtent = gridHalfExtentOverride;
                }

                Entity configEntity = em.CreateEntity(typeof(GameConfig));
                em.SetComponentData(configEntity, config);

                var group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
                group.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpawnSystem>());
                group.AddSystemToUpdateList(world.GetOrCreateSystem<HeroMoveSystem>());
                group.AddSystemToUpdateList(world.GetOrCreateSystem<BoidsSystem>());

                var spawnWatch = System.Diagnostics.Stopwatch.StartNew();
                world.SetTime(new TimeData(0f, DeltaTime));
                world.Update();
                spawnWatch.Stop();

                int spawned = em.CreateEntityQuery(typeof(EnemyTag)).CalculateEntityCount();

                for (int i = 1; i <= warmupFrames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                float before = AverageDistanceToHero(em);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = warmupFrames + 1; i <= warmupFrames + measureFrames; i++)
                {
                    world.SetTime(new TimeData(i * DeltaTime, DeltaTime));
                    world.Update();
                }

                stopwatch.Stop();

                float after = AverageDistanceToHero(em);
                float finalGap = MinDistanceToHero(em);
                int closeCount = CountWithinRadius(em, CloseRadius);
                float closeMinGap = MinPairwiseDistanceWithinRadius(em, CloseRadius);
                string closestText = ClosestPairReport(em, config.SeparationRadius);
                string ringText = RingReport(em);
                float msPerFrame = stopwatch.ElapsedMilliseconds / (float)measureFrames;

                // 提前算好文本：C# 9 的插值字符串里不能再嵌 "（那要 C# 11 的原始字符串），
                // 所以带格式的数值必须先在洞外转成 string。
                string gapText = float.IsNaN(closeMinGap) ? "n/a" : closeMinGap.ToString("F2");

                report.AppendLine($"[Measure] 规模 {spawned}（配置 {enemyCount}）；场地半边长 {config.FieldSize}；CellSize = {config.CellSize:F3} m");
                report.AppendLine($"[Measure] 生成耗时 {spawnWatch.ElapsedMilliseconds} ms（一次性）");
                report.AppendLine($"[Measure] 预热 {warmupFrames} 帧后：平均距离 {before:F2}，最近 {finalGap:F2}，贴身 {closeCount} 个");
                report.AppendLine($"[Measure] 贴身区（< {CloseRadius} m）两兵最小间距 {gapText} m（设计字间距 1.15，阈值 {CloseMinGapLimit}）");
                report.AppendLine(closestText);
                report.Append(ringText);
                report.AppendLine($"[Measure] 实测 {measureFrames} 帧：{msPerFrame:F2} ms/帧，收尾平均距离 {after:F2}");
                report.AppendLine(float.IsNaN(after)
                    ? "[Measure] 数值健康：出现 NaN（FAIL）"
                    : "[Measure] 数值健康：无 NaN");
            }
            catch (Exception e)
            {
                report.AppendLine("[Measure] 异常：" + e);
                Debug.LogError("[BoidsMeasure] 异常：" + e);
            }
            finally
            {
                world.Dispose();
            }

            return report.ToString();
        }

        /// <summary>
        /// 诊断探针：径向环带占用表。把「实际装了多少」和「几何上能装多少」并排摆出来，
        /// 一眼看出是哪一环被压穿——是英雄硬核边上堆死，还是全场均匀挤压。
        /// 容量口径：环带面积 ÷ 1.145 m²（字间距 1.15 米时六方密堆的单实体占位）。
        /// </summary>
        private static string RingReport(EntityManager em)
        {
            float2 hero = ReadHeroPosition(em);
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            float[] edges = { 0.9f, 1.2f, 1.5f, 2f, 3f, 5f, 10f, 20f, 50f, 1000f };
            int[] bins = new int[edges.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                float d = math.distance(positions[i].Value, hero);
                int b = 0;
                while (b < edges.Length - 1 && d >= edges[b])
                {
                    b++;
                }

                bins[b]++;
            }

            positions.Dispose();
            query.Dispose();

            var sb = new System.Text.StringBuilder();
            float lo = 0f;
            for (int b = 0; b < bins.Length; b++)
            {
                float hi = edges[b];
                float area = math.PI * (hi * hi - lo * lo);
                float cap = area / 1.145f;
                string hiText = hi > 900f ? "inf" : hi.ToString("F1");
                float ratio = cap > 0.01f ? bins[b] / cap : float.NaN;
                sb.AppendLine($"[Ring] {lo:F1}~{hiText} m: {bins[b]} 个，容量 {cap:F1}，占用 {ratio:F2}x");
                lo = hi;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 全场最近点对，以及两点各自离英雄多远。O(n·k) 走哈希网格——
        /// 直接 O(n²) 在 10 万实体上是 5×10^9 次距离计算，会把自检挂死。
        /// 「最近的那对在哪」比「最近有多近」信息量大得多：
        /// 落在英雄 0.9 米硬核边上 = 撞墙堆死；落在 40 米外 = 全场均匀挤压。两者的修法完全不同。
        /// </summary>
        private static string ClosestPairReport(EntityManager em, float cellSize)
        {
            float2 hero = ReadHeroPosition(em);
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            var entities = query.ToEntityArray(Allocator.Temp);

            // 身份冲突统计。Entries 里存的身份是 entity.Index —— 而 Unity 的 Entity.Index
            // 是 **chunk 内** 索引，跨 chunk 会重复。位置解算却靠它来「跳过自己」
            // （ResolveOverlapJob.NeighborPush 里的 n.Index == selfIndex）。
            // 同号的两个实体因此互相视为自己、永远不互相推开——这是「完全重合」的成因。
            var idCount = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < entities.Length; i++)
            {
                idCount.TryGetValue(entities[i].Index, out int c);
                idCount[entities[i].Index] = c + 1;
            }

            int maxShare = 0;
            int conflicted = 0;
            foreach (var kv in idCount)
            {
                if (kv.Value > 1)
                {
                    maxShare = math.max(maxShare, kv.Value);
                    conflicted += kv.Value;
                }
            }

            var buckets = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>();
            for (int i = 0; i < positions.Length; i++)
            {
                long key = BucketKey(positions[i].Value, cellSize);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new System.Collections.Generic.List<int>(4);
                    buckets[key] = list;
                }

                list.Add(i);
            }

            float min = float.MaxValue;
            int a = -1;
            int b2 = -1;

            for (int i = 0; i < positions.Length; i++)
            {
                int2 c = BucketCoord(positions[i].Value, cellSize);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!buckets.TryGetValue(BucketKey(c.x + dx, c.y + dy), out var list))
                        {
                            continue;
                        }

                        for (int k = 0; k < list.Count; k++)
                        {
                            int j = list[k];
                            if (j <= i)
                            {
                                continue;
                            }

                            float d = math.distance(positions[i].Value, positions[j].Value);
                            if (d < min)
                            {
                                min = d;
                                a = i;
                                b2 = j;
                            }
                        }
                    }
                }
            }

            string text = min > 1e9f
                ? "[Closest] 全场点数不足 2，无法求最近点对"
                : $"[Closest] 全场最近点对 {min:F3} m；两点离英雄 " +
                  $"{math.distance(positions[a].Value, hero):F2} / {math.distance(positions[b2].Value, hero):F2} m；" +
                  $"Entity.Index {entities[a].Index}(v{entities[a].Version}) / {entities[b2].Index}(v{entities[b2].Version})";

            text += $"\n[Identity] {entities.Length} 个实体共用 {idCount.Count} 个不同 Index；" +
                    $"最多 {maxShare} 个实体共用一个 Index；{conflicted} 个实体存在身份冲突";

            positions.Dispose();
            entities.Dispose();
            query.Dispose();
            return text;
        }

        private static int2 BucketCoord(float2 p, float cellSize)
        {
            return new int2((int)math.floor(p.x / cellSize), (int)math.floor(p.y / cellSize));
        }

        private static long BucketKey(float2 p, float cellSize)
        {
            int2 c = BucketCoord(p, cellSize);
            return BucketKey(c.x, c.y);
        }

        private static long BucketKey(int cx, int cy)
        {
            return ((long)cx << 32) ^ (uint)cy;
        }

        private static void SetHeroIntent(EntityManager em, float2 move)
        {
            var query = em.CreateEntityQuery(typeof(HeroIntent), typeof(HeroTag));
            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new HeroIntent { Move = move });
            }

            entities.Dispose();
            query.Dispose();
        }

        private static float2 ReadHeroPosition(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(HeroTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            float2 result = positions.Length > 0 ? positions[0].Value : float2.zero;
            positions.Dispose();
            query.Dispose();
            return result;
        }

        private static float AverageDistanceToHero(EntityManager em)
        {
            return DistanceToHero(em, average: true);
        }

        private static float MinDistanceToHero(EntityManager em)
        {
            return DistanceToHero(em, average: false);
        }

        /// <summary>
        /// 任意两个敌人的最小间距。这条断言存在的理由：
        /// 曾经因为 Random 结构体按值传参，1000 个单位全生成在同一个坐标上，
        /// 而当时所有断言（数量 / 非 NaN / 距离下降）全都能通过。
        /// </summary>
        private static float MinPairwiseDistance(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            float min = float.MaxValue;
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = i + 1; j < positions.Length; j++)
                {
                    float d = math.distance(positions[i].Value, positions[j].Value);
                    min = math.min(min, d);
                }
            }

            int count = positions.Length;
            positions.Dispose();
            query.Dispose();
            return count < 2 ? float.NaN : min;
        }

        /// <summary>敌人到英雄的距离统计；出现任何 NaN/Inf 直接返回 NaN，让判据失败。</summary>
        private static float DistanceToHero(EntityManager em, bool average)
        {
            float2 hero = ReadHeroPosition(em);

            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);
            int count = positions.Length;

            double sum = 0d;
            float min = float.MaxValue;
            bool bad = false;
            for (int i = 0; i < count; i++)
            {
                float d = math.distance(positions[i].Value, hero);
                if (float.IsNaN(d) || float.IsInfinity(d))
                {
                    bad = true;
                    break;
                }

                sum += d;
                min = math.min(min, d);
            }

            positions.Dispose();
            query.Dispose();

            if (bad || count == 0)
            {
                return float.NaN;
            }

            return average ? (float)(sum / count) : min;
        }

        /// <summary>统计真正咬到英雄的敌人数（距离 &lt; radius）。</summary>
        private static int CountWithinRadius(EntityManager em, float radius)
        {
            float2 hero = ReadHeroPosition(em);
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (math.distance(positions[i].Value, hero) < radius)
                {
                    count++;
                }
            }

            positions.Dispose();
            query.Dispose();
            return count;
        }

        /// <summary>抓一份敌人坐标快照。查询顺序在无结构性变更时稳定，可前后两次对比。</summary>
        private static float[] SnapshotPositions(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            var snapshot = new float[positions.Length * 2];
            for (int i = 0; i < positions.Length; i++)
            {
                snapshot[i * 2] = positions[i].Value.x;
                snapshot[i * 2 + 1] = positions[i].Value.y;
            }

            positions.Dispose();
            query.Dispose();
            return snapshot;
        }

        /// <summary>
        /// 与快照对比算平均位移，判断「贴住了」还是「还在绕圈」。
        /// indices 为 null 统计全群，否则只统计指定下标（贴身的那批）。
        /// </summary>
        private static float AverageDisplacement(EntityManager em, float[] snapshot, int[] indices)
        {
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            int limit = math.min(positions.Length, snapshot.Length / 2);
            double sum = 0d;
            int count = 0;

            if (indices == null)
            {
                for (int i = 0; i < limit; i++)
                {
                    sum += DisplacementOf(positions[i].Value, snapshot, i);
                    count++;
                }
            }
            else
            {
                for (int k = 0; k < indices.Length; k++)
                {
                    int i = indices[k];
                    if (i >= limit)
                    {
                        continue;
                    }

                    sum += DisplacementOf(positions[i].Value, snapshot, i);
                    count++;
                }
            }

            positions.Dispose();
            query.Dispose();
            return count == 0 ? float.NaN : (float)(sum / count);
        }

        private static float DisplacementOf(float2 current, float[] snapshot, int index)
        {
            return math.length(current - new float2(snapshot[index * 2], snapshot[index * 2 + 1]));
        }

        /// <summary>
        /// 贴身区内任意两个兵的最小间距。这是「字形还看得见吗」的量化判据：
        /// 字宽 1.1 米，间距掉到 0 就意味着「兵」字互相完全压住、糊成一块实心色块
        /// （分离力在贴身区失效时就会这样）。
        /// </summary>
        private static float MinPairwiseDistanceWithinRadius(EntityManager em, float radius)
        {
            float2 hero = ReadHeroPosition(em);
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            float min = float.MaxValue;
            int count = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (math.distance(positions[i].Value, hero) >= radius)
                {
                    continue;
                }

                count++;
                for (int j = i + 1; j < positions.Length; j++)
                {
                    if (math.distance(positions[j].Value, hero) >= radius)
                    {
                        continue;
                    }

                    min = math.min(min, math.distance(positions[i].Value, positions[j].Value));
                }
            }

            positions.Dispose();
            query.Dispose();
            return count < 2 ? float.NaN : min;
        }

        /// <summary>挑出已经贴到英雄身上的敌人下标，顺序与 SnapshotPositions 一致。</summary>
        private static int[] IndicesWithinRadius(EntityManager em, float radius)
        {
            float2 hero = ReadHeroPosition(em);
            var query = em.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            var positions = query.ToComponentDataArray<Position2D>(Allocator.Temp);

            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < positions.Length; i++)
            {
                if (math.distance(positions[i].Value, hero) < radius)
                {
                    list.Add(i);
                }
            }

            positions.Dispose();
            query.Dispose();
            return list.ToArray();
        }

        /// <summary>
        /// 配置来源。优先读 ScaleConfig.asset——自检跑的必须是游戏里真用的那套数值，
        /// 否则「自检过了」不代表「游戏里对」（曾经这里硬编码 SeparationRadius=0.8，
        /// 而实际运行是 1.15，自检验的根本不是真实配置）。读不到才回退内置值，并打警告。
        /// </summary>
        private static GameConfig LoadConfig()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScaleConfig>(
                "Assets/_HanziRogue/Data/ScaleConfig.asset");

            if (asset == null)
            {
                Debug.LogWarning("[SmokeTest] 读不到 ScaleConfig.asset，回退内置默认值——" +
                                 "此时自检数值可能与游戏实际运行不一致。");
                return new GameConfig
                {
                    EnemyCount = 1000,
                    FieldSize = 50f,
                    SpawnInnerRadius = 8f,
                    HeroSpeed = 14f,
                    EnemySpeed = 8f,
                    CellSize = 1.15f * 1f,
                    GridHalfExtent = 50f + 1.15f * 8f,
                    SeparationRadius = 1.15f,
                    NeighborRadius = 2.5f,
                    CohesionWeight = 0f,
                    AlignmentWeight = 0f,
                    SeekWeight = 2.2f,
                    TurnRate = 5f,
                    SpeedVariance = 0.12f,
                    StopRadius = 1.5f,
                    ArriveRadius = 3.5f,
                    HeroCoreRadius = 0.9f
                };
            }

            return new GameConfig
            {
                EnemyCount = asset.EnemyCount,
                FieldSize = asset.FieldSize,
                SpawnInnerRadius = asset.SpawnInnerRadius,
                HeroSpeed = asset.HeroSpeed,
                EnemySpeed = asset.EnemySpeed,
                CellSize = asset.EffectiveCellSize,
                GridHalfExtent = asset.GridHalfExtent,
                SeparationRadius = asset.SeparationRadius,
                NeighborRadius = asset.NeighborRadius,
                CohesionWeight = asset.CohesionWeight,
                AlignmentWeight = asset.AlignmentWeight,
                SeekWeight = asset.SeekWeight,
                TurnRate = asset.TurnRate,
                SpeedVariance = asset.SpeedVariance,
                StopRadius = asset.StopRadius,
                ArriveRadius = asset.ArriveRadius,
                HeroCoreRadius = asset.HeroCoreRadius
            };
        }
    }
}
