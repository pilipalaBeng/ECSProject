using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 局内 HUD。宪法 §4.3：性能类信息必须实测可见——实体数与帧率直接显示在屏幕上。
    /// 最小闭环阶段用 IMGUI，避免引入 UGUI 依赖包。
    /// 职责单一——只读数据并显示，不操作相机、不改 ECS。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Tooltip("战场相机引用，由 SceneBuilder 装配。仅用于显示当前视野档位。")]
        [SerializeField] private BattleCamera battleCamera;

        private EntityQuery _enemyQuery;
        private bool _enemyQueryReady;
        private float _fpsAccum;
        private int _fpsFrames;
        private float _fps;
        private int _enemyCount;

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

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                SceneManager.LoadScene(SceneNames.Meta);
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 360, 170));
            GUILayout.Label($"兵 字实体数: {_enemyCount}");
            GUILayout.Label($"帧率: {_fps:F1} fps");
            GUILayout.Label("方向键 / WASD：移动英雄");
            GUILayout.Label(BattleCameraLabel());
            GUILayout.Space(6);
            if (GUILayout.Button("返回局外 (Esc)"))
            {
                SceneManager.LoadScene(SceneNames.Meta);
            }
            GUILayout.EndArea();
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
            if (!_enemyQueryReady)
            {
                World world = World.DefaultGameObjectInjectionWorld;
                if (world == null)
                {
                    return 0;
                }
                _enemyQuery = world.EntityManager.CreateEntityQuery(typeof(EnemyTag));
                _enemyQueryReady = true;
            }

            return _enemyQuery.CalculateEntityCount();
        }
    }
}
