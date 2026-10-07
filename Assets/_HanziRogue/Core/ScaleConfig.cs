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
        [Tooltip("空间哈希格子边长。应略大于 NeighborRadius，使邻域查询只需 3x3 格。[PLACEHOLDER]")]
        public float CellSize = 3f;

        [Tooltip("分离半径：小于此距离产生排斥力，防止单位重叠成一坨。" +
                 "取值需 >= 单字世界尺寸（1.1 米），否则「兵」字会互相压字，糊成一片看不出是字。")]
        public float SeparationRadius = 1.15f;

        [Tooltip("邻居感知半径：参与聚合与对齐的范围。")]
        public float NeighborRadius = 2.5f;

        [Tooltip("分离权重。权重越高字越松，压字越少；太高会把字潮推散成均匀网格，失去「潮」的质感。")]
        public float SeparationWeight = 1.6f;

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
    }
}
