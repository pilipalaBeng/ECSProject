using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 按输入意图推进英雄位置，并约束在竞技场内。
    /// 逻辑与输入分离（HeroInputSystem 只写意图），便于后续插入冲刺/位移技能。
    /// 排序：必须早于 BoidsSystem——追兵每帧读的是本帧刚更新过的英雄位置，
    /// 否则整个字潮会追着上一帧的英雄跑（高速移动时肉眼可见的拖尾）。
    /// </summary>
    [UpdateBefore(typeof(BoidsSystem))]
    public partial struct HeroMoveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            GameConfig config = SystemAPI.GetSingleton<GameConfig>();

            // 出枪期间的移速系数。CombatBalance 可能不存在（只建了 GameConfig 的自检世界），
            // 那种情况按 1 处理：移动不该因为缺战斗配置就整体失效。
            float attackMoveScale = SystemAPI.HasSingleton<CombatBalance>()
                ? SystemAPI.GetSingleton<CombatBalance>().HeroMoveScaleWhileAttacking
                : 1f;

            state.Dependency = new HeroMoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ArenaHalfExtent = config.FieldSize,
                AttackMoveScale = math.clamp(attackMoveScale, 0f, 1f)
            }.Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct HeroMoveJob : IJobEntity
    {
        public float DeltaTime;
        public float ArenaHalfExtent;

        /// <summary>出枪期间（相位非 Idle）的移速系数。</summary>
        public float AttackMoveScale;

        public void Execute(ref Position2D pos, in HeroIntent intent, in MoveSpeed speed, in HeroAttackState attack)
        {
            float scale = attack.Phase == AttackPhase.Idle ? 1f : AttackMoveScale;
            pos.Value += intent.Move * speed.Value * scale * DeltaTime;

            // 没有边界的话玩家会一路跑出字潮包围圈，然后什么都看不见——
            // 场地边界不是限制自由，是保证「被追」这件事始终成立。
            float limit = math.max(0f, ArenaHalfExtent);
            pos.Value = math.clamp(pos.Value, new float2(-limit), new float2(limit));
        }
    }
}
