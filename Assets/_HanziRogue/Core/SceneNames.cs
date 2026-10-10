namespace HanziRogue.Core
{
    /// <summary>
    /// 场景名常量。禁止在各处硬编码字符串（宪法 C5.6 精神：常量集中）。
    /// 局内局外合并为单场景后只剩一个场景——多开一个普通 Scene 承载不了 ECS 内容（ADR-0012）。
    /// </summary>
    public static class SceneNames
    {
        public const string Main = "MainScene";
    }
}
