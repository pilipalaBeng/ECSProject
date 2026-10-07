using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 生成英雄与敌人集群。等 GameConfig singleton 就位后执行一次。
    /// 用「是否已存在敌人」判断是否生成过，不依赖 System 字段持久化。
    /// </summary>
    public partial struct EnemySpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var existing = SystemAPI.QueryBuilder().WithAll<EnemyTag>().Build();
            if (existing.CalculateEntityCount() > 0)
            {
                return;
            }

            GameConfig config = SystemAPI.GetSingleton<GameConfig>();
            EntityManager em = state.EntityManager;

            SpawnHero(em, config);
            SpawnEnemies(em, config);
        }

        private static void SpawnHero(EntityManager em, in GameConfig config)
        {
            EntityArchetype archetype = em.CreateArchetype(
                typeof(Position2D), typeof(Velocity2D), typeof(MoveSpeed),
                typeof(HeroTag), typeof(HeroInput));

            Entity hero = em.CreateEntity(archetype);
            em.SetComponentData(hero, new Position2D { Value = float2.zero });
            em.SetComponentData(hero, new Velocity2D { Value = float2.zero });
            em.SetComponentData(hero, new MoveSpeed { Value = config.HeroSpeed });
            em.SetComponentData(hero, new HeroInput { Move = float2.zero });
        }

        private static void SpawnEnemies(EntityManager em, in GameConfig config)
        {
            EntityArchetype archetype = em.CreateArchetype(
                typeof(Position2D), typeof(Velocity2D), typeof(MoveSpeed), typeof(EnemyTag));

            int count = math.max(1, config.EnemyCount);
            NativeArray<Entity> entities = em.CreateEntity(archetype, count, Allocator.Temp);

            var rnd = new Random(20261007u);
            float inner = math.max(0f, config.SpawnInnerRadius);
            float outer = math.max(inner + 0.1f, config.FieldSize);
            float innerSq = inner * inner;
            float outerSq = outer * outer;

            float variance = math.clamp(config.SpeedVariance, 0f, 0.5f);

            for (int i = 0; i < entities.Length; i++)
            {
                float speed = config.EnemySpeed * (1f + rnd.NextFloat(-variance, variance));
                em.SetComponentData(entities[i], new Position2D { Value = SampleAnnulus(ref rnd, innerSq, outerSq) });
                em.SetComponentData(entities[i], new Velocity2D { Value = float2.zero });
                em.SetComponentData(entities[i], new MoveSpeed { Value = speed });
            }

            entities.Dispose();
        }

        /// <summary>
        /// 在环形区域内取样。半径开方是为了让分布在面积上均匀——
        /// 直接对半径做线性取样会把单位挤到内圈，字潮一开始就糊成一团。
        /// rnd 必须按引用传：Unity.Mathematics.Random 是 struct，
        /// 按值传会把状态拷贝出去，每次调用都从同一起点取数，结果是 1000 个单位叠在同一点。
        /// </summary>
        private static float2 SampleAnnulus(ref Random rnd, float innerSq, float outerSq)
        {
            float angle = rnd.NextFloat(0f, math.PI * 2f);
            float radius = math.sqrt(rnd.NextFloat(innerSq, outerSq));
            return new float2(math.cos(angle), math.sin(angle)) * radius;
        }
    }
}
