using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 局内 HUD。宪法 §4.3：性能类信息必须实测可见——实体数与帧率直接显示在屏幕上。
    /// 最小闭环阶段用 IMGUI，避免引入 UGUI 依赖包。
    /// 职责单一——只读数据并显示，不操作相机、不改 ECS。
    /// 挂在 BattleRoot 下，局外模式整棵树失活，所以 Esc 天然只在局内有效。
    ///
    /// 为什么英雄血**必须**有条状图而不只是数字：这场仗里玩家的视线钉在屏幕中心的英雄身上，
    /// 只给数字就等于要求玩家不停把视线挪到左上角去读。血条是余光就能读出危险程度的形状，
    /// 数字只是它的补充。同理，「阵亡」也不该是行小字——游戏结束了就该有结束的样子，
    /// 否则玩家看到英雄变灰会以为是渲染出问题，而不是自己死了。
    ///
    /// 所有读数都只用于显示：本类不提供任何写回 ECS 的入口（宪法 §3.1）。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Tooltip("战场相机引用，由 SceneBuilder 装配。仅用于显示当前视野档位。")]
        [SerializeField] private BattleCamera battleCamera;

        [Tooltip("模式切换器，由 SceneBuilder 装配。结算面板的「重开一局」也走它。")]
        [SerializeField] private ModeSwitcher switcher;

        private readonly EcsReadQuery _enemyQuery = new(typeof(EnemyTag));
        private readonly EcsReadQuery _heroHealthQuery = new(typeof(Health), typeof(HeroTag));
        private readonly EcsReadQuery _progressQuery = new(typeof(BattleProgress));
        private readonly EcsReadQuery _configQuery = new(typeof(GameConfig));

        /// <summary>
        /// 攻击状态。存在的唯一目的是**自证**：这个项目连着两轮栽在「玩家以为在攻击、
        /// 实际上按键没进去 / 相位没起来」上，而这两种情况在画面上完全一样（枪该动还是动）。
        /// 把「键按没按 + 相位到哪了」摊在屏幕上，一眼就能分清是输入问题还是判定问题。
        /// </summary>
        private readonly EcsReadQuery _heroAttackQuery =
            new(typeof(HeroAttackState), typeof(HeroIntent), typeof(HeroTag));

        private float _fpsAccum;
        private int _fpsFrames;
        private float _fps;
        private int _enemyCount;
        private int _enemyTotal;
        private float _heroHealth;
        private float _heroMaxHealth;
        private int _killCount;
        private bool _heroDead;
        private bool _combatReady;
        private byte _attackPhase;
        private bool _attackPressed;

        // 结算面板的样式：GUIStyle 只能在 OnGUI 里从 GUI.skin 派生，而且不能每帧重建
        private GUIStyle _titleStyle;
        private GUIStyle _centerStyle;

        private void Update()
        {
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.5f)
            {
                _fps = _fpsFrames / _fpsAccum;
                _fpsAccum = 0f;
                _fpsFrames = 0;
            }

            _enemyCount = CountEnemies();
            ReadCombatState();
            ReadAttackState();

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                ReturnToMeta();
            }

            // 阵亡之后唯一的出口是「重开一局」。只给按钮是不够的：玩家这时手还在 J 上，
            // 而「按了半天没反应」正是这一局卡住的典型状态，用键盘给一条最短的重开路径。
            // R 与移动 / 攻击 / 切视野都不冲突。
            if (_heroDead && UnityEngine.Input.GetKeyDown(KeyCode.R))
            {
                RestartBattle();
            }
        }

        private void OnGUI()
        {
            DrawStatusPanel();

            if (_heroDead)
            {
                DrawGameOver();
            }
        }

        /// <summary>左上角状态面板。战斗没就绪时不画半截数据——宁缺毋滥，免得读到过期值。</summary>
        private void DrawStatusPanel()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 400f, 320f));

            if (_combatReady)
            {
                GUILayout.Label("英雄血量");
                DrawBar(GUILayoutUtility.GetRect(372f, 20f), HealthRatio(), HealthColor());
                GUILayout.Label($"　{HealthText()}");

                string total = _enemyTotal > 0 ? $" / {_enemyTotal}" : string.Empty;
                GUILayout.Label($"剩余兵 {_enemyCount}{total}　　击杀 {_killCount}");
            }

            GUILayout.Label($"帧率 {_fps:F1} fps");
            GUILayout.Label(AttackLabel());
            GUILayout.Label("方向键 / WASD：移动英雄");
            GUILayout.Label("J / 鼠标左键：突刺");
            GUILayout.Label(BattleCameraLabel());
            GUILayout.Space(6f);

            if (GUILayout.Button("返回局外 (Esc)"))
            {
                ReturnToMeta();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// 结算面板：一层压暗的全屏遮罩 + 居中的「游戏结束」。
        /// 遮罩的作用是把战场压下去——玩家不会一边读结算一边还在盯战场，压暗能明确划出「结束了」。
        /// </summary>
        private void DrawGameOver()
        {
            EnsureStyles();

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            var panel = new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.40f, 400f, 170f);
            GUILayout.BeginArea(panel);

            GUILayout.Label("游 戏 结 束", _titleStyle);
            GUILayout.Label($"英雄阵亡　　击杀 {_killCount} 个「兵」", _centerStyle);
            GUILayout.Space(16f);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重开一局", GUILayout.Width(148f), GUILayout.Height(34f)))
            {
                RestartBattle();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label("（R 键重开一局　Esc 返回局外）", _centerStyle);

            GUILayout.EndArea();
        }

        /// <summary>
        /// 一根填充条。用 <c>Texture2D.whiteTexture</c> + <c>GUI.color</c> 染色，
        /// 不引入任何贴图资产——血条的形状信息全在宽度里，颜色只是刻度。
        /// </summary>
        private static void DrawBar(Rect rect, float fill, Color fillColor)
        {
            Color previous = GUI.color;

            GUI.color = new Color(0.06f, 0.06f, 0.07f, 0.85f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            float clamped = Mathf.Clamp01(fill);
            if (clamped > 0f)
            {
                GUI.color = fillColor;
                GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f,
                    (rect.width - 2f) * clamped, rect.height - 2f), Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        /// <summary>
        /// 攻击状态读数。故意写成文字而不是图标：这一行的用途是排查，不是好看。
        /// 「未按」说明键没进到 ECS（输入层）；「已按」而相位一直停在待机说明相位机没跑（逻辑层）。
        /// 这两种故障以前在画面上完全一样，只能靠反推。
        ///
        /// 括号里那句「先点一下 Game 窗口」是**实测踩出来的**：编辑器里 legacy Input 只送给
        /// 有焦点的 Game 视图，玩家在 Scene 视图里按 J 时这里会一直显示「未按」——
        /// 那既不是输入层坏了也不是判定坏了，纯粹是焦点没给对。不写出来就会被当成 bug 查半天。
        /// </summary>
        private string AttackLabel()
        {
            if (!_attackPressed)
            {
                return "突刺 J：未按（先点一下 Game 窗口再按）";
            }

            switch (_attackPhase)
            {
                case AttackPhase.Startup:
                    return "突刺 J：蓄势";
                case AttackPhase.Active:
                    return "突刺 J：出枪（判定帧）";
                case AttackPhase.Recover:
                    return "突刺 J：收枪";
                default:
                    return "突刺 J：已按，等待起手";
            }
        }

        private void ReadAttackState()
        {
            if (!_heroAttackQuery.TryGet(out EntityQuery query) || query.CalculateEntityCount() == 0)
            {
                _attackPressed = false;
                return;
            }

            var states = query.ToComponentDataArray<HeroAttackState>(Allocator.TempJob);
            var intents = query.ToComponentDataArray<HeroIntent>(Allocator.TempJob);

            _attackPhase = states[0].Phase;
            _attackPressed = intents[0].Attack;

            states.Dispose();
            intents.Dispose();
        }

        private float HealthRatio()
        {
            return _heroMaxHealth > 0f ? Mathf.Clamp01(_heroHealth / _heroMaxHealth) : 0f;
        }

        /// <summary>
        /// 血量→颜色。满血金橙（跟英雄字同色，一眼对得上是「我」），残血转朱砂。
        /// 颜色本身就是刻度，所以哪怕玩家不读数字，余光扫一眼也知道该跑了。
        /// </summary>
        private Color HealthColor()
        {
            if (_heroDead)
            {
                return new Color(0.40f, 0.13f, 0.13f, 1f);
            }

            return Color.Lerp(new Color(0.86f, 0.20f, 0.16f, 1f),
                              new Color(1f, 0.78f, 0.30f, 1f),
                              HealthRatio());
        }

        private string HealthText()
        {
            return _heroMaxHealth > 0f
                ? $"{_heroHealth:F0} / {_heroMaxHealth:F0}"
                : "—";
        }

        /// <summary>
        /// 三个读数分开查询而不是合成一个大查询：它们属于不同的 singleton / archetype，
        /// 硬凑成一个会让「只有血量没有进度」这种中间态整块读不出来。
        /// </summary>
        private void ReadCombatState()
        {
            _combatReady = _heroHealthQuery.TryGet(out EntityQuery healthQuery)
                           && healthQuery.CalculateEntityCount() > 0;

            if (!_combatReady)
            {
                return;
            }

            var healths = healthQuery.ToComponentDataArray<Health>(Allocator.TempJob);
            _heroHealth = healths[0].Value;
            _heroMaxHealth = healths[0].Max;
            healths.Dispose();

            if (_progressQuery.TryGet(out EntityQuery progressQuery) &&
                progressQuery.CalculateEntityCount() > 0)
            {
                var progresses = progressQuery.ToComponentDataArray<BattleProgress>(Allocator.TempJob);
                _killCount = progresses[0].KillCount;
                _heroDead = progresses[0].HeroDead;
                progresses.Dispose();
            }

            _enemyTotal = ReadEnemyTotal();
        }

        /// <summary>
        /// 本局目标兵数，用于把「剩余兵 734」读成「还剩 734 / 1000」。
        /// 少了分母，玩家看不出自己打了多少——这个数字是整场战斗唯一的进度条。
        /// </summary>
        private int ReadEnemyTotal()
        {
            if (!_configQuery.TryGet(out EntityQuery query) || query.CalculateEntityCount() == 0)
            {
                return 0;
            }

            var configs = query.ToComponentDataArray<GameConfig>(Allocator.TempJob);
            int total = configs[0].EnemyCount;
            configs.Dispose();
            return total;
        }

        private string BattleCameraLabel()
        {
            if (battleCamera == null)
            {
                return "Space：切换视野（相机未装配）";
            }

            return $"视野：{battleCamera.ViewModeLabel}　Space → {battleCamera.NextViewModeLabel}";
        }

        private int CountEnemies()
        {
            return _enemyQuery.TryGet(out EntityQuery query) ? query.CalculateEntityCount() : 0;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = new Color(0.92f, 0.34f, 0.28f, 1f);

            _centerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            _centerStyle.normal.textColor = new Color(0.88f, 0.86f, 0.82f, 1f);
        }

        /// <summary>
        /// 重开一局。走 <see cref="ModeSwitcher.EnterBattle"/> 而不是另开一个重置入口：
        /// 它内部会调到 <c>BattleSession.Enter</c>，而后者**先清场再建局**，
        /// 所以「重开」与「首次进战斗」本来就是同一条路径，不需要第二套重置逻辑。
        /// </summary>
        private void RestartBattle()
        {
            if (switcher == null)
            {
                Debug.LogError("[HudController] ModeSwitcher 未装配，无法重开。");
                return;
            }

            switcher.EnterBattle();
        }

        private void ReturnToMeta()
        {
            if (switcher == null)
            {
                Debug.LogError("[HudController] ModeSwitcher 未装配，无法返回局外。");
                return;
            }

            switcher.EnterMeta();
        }
    }
}
