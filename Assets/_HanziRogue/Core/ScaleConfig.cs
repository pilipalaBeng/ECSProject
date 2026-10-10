using UnityEngine;

namespace HanziRogue.Core
{
    /// <summary>
    /// 规模与手感配置。宪法 C5.6：规模常量集中管理，禁止散落在 System 里的魔数。
    /// 每个字段的 rationale 写在注释里；未经实测的一律是 [PLACEHOLDER]。
    /// </summary>
    [CreateAssetMenu(menuName = "HanziRogue/Scale Config", fileName = "ScaleConfig")]
    public class ScaleConfig : ScriptableObject
    {
        [Header("规模")]
        [Tooltip("敌人数量。当前验证目标：1000 个「兵」追 1 个「英雄」。低于 CONSTRAINTS C1.1 的 16500 上限。")]
        public int EnemyCount = 1000;

        [Tooltip("竞技场半边长（米）。英雄被约束在 ±FieldSize 内；敌人生成外径也取这个值。[PLACEHOLDER]")]
        public float FieldSize = 50f;

        [Tooltip("敌人生成环内径（米）。环形取样而非铺满，字潮才会从外向内「涌」过来；" +
                 "太小会让英雄开局就被贴脸围死，太大会让近景视野里一个兵都没有。[PLACEHOLDER]")]
        public float SpawnInnerRadius = 8f;

        [Header("移动")]
        [Tooltip("英雄速度。要略快于敌人，否则玩家永远甩不掉，权力幻想不成立。")]
        public float HeroSpeed = 14f;

        [Tooltip("敌人速度。低于英雄速度是设计意图：能追、能围，但追不上。")]
        public float EnemySpeed = 8f;

        [Header("集群与追击")]
        [Tooltip("格子边长系数：CellSize = SeparationRadius × 本值（ADR-0011）。\n" +
                 "硬约束：CellSize ≥ SeparationRadius，即本值 ≥ 1，否则 3×3 扫描盖不住查询半径、会漏邻居。\n" +
                 "推导（原版 ADR 在这里推错过，0.7 就是这么来的）：\n" +
                 "  实体在格内位置 a ∈ [0, s)，邻居偏移 δ ∈ [-R, R]，格偏移 = floor((a+δ)/s)。\n" +
                 "  正方向 sup = 1 + R/s，负方向 inf = -R/s。两者都要落进 {-1, 0, 1}：\n" +
                 "  正方向要求 R ≤ s（这才是约束），负方向只要 R < 2s。\n" +
                 "  原版用「实体在格中心」算成 1.5 × CellSize ≥ R，忽略了实体可以贴格子边缘。\n" +
                 "取 1.0 是 3×3 扫描下的最优：格子再小就得改 5×5（25 格），候选数反而更多。\n" +
                 "这是让「每实体候选邻居数」与总体密度解耦的关键——固定 3 米格子、按环带密度 13/m² 估：\n" +
                 "  3.0 米：每格约 118 个，每实体扫约 1058 个候选，三趟扫描约 3.2 亿次/帧（ADR-0011 的事故现场）；\n" +
                 "  1.15 米：每格约 17 个，每实体扫约 155 个候选，三趟扫描约 4700 万次/帧。")]
        public float CellSizeFactor = 1f;

        [Tooltip("字间距（米）：两个「兵」字的中心不得小于这个距离。\n" +
                 "由位置层硬约束保证（ResolveOverlapJob），不是速度层的分离力——" +
                 "软力在 1000 单位的密度下会被压穿，实测最近能压到 0.03 米、字全糊成一块色块。\n" +
                 "取值需 >= 单字世界尺寸（1.1 米），否则会互相压字、看不出是字。")]
        public float SeparationRadius = 1.15f;

        [Tooltip("邻居感知半径：参与聚合与对齐的范围。")]
        public float NeighborRadius = 2.5f;

        [Tooltip("聚合权重：向邻居质心靠拢。**默认 0**——Boids 的聚合是为鸟群自组织设计的，" +
                 "用在追杀玩家的敌人身上会让它们绕着玩家盘旋而不是扑上来（ADR-0008）。非 0 仅供 A/B 对比。")]
        public float CohesionWeight = 0f;

        [Tooltip("对齐权重：与邻居速度趋同。**默认 0**——同聚合，会形成旋臂/绕圈观感（ADR-0008）。")]
        public float AlignmentWeight = 0f;

        [Tooltip("追击权重：指向英雄。这是「追着打」的核心项，权重最高。")]
        public float SeekWeight = 2.2f;

        [Tooltip("转向速率（弧度/秒）：每秒最多把方向掰过来多少。" +
                 "复刻幸存者类时的实测区间是 3~5——越小越迟钝，玩家拉扯空间越大；越大越贴脸。[PLACEHOLDER]")]
        public float TurnRate = 5f;

        [Tooltip("每个兵的速度随机幅度（±比例）。0.12 即速度在 EnemySpeed 的 88%~112% 之间。" +
                 "全场同速会像一块刚性板整体平移，看不到字潮的层次。")]
        public float SpeedVariance = 0.12f;

        [Header("贴身（抵达）")]
        [Tooltip("贴身半径（米）：既是追击力的消失点，也是英雄的硬核半径。\n" +
                 "① 追击侧：进到这个距离 seek 权重归零，兵不再往前冲。\n" +
                 "   绕圈挡不住不是转向不够快——转向半径 = 速度/转向率 = 8/5 = 1.6 米，\n" +
                 "   怎么转都在绕，只能靠「力在贴身处消失」来解决。\n" +
                 "② 碰撞侧：兵不许钻进英雄体内，否则字压住英雄，看不清谁是谁。\n" +
                 "取值参照：英雄 quad 3.0x1.5、兵 1.1x1.1。上下方向贴边距 1.3、" +
                 "左右方向 2.05，取 1.5 让左右略微压住英雄，观感是「咬住」不是「站岗」。")]
        public float StopRadius = 1.5f;

        [Tooltip("减速区外缘（米）：进入此距离开始线性收速，到 StopRadius 收为 0。\n" +
                 "太短会急刹（贴身瞬间一顿一顿），太长则老远就开始减速、字潮显得拖沓。" +
                 "默认 StopRadius + 2。[PLACEHOLDER]")]
        public float ArriveRadius = 3.5f;

        [Tooltip("英雄碰撞硬核半径（米）：兵中心绝对不许进到这个距离以内。\n" +
                 "必须明显小于 StopRadius——两者之间那段是位置解算的活动空间。\n" +
                 "压成同一个值会死锁：内层兵被外层挤压、推力合力向内，" +
                 "一帧就被投影拉回环上，越挤越糊（实测 254 个兵叠在 2 米内、字间距 0.00）。")]
        public float HeroCoreRadius = 0.9f;

        /// <summary>
        /// 实际格子边长。由 SeparationRadius 推导而非手填——手填与半径脱钩是 ADR-0011 记录的事故来源
        /// （固定 3 米格子 + 密度涨 100 倍 → 每格 118 个实体 → 复杂度从 O(n·k) 退化成 O(n²/A)）。
        /// </summary>
        public float EffectiveCellSize => SeparationRadius * CellSizeFactor;

        /// <summary>
        /// 空间网格要覆盖的世界半边长。比 FieldSize 大一圈：
        /// 敌人不受场地边界钳制（约束只加在英雄身上），外圈字会被挤到场地外，
        /// 留 8 个字间距的余量避免它们全被 clamp 到最外圈格子、把那一格的候选数顶爆。
        /// </summary>
        public float GridHalfExtent => FieldSize + SeparationRadius * 8f;

        /// <summary>
        /// 场地在「字间距」约束下装得下的实体上限。
        /// 单实体占位 = (√3/2) × SeparationRadius²（六方密堆），可用面积 = π × FieldSize²。
        ///
        /// 口径说明：**不扣生成环内径**。敌人生成在环形区，但会一路挤进内圈贴身，
        /// 最终占据的是整块场地。实测校准：±50 场地 → 本式得 6,858，
        /// 而 7,000 实体实测全场占用 1.02 倍、5,000 为 0.73 倍，对得上。
        ///
        /// 这不是性能指标，是**几何判据**：超过它，字潮在面积上就摆不开了，
        /// 位置解算永远收敛不了——不是迭代次数不够，是装不下。
        /// 实测（2026-10-07，±50 场地、1.15 米间距，看「全场最近点对」，设计值 1.15）：
        ///   5,000（0.73x）1.033 米，健康；7,000（1.02x）0.392 米，开始劣化；
        ///   10,000（1.46x）0.016 米，完全重合、字糊成实心色块。
        /// 同一批 10,000 实体换到 ±120 场地（0.25x）立刻恢复到 0.968 米——**根因是面积，不是代码**。
        /// 所以 EnemyCount 必须与 FieldSize、SeparationRadius 联动调整，三者不能各改各的。
        /// </summary>
        public float PhysicalCapacity
        {
            get
            {
                float perEntity = 0.8660254f * SeparationRadius * SeparationRadius;
                return Mathf.PI * FieldSize * FieldSize / perEntity;
            }
        }

        /// <summary>
        /// 要把 count 个实体按设计字间距摆开，场地半边长至少需要多少米。
        /// 与 `PhysicalCapacity` 互为反函数，把「超容量」这个抽象判断变成可直接抄进 Inspector 的数字：
        /// `RequiredFieldSizeFor(12000)` → 约 66 米（对应当前 ±50）。
        /// </summary>
        public float RequiredFieldSizeFor(int count)
        {
            float perEntity = 0.8660254f * SeparationRadius * SeparationRadius;
            return Mathf.Sqrt(count * perEntity / Mathf.PI);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 硬约束守卫：CellSizeFactor &lt; 1 会让 3×3 扫描漏邻居。
        /// 这种错误的恶劣之处在于**它不会让自检 FAIL**——只是贴身间距从 1.13 悄悄掉到 0.94、
        /// 静止漂移从 0.18 涨到 0.35，而阈值宽松到照样 PASS。靠断言抓不住，只能在配置层直接拦。
        /// </summary>
        private void OnValidate()
        {
            if (CellSizeFactor < 1f)
            {
                Debug.LogWarning($"[ScaleConfig] CellSizeFactor={CellSizeFactor} < 1：" +
                                 "格子边长会小于字间距（SeparationRadius），3×3 扫描盖不住查询半径、会漏邻居。" +
                                 "已钳到 1。");
                CellSizeFactor = 1f;
            }

            // 超容量的症状是「自检照样 PASS，但字在你看不见的地方糊成一团」——必须在这里拦。
            if (EnemyCount > PhysicalCapacity)
            {
                Debug.LogWarning($"[ScaleConfig] EnemyCount={EnemyCount} 超过场地物理容量 " +
                                 $"{PhysicalCapacity:F0}（占用 {EnemyCount / PhysicalCapacity:F2}x）。±{FieldSize} 米场地、" +
                                 $"{SeparationRadius} 米字间距在几何上只摆得下这么多，超出部分会被压实重叠——" +
                                 $"不是实现慢，是面积不够（实测 10000 时字间距塌到 0.016 米）。" +
                                 $"要在这个场地装下 {EnemyCount} 个，需把 FieldSize 提到 ±{RequiredFieldSizeFor(EnemyCount):F0} 米。");
            }
        }
#endif
    }
}
