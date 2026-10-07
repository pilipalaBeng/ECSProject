using Unity.Entities;
using Unity.Mathematics;
using HanziRogue.Core;

namespace HanziRogue.Gameplay
{
    /// <summary>
    /// 读取方向键 / WASD，写入 HeroInput 组件。
    /// 设计意图：输入意图与移动解耦——将来接手柄或触屏只需替换本 System。
    /// 注意：本 System 在主线程跑且不加 Burst，因为 UnityEngine.Input 是 managed API。
    /// </summary>
    public partial struct HeroInputSystem : ISystem
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

            foreach (var heroInput in SystemAPI.Query<RefRW<HeroInput>>())
            {
                heroInput.ValueRW.Move = move;
            }
        }
    }
}
