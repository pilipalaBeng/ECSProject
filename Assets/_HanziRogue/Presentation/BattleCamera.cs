using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 战场相机：正交俯视 + 平滑跟随英雄 + 多档视野循环切换。
    /// 为什么需要它：全场是 100x100 米，固定原点的相机在英雄跑开后只能看到空地——
    /// 跟随是「字潮追着走」这条体验成立的前置条件。
    /// 宪法 §3.1：表现层只读 ECS 英雄位置，不回写逻辑。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class BattleCamera : MonoBehaviour
    {
        [Tooltip("相机离地高度。正交投影下只影响裁剪平面，不影响画面大小。")]
        [SerializeField] private float cameraHeight = 60f;

        [Tooltip("视野档位（正交半径）。特写看清单字质感，全景看整片字潮的规模。")]
        [SerializeField] private float[] zoomLevels = { 9f, 18f, 55f };

        [Tooltip("视野档位名称，用于 HUD 显示，长度需与 zoomLevels 一致。")]
        [SerializeField] private string[] zoomLabels = { "特写", "近景", "全景" };

        [Tooltip("跟随平滑系数，越大跟得越紧。8 左右介于「黏住」和「松」之间。")]
        [SerializeField] private float followSharpness = 8f;

        [Tooltip("视野切换的过渡速度。")]
        [SerializeField] private float zoomSharpness = 6f;

        private Camera _camera;
        private readonly EcsReadQuery _heroQuery = new(typeof(Position2D), typeof(HeroTag));
        private int _zoomIndex = 1;

        /// <summary>当前视野档位名称。HUD 只读展示。</summary>
        public string ViewModeLabel
        {
            get
            {
                if (zoomLabels == null || zoomLabels.Length == 0 || _zoomIndex >= zoomLabels.Length)
                {
                    return string.Empty;
                }
                return zoomLabels[_zoomIndex];
            }
        }

        /// <summary>下一档名称，用于提示按键效果。</summary>
        public string NextViewModeLabel
        {
            get
            {
                if (zoomLabels == null || zoomLabels.Length == 0)
                {
                    return string.Empty;
                }
                return zoomLabels[(_zoomIndex + 1) % zoomLabels.Length];
            }
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = CurrentZoom;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.position = new Vector3(0f, cameraHeight, 0f);
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                _zoomIndex = (_zoomIndex + 1) % Mathf.Max(1, zoomLevels.Length);
            }

            _camera.orthographicSize = Mathf.Lerp(
                _camera.orthographicSize,
                CurrentZoom,
                1f - Mathf.Exp(-zoomSharpness * Time.deltaTime));

            if (TryGetHeroPosition(out Vector3 heroPos))
            {
                float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
                Vector3 target = new Vector3(heroPos.x, cameraHeight, heroPos.z);
                transform.position = Vector3.Lerp(transform.position, target, t);
            }
        }

        private float CurrentZoom
        {
            get
            {
                if (zoomLevels == null || zoomLevels.Length == 0)
                {
                    return 18f;
                }
                return zoomLevels[Mathf.Clamp(_zoomIndex, 0, zoomLevels.Length - 1)];
            }
        }

        private bool TryGetHeroPosition(out Vector3 position)
        {
            position = transform.position;

            if (!_heroQuery.TryGet(out EntityQuery query))
            {
                return false;
            }

            var positions = query.ToComponentDataArray<Position2D>(Allocator.TempJob);
            bool found = positions.Length > 0;
            if (found)
            {
                float2 p = positions[0].Value;
                position = new Vector3(p.x, 0f, p.y);
            }

            positions.Dispose();
            return found;
        }
    }
}
