using System;
using Unity.Entities;
using UnityEngine;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 表现层「只读查询」句柄：把「等 World 就绪 → 建 Query 一次 → 之后复用」这套样板收敛到一处。
    /// 这套样板原本被抄在 InstancedEnemyRenderer / BattleCamera / HeroView / HudController 四处，
    /// 每处都得自己挂一个 bool 再到处判空——首帧 World 尚未建立是常态，不能直接建查询。
    ///
    /// 构造参数故意收 `Type[]` 而不是 `ComponentType[]`：`Type → ComponentType` 的隐式转换
    /// 会去问 `TypeManager`，而 `TypeManager` 要等 ECS 世界初始化完毕才可用。
    /// 这四个 MonoBehaviours 都在**字段初始化器**里建查询，域重载时场景对象先于 TypeManager
    /// 被构造，于是每次重载稳定刷 4 条 NullReferenceException（栈指向 TypeManager.cs:1637）。
    /// 收 Type[] 后字段初始化器只存反射类型、零 ECS 参与，转换推迟到首次 TryGet——那时世界已就绪。
    ///
    /// 只读：本类不提供任何写入 ECS 的入口（宪法 §3.1 表现层只读不回写）。
    /// 类型数组在构造时分配一次，不在每帧热路径上（宪法 C5.4）。
    /// </summary>
    public sealed class EcsReadQuery
    {
        private readonly Type[] _types;
        private ComponentType[] _componentTypes;
        private EntityQuery _query;
        private bool _ready;

        public EcsReadQuery(params Type[] types)
        {
            _types = types;
        }

        /// <summary>
        /// 取出可用的查询。World 还没建立时返回 false —— 调用方直接 return 即可，不必自己判空。
        /// </summary>
        public bool TryGet(out EntityQuery query)
        {
            query = _query;
            if (_ready)
            {
                return true;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return false;
            }

            // 唯一一次做 Type → ComponentType 解析。放在这里而不是构造函数，
            // 是因为字段初始化器跑的时候 TypeManager 还没起来（见类注释）。
            if (_componentTypes == null)
            {
                _componentTypes = new ComponentType[_types.Length];
                for (int i = 0; i < _types.Length; i++)
                {
                    _componentTypes[i] = _types[i];
                }
            }

            _query = world.EntityManager.CreateEntityQuery(_componentTypes);
            _ready = true;
            query = _query;
            return true;
        }
    }
}
