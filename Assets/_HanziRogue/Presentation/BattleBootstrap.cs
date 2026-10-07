using Unity.Entities;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 局内战斗引导：把 ScaleConfig (ScriptableObject) 写进 ECS 的 GameConfig singleton。
    /// 这是 managed 世界与 ECS 世界唯一的桥接点——System 保持 unmanaged，不持有 UnityEngine.Object。
    /// 宪法 §3.1：逻辑与表现分离。
    /// </summary>
    public class BattleBootstrap : MonoBehaviour
    {
        [SerializeField] private ScaleConfig config;

        private void Start()
        {
            if (config == null)
            {
                Debug.LogError("[BattleBootstrap] ScaleConfig 未指定，战斗无法初始化。");
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[BattleBootstrap] 找不到默认 World。");
                return;
            }

            EntityManager em = world.EntityManager;
            Entity configEntity = em.CreateEntity(typeof(GameConfig));
            em.SetComponentData(configEntity, new GameConfig
            {
                EnemyCount = config.EnemyCount,
                FieldSize = config.FieldSize,
                SpawnInnerRadius = config.SpawnInnerRadius,
                HeroSpeed = config.HeroSpeed,
                EnemySpeed = config.EnemySpeed,
                CellSize = config.CellSize,
                SeparationRadius = config.SeparationRadius,
                NeighborRadius = config.NeighborRadius,
                SeparationWeight = config.SeparationWeight,
                CohesionWeight = config.CohesionWeight,
                AlignmentWeight = config.AlignmentWeight,
                SeekWeight = config.SeekWeight,
                TurnRate = config.TurnRate,
                SpeedVariance = config.SpeedVariance
            });

            Debug.Log($"[BattleBootstrap] 配置就绪，敌人目标数 {config.EnemyCount}。");
        }
    }
}
