using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 递减受击染色计时。逻辑侧不解释这个计时的含义——它只负责收到 0 就停，
    /// 「0 以上该显示成什么」是表现层的事（兵闪白 / 英雄闪红）。
    ///
    /// 单独成 System 的理由很实在：它是唯一一个「每帧都要遍历全部战斗单位」的战斗 System，
    /// 把它并进 ApplyDamageSystem 会让后者的职责变成「结算 + 维护」，以后想给结算加重逻辑就会被它拖累。
    ///
    /// 它**不依赖任何 singleton**——这一点是刻意的：
    /// 切进局内的那一帧，CombatBalance 可能还没写完，此时它只是把 0 减成 0 的空转，
    /// 不会让任何系统因缺配置而报错。
    /// </summary>
    public partial struct HitFlashDecaySystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new HitFlashDecayJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct HitFlashDecayJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(ref HitFlash flash)
        {
            flash.Timer = math.max(0f, flash.Timer - DeltaTime);
        }
    }
}
