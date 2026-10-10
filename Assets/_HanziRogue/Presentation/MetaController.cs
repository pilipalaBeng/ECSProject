using UnityEngine;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 局外模式。最小闭环阶段只承担「入口」职责：显示状态 + 进入战斗。
    /// 后续承接构筑、图鉴、关卡选择。
    /// 模式切换走 <see cref="ModeSwitcher"/>，不再 LoadScene——局内局外是同一场景的两种模式（ADR-0012）。
    /// </summary>
    public class MetaController : MonoBehaviour
    {
        [Tooltip("模式切换器，由 SceneBuilder 装配。")]
        [SerializeField] private ModeSwitcher switcher;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 460, 240));
            GUILayout.Label("HanziRogue · 局外（最小闭环）");
            GUILayout.Label("用正确的字，把一整片错误的字扫成漫天笔画。");
            GUILayout.Space(8);
            GUILayout.Label("局内：ECS 生成敌人集群，Boids 三规则 + 追击英雄");
            GUILayout.Label("操作：方向键 / WASD 移动英雄，Esc 返回局外");
            GUILayout.Space(12);
            if (GUILayout.Button("进入战斗"))
            {
                EnterBattle();
            }
            GUILayout.EndArea();
        }

        private void EnterBattle()
        {
            if (switcher == null)
            {
                Debug.LogError("[MetaController] ModeSwitcher 未装配，无法进入战斗。");
                return;
            }

            switcher.EnterBattle();
        }
    }
}
