using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HanziRogue.Core;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 英雄的持枪表现：一根细长 quad 跟着 <see cref="HeroAttackState"/> 的相位前后推进，
    /// 以及英雄的**伤害状态着色**（挨打闪红 / 阵亡褪成灰）。
    ///
    /// 为什么是独立的 MonoBehaviour 而不并进 <c>HeroView</c>：
    /// HeroView 的职责是「把 ECS 位置搬到 GameObject 位置」，属于地图同步，每帧无条件执行；
    /// 这里是「把 ECS 状态翻译成一段动画」，只在出枪期间才有内容。两者的变化频率与失败模式都不同
    /// （一个错了是英雄瞬移，一个错了是枪不会动，排查路径完全不一样）。
    ///
    /// **逻辑不认识动画，动画不认识伤害**：两边唯一的接口是 <see cref="HeroAttackState"/>
    /// 的 Phase 与 Timer 这两个数。将来要把这根细棍换成真正的枪（或者加挥砍、旋身），
    /// 只动这一个文件，ECS 侧一个字都不用改。
    ///
    /// 颜色通道也收敛在这一个文件里，不另开一个 MonoBehaviour：MaterialPropertyBlock 是
    /// **挂在 Renderer 上的一份共享状态**，两个组件各写一份就会互相覆盖，谁最后写谁赢。
    /// 一个写入方，是这个类存在的理由。
    ///
    /// 宪法 §3.1：只读 ECS，绝不回写。
    /// </summary>
    public class HeroWeaponView : MonoBehaviour
    {
        [Tooltip("武器子节点。由 SceneBuilder 装配（不是 GameObject.Find——那条路径太脆）。")]
        [SerializeField] private Transform weaponRoot;

        [Tooltip("「英雄」二字本身的 Renderer，受击时靠它闪红。")]
        [SerializeField] private MeshRenderer heroRenderer;

        [Header("姿态")]
        [Tooltip("常态时枪尾离英雄中心的距离（米）。0 = 枪尾正好握在英雄中心。" +
                 "刀身半长 1.4，所以常态下枪尖落在 2.8 米。")]
        [SerializeField] private float restDistance = 1.4f;

        [Tooltip("蓄势时枪回收到的距离（米）。比常态更近，才有「先收后刺」的落差——" +
                 "收到负值意味着枪尾缩进「英雄」二字里面，那是刻意的。")]
        [SerializeField] private float chargeDistance = 0.9f;

        [Tooltip("突刺帧枪伸到的距离（米）。**必须让枪尖正好落在 CombatConfig.HeroReach 上**：" +
                 "thrustDistance + 刀身半长(1.4) = HeroReach(3.6)，取 2.2。" +
                 "对不上就会出现「刀压着字却不掉血」，这条不变式是本项目踩过两次的坑。")]
        [SerializeField] private float thrustDistance = 2.2f;

        [Tooltip("枪离地的高度（米）。必须高于字潮层，否则会被「兵」字盖住。")]
        [SerializeField] private float weaponHeight = 0.16f;

        [Header("闪红")]
        [Tooltip("受击时混向的颜色。逻辑侧没有「红色」这个概念——那是纯粹的表现约定。")]
        [SerializeField] private Color flashColor = new Color(1f, 0.25f, 0.2f, 1f);

        [Header("阵亡")]
        [Tooltip("阵亡后字形褪到的颜色。逻辑侧同样没有「死亡色」，这是表现层的约定。" +
                 "刻意选低饱和灰而不是黑：黑会和地面底色糊在一起，看起来像「英雄被删了」。")]
        [SerializeField] private Color deathColor = new Color(0.42f, 0.40f, 0.44f, 1f);

        [Tooltip("阵亡褪色用时（秒）。设为 0 则瞬间变灰——那看起来像掉帧，不建议。")]
        [SerializeField] private float deathFadeTime = 0.5f;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int FlashColorProperty = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmountProperty = Shader.PropertyToID("_Flash");

        /// <summary>只用于哨兵探测：这是实例化版着色器的专有参数，单实例版没有它。</summary>
        private static readonly int GlyphSizeProperty = Shader.PropertyToID("_GlyphSize");

        private readonly EcsReadQuery _heroQuery =
            new(typeof(Position2D), typeof(HeroAttackState), typeof(Facing2D), typeof(HitFlash), typeof(HeroTag));

        private readonly EcsReadQuery _balanceQuery = new(typeof(CombatBalance));

        private readonly EcsReadQuery _progressQuery = new(typeof(BattleProgress));

        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor;
        private bool _baseColorCached;
        private float _deathBlend;

        /// <summary>
        /// 开局哨兵：英雄材质必须走**读 Transform** 的单实例着色器。
        ///
        /// 这是本项目最容易静默失效的一处——2026-10-09 / 10-10 连着两轮「英雄原地不动」都出在这里：
        /// 材质一旦被指回实例化着色器，字块就会被画死在世界原点、尺寸恒为 1.1 米，
        /// 而且**一个错都不报**，静态检查全过，只有盯截图像素才能发现。
        /// 与其每轮都靠反推，不如开局先喊一声。
        ///
        /// 判据用**能力探测**（有没有 `_GlyphSize`）而不是比着色器名字：`_GlyphSize` 是实例化版本的
        /// 专有参数，而名字属于资产选型、归 GlyphAssetBuilder 管，表现层不该复制一份。
        /// </summary>
        private void Start()
        {
            if (heroRenderer == null || heroRenderer.sharedMaterial == null)
            {
                return;
            }

            if (!heroRenderer.sharedMaterial.HasProperty(GlyphSizeProperty))
            {
                return;
            }

            Debug.LogError(
                $"[HeroView] 英雄材质的着色器是 {heroRenderer.sharedMaterial.shader.name}，" +
                "它按实例缓冲取位置、不读 Transform ——「英雄」二字会被画死在世界原点不动。" +
                "跑一次 HanziRogue/Build Minimal Scenes，或让编辑器重新导入 Mat_Glyph_Hero.mat。");
        }

        private void Update()
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (!_heroQuery.TryGet(out EntityQuery query) || query.CalculateEntityCount() == 0)
            {
                return;
            }

            var positions = query.ToComponentDataArray<Position2D>(Allocator.TempJob);
            var states = query.ToComponentDataArray<HeroAttackState>(Allocator.TempJob);
            var facings = query.ToComponentDataArray<Facing2D>(Allocator.TempJob);
            var flashes = query.ToComponentDataArray<HitFlash>(Allocator.TempJob);

            Position2D heroPos = positions[0];
            HeroAttackState state = states[0];
            Facing2D facing = facings[0];
            HitFlash flash = flashes[0];

            positions.Dispose();
            states.Dispose();
            facings.Dispose();
            flashes.Dispose();

            // 每帧只读一次配置：这里读的是 singleton，重复读等于重复一次 TempJob 分配
            CombatBalance balance = ReadBalance();
            bool heroDead = ReadHeroDead();
            AdvanceDeathBlend(heroDead);

            PlaceWeapon(heroPos, state, facing, balance, heroDead);
            ApplyHeroTint(flash, balance);
        }

        /// <summary>
        /// 按相位把枪摆到对应的前伸距离。相位只是一枚 byte，怎么解释它完全是表现层的事——
        /// 这也意味着数值改了（比如 Startup 变长）动画会自动跟着变慢，不用同步改两处。
        ///
        /// 武器节点**刻意不挂在 Hero 下面**：Hero 的缩放是 (3, 1, 1.5)（它是两个字宽），
        /// 子节点会继承这份缩放，一把 0.12×1.8 的枪会被拉成 0.36×2.7 的棒子。
        /// 所以这里直接用世界坐标落位，父子关系留给 SceneBuilder 去安排。
        /// </summary>
        private void PlaceWeapon(in Position2D heroPos, in HeroAttackState state, in Facing2D facing,
            in CombatBalance balance, bool heroDead)
        {
            Vector3 forward = new Vector3(facing.Value.x, 0f, facing.Value.y);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            // 阵亡后相位被冻在最后一帧（攻击系统整个停摆），枪会停在「半空」那个姿态上，
            // 看起来像卡死了。所以阵亡时直接收回常态位——枪是在手里，不是悬在半路。
            float distance = heroDead ? restDistance : ComputeDistance(in state, in balance);

            float heroHeight = heroRenderer != null ? heroRenderer.transform.position.y : 0f;
            Vector3 origin = new Vector3(heroPos.Value.x, heroHeight, heroPos.Value.y);
            Vector3 offset = forward * distance + new Vector3(0f, weaponHeight, 0f);

            weaponRoot.SetPositionAndRotation(origin + offset, Quaternion.LookRotation(forward));
        }

        private float ComputeDistance(in HeroAttackState state, in CombatBalance balance)
        {
            switch (state.Phase)
            {
                case AttackPhase.Startup:
                    return Mathf.Lerp(restDistance, chargeDistance,
                        NormalizeElapsed(state.Timer, balance.HeroStartupTime));

                case AttackPhase.Active:
                    return thrustDistance;

                case AttackPhase.Recover:
                    return Mathf.Lerp(thrustDistance, restDistance,
                        NormalizeElapsed(state.Timer, balance.HeroRecoverTime));

                default:
                    return restDistance;
            }
        }

        /// <summary>
        /// 英雄着色。走 MaterialPropertyBlock 而不是改 sharedMaterial——
        /// 后者会把颜色写进材质资产本身，下一次进游戏颜色就变了（这种脏资产很难查）。
        ///
        /// 两条通道各写各的：<c>_Color</c> 管底色（常态金橙 → 阵亡灰），
        /// <c>_Flash</c> 只负责受击那一层强弱。合成一个颜色让 C# 去算也能出画面，
        /// 但「正在闪红的时候被打死」这种叠加态就没有确定结果了——那恰好是最常见的死法。
        /// </summary>
        private void ApplyHeroTint(in HitFlash flash, in CombatBalance balance)
        {
            if (heroRenderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            if (!_baseColorCached)
            {
                _baseColor = heroRenderer.sharedMaterial != null
                    ? heroRenderer.sharedMaterial.color
                    : Color.white;
                _baseColorCached = true;
            }

            float duration = balance.HitFlashDuration > 0f ? balance.HitFlashDuration : 0.12f;
            float intensity = flash.Timer <= 0f ? 0f : Mathf.Clamp01(flash.Timer / duration);

            heroRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(ColorProperty, Color.Lerp(_baseColor, deathColor, _deathBlend));
            _propertyBlock.SetColor(FlashColorProperty, flashColor);
            _propertyBlock.SetFloat(FlashAmountProperty, intensity);
            heroRenderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// 阵亡褪色的推进。缓动而不是开关式切换：0.5 秒的灰化读得出「这一下是致命的」，
        /// 瞬变只会被当成掉帧。用 <c>MoveTowards</c> 而不是 <c>Lerp(…, 常量)</c>，
        /// 是为了让总时长与帧率无关（后者的收敛速度会随帧率变）。
        /// </summary>
        private void AdvanceDeathBlend(bool heroDead)
        {
            float step = deathFadeTime > 0f ? Time.deltaTime / deathFadeTime : 1f;
            _deathBlend = Mathf.MoveTowards(_deathBlend, heroDead ? 1f : 0f, step);
        }

        /// <summary>
        /// 本局英雄是否已阵亡。<c>BattleProgress.HeroDead</c> 是唯一判据来源——
        /// 表现层不自己判断「血量是不是 ≤ 0」，那会把「未初始化（Max=0）」也算成死亡，
        /// 开局第一帧整块字就会是灰的。
        /// </summary>
        private bool ReadHeroDead()
        {
            if (!_progressQuery.TryGet(out EntityQuery query) || query.CalculateEntityCount() == 0)
            {
                return false;
            }

            var progresses = query.ToComponentDataArray<BattleProgress>(Allocator.TempJob);
            bool dead = progresses[0].HeroDead;
            progresses.Dispose();
            return dead;
        }

        private CombatBalance ReadBalance()
        {
            if (!_balanceQuery.TryGet(out EntityQuery query))
            {
                return default;
            }

            var balances = query.ToComponentDataArray<CombatBalance>(Allocator.TempJob);
            if (balances.Length == 0)
            {
                balances.Dispose();
                return default;
            }

            CombatBalance balance = balances[0];
            balances.Dispose();
            return balance;
        }

        private static float NormalizeElapsed(float timer, float duration)
        {
            return duration > 0f ? Mathf.Clamp01(timer / duration) : 1f;
        }
    }
}
