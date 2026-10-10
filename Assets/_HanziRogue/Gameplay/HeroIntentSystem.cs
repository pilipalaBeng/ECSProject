using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 读取键盘 / 鼠标，写入 <see cref="HeroIntent"/>。
    /// 设计意图：输入意图与移动解耦——将来接手柄或触屏只需替换本 System。
    /// 注意：本 System 在主线程跑且不加 Burst，因为 UnityEngine.Input 是 managed API。
    /// 排序：必须早于 HeroMoveSystem，否则移动消费到的是上一帧的意图（一帧延迟）。
    ///
    /// 攻击键为什么是 J / 鼠标左键而不是空格：空格已经被 BattleCamera 的「切换视野」占用。
    /// 抢键会让老玩家的操作肌肉记忆崩掉，代价远大于选一个冷门键。
    /// 后续若要允许自定义，改这里一处即可。
    /// </summary>
    [UpdateBefore(typeof(HeroMoveSystem))]
    public partial struct HeroIntentSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float2 move = new float2(
                UnityEngine.Input.GetAxisRaw("Horizontal"),
                UnityEngine.Input.GetAxisRaw("Vertical"));

            // 斜向不应比直线快，所以超过单位长度就归一化
            if (math.lengthsq(move) > 1f)
            {
                move = math.normalize(move);
            }

            // 按住 = 连续突刺。割草手感的来源之一是「不用连点」。
            bool attack = UnityEngine.Input.GetKey(KeyCode.J)
                          || UnityEngine.Input.GetMouseButton(0);

            foreach (var intent in SystemAPI.Query<RefRW<HeroIntent>>())
            {
                intent.ValueRW.Move = move;
                intent.ValueRW.Attack = attack;
            }
        }
    }
}
