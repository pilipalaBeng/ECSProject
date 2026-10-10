using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 局内 / 局外模式切换：两组根节点互斥显隐，战斗实体的生死交给 <see cref="BattleSession"/>。
    ///
    /// 为什么不用两个场景：ECS 的 World 是全局常驻的，加载场景不会重建它，于是
    /// 「场景边界」与「状态边界」错开——场景卸载了、实体还在，局内局外并不独立。
    /// 一个场景内用 SetActive 切换两组内容，保留表现层的整体拆装，
    /// 同时把切换成本从「加载场景」降到「切激活」（ADR-0012）。
    ///
    /// 职责单一：只管模式与根节点，不碰 ECS、不碰相机参数。
    /// </summary>
    public class ModeSwitcher : MonoBehaviour
    {
        [SerializeField] private GameObject metaRoot;
        [SerializeField] private GameObject battleRoot;
        [SerializeField] private BattleSession session;

        /// <summary>当前模式。表现层若要按模式分支，读这里，而不是猜根节点的激活状态。</summary>
        public GameMode Current { get; private set; } = GameMode.Meta;

        private void Awake()
        {
            // 场景装载时以局外为准，顺带清掉可能残留的战斗实体。
            Apply(GameMode.Meta);
        }

        /// <summary>进入战斗：显示 BattleRoot，并让 BattleSession 建立本局实体。</summary>
        public void EnterBattle()
        {
            Apply(GameMode.Battle);
        }

        /// <summary>返回局外：显示 MetaRoot，并让 BattleSession 销毁本局实体。</summary>
        public void EnterMeta()
        {
            Apply(GameMode.Meta);
        }

        private void Apply(GameMode mode)
        {
            Current = mode;
            bool inBattle = mode == GameMode.Battle;

            if (metaRoot != null)
            {
                metaRoot.SetActive(!inBattle);
            }

            if (battleRoot != null)
            {
                battleRoot.SetActive(inBattle);
            }

            if (session == null)
            {
                return;
            }

            if (inBattle)
            {
                session.Enter();
            }
            else
            {
                session.Exit();
            }
        }
    }
}
