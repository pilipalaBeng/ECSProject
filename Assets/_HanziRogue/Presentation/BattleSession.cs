using Unity.Entities;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 一局战斗的生命周期。整个工程里唯一写明「一局边界」的地方（ADR-0012）。
    ///
    /// 为什么需要它：ECS 默认 World 建在「进入 Play 会话」时，不建在「加载场景」时，
    /// 所以 <c>SceneManager.LoadScene</c> 不会清掉任何实体——「一局」在代码里原本没有对应物。
    /// 不建立这个边界，第二次进战斗就会出现 2 个 GameConfig，<c>GetSingleton</c> 每帧抛异常，
    /// 字潮与英雄全部定格（2026-10-07 实测，见 SceneFlowSmokeTest）。
    ///
    /// <c>Enter</c> 自己做清场，而不是指望调用方先调 <c>Exit</c>：重进、重开、异常恢复都走
    /// 同一个入口，少一个「忘了先退出」的失败模式。
    ///
    /// 本类同时是 managed 世界与 ECS 世界唯一的桥接点——System 保持 unmanaged，
    /// 不持有 UnityEngine.Object（宪法 §3.1：逻辑与表现分离）。
    /// </summary>
    public class BattleSession : MonoBehaviour
    {
        [SerializeField] private ScaleConfig config;

        [Tooltip("战斗数值。留空则一局只有移动、没有攻击——这里是唯一的配置入口，别在代码里另填一份。")]
        [SerializeField] private CombatConfig combatConfig;

        /// <summary>开局：清掉上一局残留，再把两份配置写进 ECS singleton。</summary>
        public void Enter()
        {
            if (config == null)
            {
                Debug.LogError("[BattleSession] ScaleConfig 未指定，战斗无法初始化。");
                return;
            }

            if (combatConfig == null)
            {
                Debug.LogError("[BattleSession] CombatConfig 未指定，战场不会有任何伤害。" +
                               "请在 Inspector 上挂 Data/CombatConfig。");
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[BattleSession] 找不到默认 World。");
                return;
            }

            Purge(world);
            CreateConfig(world, config);
            CreateCombatBalance(world, combatConfig);
            CreateProgress(world);
            Debug.Log($"[BattleSession] 战斗就绪，敌人目标数 {config.EnemyCount}。");
        }

        /// <summary>
        /// 收局：销毁本局全部实体。局外期间 ECS 系统仍挂在 PlayerLoop 上照跑，
        /// 不清场就是拿一整片字潮白烧 CPU（10 万实体时尤其明显）。
        /// 清掉实体也让表现层（相机 / HUD）自然回到空状态。
        /// </summary>
        public void Exit()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return;
            }

            Purge(world);
        }

        private static void CreateConfig(World world, ScaleConfig config)
        {
            EntityManager em = world.EntityManager;
            Entity configEntity = em.CreateEntity(typeof(GameConfig));
            em.SetComponentData(configEntity, new GameConfig
            {
                EnemyCount = config.EnemyCount,
                FieldSize = config.FieldSize,
                SpawnInnerRadius = config.SpawnInnerRadius,
                HeroSpeed = config.HeroSpeed,
                EnemySpeed = config.EnemySpeed,
                CellSize = config.EffectiveCellSize,
                GridHalfExtent = config.GridHalfExtent,
                SeparationRadius = config.SeparationRadius,
                NeighborRadius = config.NeighborRadius,
                CohesionWeight = config.CohesionWeight,
                AlignmentWeight = config.AlignmentWeight,
                SeekWeight = config.SeekWeight,
                TurnRate = config.TurnRate,
                SpeedVariance = config.SpeedVariance,
                StopRadius = config.StopRadius,
                ArriveRadius = config.ArriveRadius,
                HeroCoreRadius = config.HeroCoreRadius
            });
        }

        /// <summary>
        /// 战斗数值 → ECS singleton。
        /// 为什么不并进 <see cref="GameConfig"/>：那份是「规模与集群手感」的载体，已经有 16 个字段，
        /// 战斗数值是另一个会持续生长的关注点（后面还要接词条），混在一起必然变成配置垃圾桶。
        /// </summary>
        private static void CreateCombatBalance(World world, CombatConfig combat)
        {
            EntityManager em = world.EntityManager;
            Entity entity = em.CreateEntity(typeof(CombatBalance));
            em.SetComponentData(entity, new CombatBalance
            {
                HeroMaxHealth = combat.HeroMaxHealth,
                HeroAttackDamage = combat.HeroAttackDamage,
                HeroReach = combat.HeroReach,
                HeroAttackHalfWidth = combat.HeroAttackHalfWidth,
                HeroPointBlankRadius = combat.HeroPointBlankRadius,
                HeroStartupTime = combat.HeroStartupTime,
                HeroRecoverTime = combat.HeroRecoverTime,
                HeroPierceCap = combat.HeroPierceCap,
                HeroMoveScaleWhileAttacking = combat.HeroMoveScaleWhileAttacking,
                EnemyMaxHealth = combat.EnemyMaxHealth,
                EnemyAttackDamage = combat.EnemyAttackDamage,
                EnemyAttackInterval = combat.EnemyAttackInterval,
                EnemyAttackRange = combat.EnemyAttackRange,
                HitFlashDuration = combat.HitFlashDuration
            });
        }

        /// <summary>
        /// 本局进度 singleton。它是「这一局」在代码里的落点，
        /// 顺带承载了「本局生成过没有」——没有它就只能靠「场上还有没有兵」来猜，
        /// 而引入死亡之后那个猜法会是错的（杀光最后一波会当场重刷）。
        /// </summary>
        private static void CreateProgress(World world)
        {
            EntityManager em = world.EntityManager;
            Entity entity = em.CreateEntity(typeof(BattleProgress));
            em.SetComponentData(entity, new BattleProgress
            {
                SpawnDone = false,
                HeroDead = false,
                KillCount = 0
            });
        }

        /// <summary>
        /// 销毁一局战斗的全部实体：两个战斗 singleton + 配置 singleton + 英雄 + 兵。
        /// 英雄这里一并删掉——它由 EnemySpawnSystem 在清场后重新生成；只删兵的话，
        /// 上一局的英雄会留在原地，再进战斗时相机直接跟过去、画面跳一下。
        /// </summary>
        private static void Purge(World world)
        {
            EntityManager em = world.EntityManager;

            DestroyAll(em, typeof(GameConfig));
            DestroyAll(em, typeof(CombatBalance));
            DestroyAll(em, typeof(BattleProgress));

            var unitQuery = em.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new ComponentType[] { typeof(EnemyTag), typeof(HeroTag) }
            });
            em.DestroyEntity(unitQuery);
            unitQuery.Dispose();
        }

        private static void DestroyAll(EntityManager em, ComponentType type)
        {
            EntityQuery query = em.CreateEntityQuery(type);
            em.DestroyEntity(query);
            query.Dispose();
        }
    }
}
