using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 由移动意图更新英雄朝向。
    ///
    /// 单独成一个 System 的理由：<c>HeroMoveSystem</c> 的职责是「位移」，把朝向塞进去
    /// 就让它同时干两件事（违反单一职责），而朝向有两个互不相关的消费方——
    /// 攻击判定（决定枪往哪扎）与武器动画（决定枪画在哪）。放在两者之外最省耦合。
    ///
    /// 没有输入时**保持上一帧朝向**：松手的一瞬间如果归零，武器会甩回默认方向，
    /// 而且下一次突刺就会朝一个玩家没看的方向刺出去。
    /// 初始朝向由 <c>CombatInitSystem</c> 兜底写为 (1, 0)。
    /// </summary>
    [UpdateBefore(typeof(HeroAttackSystem))]
    public partial struct HeroFacingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new UpdateFacingJob().ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct UpdateFacingJob : IJobEntity
    {
        public void Execute(in HeroIntent intent, ref Facing2D facing)
        {
            if (math.lengthsq(intent.Move) < 1e-6f)
            {
                return;
            }

            facing.Value = math.normalize(intent.Move);
        }
    }
}
