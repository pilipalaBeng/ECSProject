using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 字潮渲染：分批 Graphics.DrawMeshInstanced 绘制「兵」字。
    /// 宪法 C4.1：禁止用 TextMeshPro 逐字渲染。C4.3：单批次上限 1023，超出分批。
    /// 职责单一——只负责把 Position2D 画成一坨字，不碰逻辑、不改 ECS 数据。
    /// </summary>
    public class InstancedEnemyRenderer : MonoBehaviour
    {
        private const int BatchCap = 1023;

        [Tooltip("单个字的世界尺寸（米）。")]
        [SerializeField] private float glyphSize = 1.1f;

        [Tooltip("抬离地面的高度，避免与地面 z-fighting。")]
        [SerializeField] private float surfaceOffset = 0.05f;

        [Tooltip("字形材质。由 SceneBuilder 装配；留空则退化为洋红纯色以便肉眼发现配置缺失。")]
        [SerializeField] private Material glyphMaterial;

        private Matrix4x4[] _matrices;
        private EntityQuery _query;
        private bool _queryReady;

        /// <summary>上一帧实际绘制的字数，供 HUD 与自检读取。</summary>
        public int LastRenderedCount { get; private set; }

        private void Awake()
        {
            _matrices = new Matrix4x4[BatchCap];

            if (glyphMaterial == null)
            {
                Debug.LogError("[InstancedEnemyRenderer] 字形材质未装配，字潮将不可读。");
            }
        }

        private void Update()
        {
            if (!EnsureQuery())
            {
                return;
            }

            // 技术债：每帧 TempJob 分配（Native，不产生 GC 堆分配）。
            // 规模稳定后可换成持久 NativeArray + ToComponentDataArrayAsync。
            var positions = _query.ToComponentDataArray<Position2D>(Allocator.TempJob);
            int total = positions.Length;
            LastRenderedCount = total;

            for (int offset = 0; offset < total; offset += BatchCap)
            {
                int count = Mathf.Min(BatchCap, total - offset);
                for (int i = 0; i < count; i++)
                {
                    float2 p = positions[offset + i].Value;
                    _matrices[i] = Matrix4x4.TRS(
                        new Vector3(p.x, surfaceOffset, p.y),
                        Quaternion.identity,
                        new Vector3(glyphSize, 1f, glyphSize));
                }

                Graphics.DrawMeshInstanced(
                    GlyphQuadMesh.Shared(), 0, glyphMaterial, _matrices, count);
            }

            positions.Dispose();
        }

        private bool EnsureQuery()
        {
            if (_queryReady)
            {
                return true;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return false;
            }

            _query = world.EntityManager.CreateEntityQuery(typeof(Position2D), typeof(EnemyTag));
            _queryReady = true;
            return true;
        }
    }
}
