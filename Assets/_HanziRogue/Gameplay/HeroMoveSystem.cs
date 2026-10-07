using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 按输入意图推进英雄位置，并约束在竞技场内。
    /// 逻辑与输入分离（HeroInputSystem 只写意图），便于后续插入冲刺/位移技能。
    /// </summary>
    public partial struct HeroMoveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            GameConfig config = SystemAPI.GetSingleton<GameConfig>();

            state.Dependency = new HeroMoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ArenaHalfExtent = config.FieldSize
            }.Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct HeroMoveJob : IJobEntity
    {
        public float DeltaTime;
        public float ArenaHalfExtent;

        public void Execute(ref Position2D pos, in HeroInput input, in MoveSpeed speed)
        {
            pos.Value += input.Move * speed.Value * DeltaTime;

            // 没有边界的话玩家会一路跑出字潮包围圈，然后什么都看不见——
            // 场地边界不是限制自由，是保证「被追」这件事始终成立。
            float limit = math.max(0f, ArenaHalfExtent);
            pos.Value = math.clamp(pos.Value, new float2(-limit), new float2(limit));
        }
    }
}
