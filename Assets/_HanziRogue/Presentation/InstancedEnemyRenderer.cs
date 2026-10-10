using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>单实例数据：位置 + 闪白强度。与 shader 里的 GlyphInstance 布局一一对应（16 字节）。</summary>
    public struct GlyphInstanceData
    {
        public float3 Position;
        public float Flash;
    }

    /// <summary>
    /// 字潮渲染：一次 <c>DrawMeshInstancedIndirect</c> 画完所有「兵」字，每个字自带一份数据。
    /// 宪法 C4.1：禁止用 TextMeshPro 逐字渲染。C4.2：走 GPU instancing + per-instance 数据。
    /// 职责单一——只负责把 Position2D/HitFlash 画成一坨字，不碰逻辑、不改 ECS 数据。
    ///
    /// 为什么从 <c>DrawMeshInstanced</c> 换成 indirect：前者只能传矩阵数组，
    /// 想给某个字单独染色只能靠 MaterialPropertyBlock，而 MPB 是**整批统一**的——
    /// 做不出「这个字白了一下、那个字没有」。逐实例染色必须让每个实例自己带数据。
    /// 换过来的附带收益：C4.3 的 1023 分批上限消失（indirect 的实例数由 args 缓冲给，不设上限）。
    ///
    /// 矩阵构建 / 实例数据填充都走并行 job（ADR-0011）：主线程只剩等待与一次上传。
    /// </summary>
    public class InstancedEnemyRenderer : MonoBehaviour
    {
        /// <summary>单实例数据字节数：float3 + float。改结构体时必须同步改这里。</summary>
        private const int InstanceStride = 16;

        /// <summary>实例数据填充 job 的分片大小。太小会让调度开销盖过计算本身。</summary>
        private const int MatrixBatchSize = 256;

        /// <summary>容量增长系数。留 25% 余量，避免规模微调时反复重分配。</summary>
        private const float CapacitySlack = 1.25f;

        /// <summary>indirect 参数缓冲固定 5 个 uint：索引数 / 实例数 / 起始索引 / 基顶点 / 起始实例。</summary>
        private const int ArgsCount = 5;

        /// <summary>没有 CombatBalance 时的闪白兜底时长（秒）。只影响表现，不影响逻辑。</summary>
        private const float FallbackFlashDuration = 0.12f;

        [Tooltip("单个字的世界尺寸（米）。")]
        [SerializeField] private float glyphSize = 1.1f;

        [Tooltip("抬离地面的高度，避免与地面 z-fighting。")]
        [SerializeField] private float surfaceOffset = 0.05f;

        [Tooltip("indirect 剔除包围盒的半边长（米）。必须盖住整个战场，否则远处的字会被整批剔掉。")]
        [SerializeField] private float boundsHalfExtent = 200f;

        [Tooltip("字形材质。由 SceneBuilder 装配；留空则退化为洋红纯色以便肉眼发现配置缺失。")]
        [SerializeField] private Material glyphMaterial;

        // 字符串每帧查 shader 属性会有分配，属性 id 只解析一次
        private static readonly int InstanceBufferProperty = Shader.PropertyToID("_GlyphInstances");
        private static readonly int GlyphSizeProperty = Shader.PropertyToID("_GlyphSize");

        private ComputeBuffer _instanceBuffer;
        private ComputeBuffer _argsBuffer;
        private readonly uint[] _args = new uint[ArgsCount];

        private NativeArray<GlyphInstanceData> _nativeInstances;
        private int _capacity;

        private readonly EcsReadQuery _enemyQuery =
            new(typeof(Position2D), typeof(HitFlash), typeof(EnemyTag));

        private readonly EcsReadQuery _balanceQuery = new(typeof(CombatBalance));

        /// <summary>上一帧实际绘制的字数，供 HUD 与自检读取。</summary>
        public int LastRenderedCount { get; private set; }

        private void Awake()
        {
            if (glyphMaterial == null)
            {
                Debug.LogError("[InstancedEnemyRenderer] 字形材质未装配，字潮将不可读。");
            }
        }

        private void OnDestroy()
        {
            if (_instanceBuffer != null)
            {
                _instanceBuffer.Release();
                _instanceBuffer = null;
            }

            if (_argsBuffer != null)
            {
                _argsBuffer.Release();
                _argsBuffer = null;
            }

            if (_nativeInstances.IsCreated)
            {
                _nativeInstances.Dispose();
            }
        }

        private void Update()
        {
            if (glyphMaterial == null)
            {
                return;
            }

            if (!_enemyQuery.TryGet(out EntityQuery query))
            {
                return;
            }

            int total = query.CalculateEntityCount();
            LastRenderedCount = total;
            if (total == 0)
            {
                return;
            }

            EnsureBuffers(total);

            // 同步取快照。这一步会隐式完成所有写入 job——已在渲染前，无法再往后推。
            NativeArray<Position2D> positions = query.ToComponentDataArray<Position2D>(Allocator.TempJob);
            NativeArray<HitFlash> flashes = query.ToComponentDataArray<HitFlash>(Allocator.TempJob);

            new BuildGlyphInstancesJob
            {
                Positions = positions,
                Flashes = flashes,
                Out = _nativeInstances,
                Height = surfaceOffset,
                FlashDuration = FlashDuration()
            }.Schedule(total, MatrixBatchSize).Complete();

            positions.Dispose();
            flashes.Dispose();

            _instanceBuffer.SetData(_nativeInstances);
            _args[1] = (uint)total;
            _argsBuffer.SetData(_args);

            glyphMaterial.SetBuffer(InstanceBufferProperty, _instanceBuffer);
            glyphMaterial.SetFloat(GlyphSizeProperty, glyphSize);

            Mesh mesh = GlyphQuadMesh.Shared();
            Bounds bounds = new Bounds(Vector3.zero,
                new Vector3(boundsHalfExtent * 2f, 1f, boundsHalfExtent * 2f));

            // 一次画完，不再分批——indirect 的实例数上限由 args 决定，不受 1023 约束（C4.3 的解法）。
            Graphics.DrawMeshInstancedIndirect(mesh, 0, glyphMaterial, bounds, _argsBuffer);
        }

        /// <summary>
        /// 闪白时长取自 <see cref="CombatBalance"/>，表现层不复制一份。
        /// 拿不到时退回兜底值——宁画面难看，也不要因为缺配置就不画。
        /// </summary>
        private float FlashDuration()
        {
            if (!_balanceQuery.TryGet(out EntityQuery query))
            {
                return FallbackFlashDuration;
            }

            var balances = query.ToComponentDataArray<CombatBalance>(Allocator.TempJob);
            if (balances.Length == 0)
            {
                balances.Dispose();
                return FallbackFlashDuration;
            }

            float duration = balances[0].HitFlashDuration;
            balances.Dispose();
            return duration > 0f ? duration : FallbackFlashDuration;
        }

        private void EnsureBuffers(int required)
        {
            if (_nativeInstances.IsCreated && _capacity >= required && _instanceBuffer != null)
            {
                return;
            }

            int newCapacity = Mathf.Max(1024, Mathf.CeilToInt(required * CapacitySlack));

            if (_nativeInstances.IsCreated)
            {
                _nativeInstances.Dispose();
            }

            _nativeInstances = new NativeArray<GlyphInstanceData>(newCapacity, Allocator.Persistent);

            if (_instanceBuffer != null)
            {
                _instanceBuffer.Release();
            }

            _instanceBuffer = new ComputeBuffer(newCapacity, InstanceStride, ComputeBufferType.Structured);

            // 原生数组与 ComputeBuffer 必须等长：上传走的是「整块拷贝」重载，
            // 两边长度不一致是直接抛异常的，与其事后处理越界，不如在分配这一层锁死。
            if (_argsBuffer == null)
            {
                _argsBuffer = new ComputeBuffer(ArgsCount, sizeof(uint), ComputeBufferType.IndirectArguments);
            }

            Mesh mesh = GlyphQuadMesh.Shared();
            _args[0] = (uint)mesh.GetIndexCount(0);
            _args[1] = 0;
            _args[2] = (uint)mesh.GetIndexStart(0);
            _args[3] = (uint)mesh.GetBaseVertex(0);
            _args[4] = 0;

            _capacity = newCapacity;
        }
    }

    /// <summary>
    /// 并行填充每实例数据。只算「位置 + 闪白强度」——字形是统一朝向的轴对齐 quad，没有旋转，
    /// 所以不像矩阵方案那样要写 16 个 float，16 字节就能装下一个实例。
    ///
    /// 闪白用「剩余时间 / 总时长」线性衰减而不是非 0 即 1 的硬闪：
    /// 硬闪在连续挨打时会变成持续的白块，反而看不出是一次被打了几下。
    /// </summary>
    // 注意：Burst 1.8.x 编译本 job 时会报 "Burst internal compiler error: ... Could not find type ..."。
    // 那是 Burst 内部解析程序集引用表的故障，不是这里的代码写错——摘掉本行的 [BurstCompile]
    // 只是让日志少一条，job 照样退回托管执行、性能并不会变好。留着它，等 Burst 版本更新后自动受益。
    // 定位手段：临时摘掉某个 [BurstCompile] 再看错误条数有没有减少，能判断是哪几个 job 受影响。
    [BurstCompile]
    public struct BuildGlyphInstancesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Position2D> Positions;
        [ReadOnly] public NativeArray<HitFlash> Flashes;

        /// <summary>长度必须 ≥ Positions.Length。多出来的部分不动。</summary>
        [WriteOnly] public NativeArray<GlyphInstanceData> Out;

        public float Height;
        public float FlashDuration;

        public void Execute(int index)
        {
            float2 p = Positions[index].Value;
            float timer = Flashes[index].Timer;

            Out[index] = new GlyphInstanceData
            {
                Position = new float3(p.x, Height, p.y),
                Flash = timer <= 0f ? 0f : math.saturate(timer / FlashDuration)
            };
        }
    }
}
