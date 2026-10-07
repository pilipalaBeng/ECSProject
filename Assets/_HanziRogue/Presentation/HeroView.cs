using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 英雄表现：把英雄 Entity 的位置同步到挂着「英雄」二字贴图的 GameObject。
    /// 宪法 §3.1：表现层只读 ECS 数据，绝不回写。
    /// 职责单一——只管位置同步，字形尺寸/材质由 SceneBuilder 装配。
    /// </summary>
    public class HeroView : MonoBehaviour
    {
        [Tooltip("抬离地面的高度，需高于字潮渲染层，否则会被「兵」字盖住。")]
        [SerializeField] private float surfaceOffset = 0.12f;

        private EntityQuery _query;
        private bool _queryReady;

        private void Update()
        {
            if (!EnsureQuery())
            {
                return;
            }

            var positions = _query.ToComponentDataArray<Position2D>(Allocator.TempJob);
            if (positions.Length > 0)
            {
                float2 p = positions[0].Value;
                transform.position = new Vector3(p.x, surfaceOffset, p.y);
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

            _query = world.EntityManager.CreateEntityQuery(typeof(Position2D), typeof(HeroTag));
            _queryReady = true;
            return true;
        }
    }
}
