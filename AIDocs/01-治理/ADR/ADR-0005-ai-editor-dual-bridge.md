# ADR-0005 — AI 编辑器操控双通道（MCP for Unity + UnitySkills）

- 状态：已采纳
- 日期：2026-10-07
- 决策人：扣丢桑 + AI
- 参考：`GrumpyGrandma/AIDocs/01-治理/ADR/ADR-0006-ai-editor-dual-bridge.md`（2026-09-29 采纳并实测通过）

## 背景

本项目是万级实体 ECS 战斗，AI 协作需要能直接操控 Unity 编辑器：查层级、读 Console、批量搭场景与 UI。
仅靠改文件 + 读 Editor.log 不够——GUI 开启时 batchmode 被多实例锁拒绝，无法实时验证编辑器内状态。

## 决策

引入**两条并存**的桥接通道（端口不冲突，可共存）：

| 通道 | 包 | 版本 | 端口 | 协议 | 定位 |
|------|-----|------|------|------|------|
| 主力 | `com.coplaydev.unity-mcp`（MCP for Unity） | 9.7.1 | **8080** | MCP streamable_http | 日常操控：查层级/Console、改物体、截图、execute_code |
| 备用 | `com.besty.unity-skills`（UnitySkills） | 2.8.4 | **8090** | REST HTTP | 批量搭建：805 skills、batch 事务、dryRun、失败回滚 |

两条都以 **embedded 包**放进 `Packages/`，不走 UPM 下载——本机 shell 带沙箱代理，Unity 联网拉包必超时。

分工约定：**批量搭建走 Skills，验收/微调走 MCP**，避免双桥交叉写入。

## 理由

1. MCP for Unity 是生态事实标准，标准协议、迭代快，覆盖日常 90% 操作
2. UnitySkills 的 batch 事务 + dryRun + 回滚，是"AI 批量写真实场景/prefab"最厚的保险；其官方自述即基于 unity-mcp 概念重构
3. 本项目敌人是 ECS 运行时动态生成，**场景里手工摆放的物体很少**——所以 Skills 是备胎而非常规通道，但装它的边际成本≈0（复用老奶奶已下载的包，无网络）
4. 老奶奶项目（同机器 / 同 Unity 版本）已实测通过，直接复用零试错

## 风险与对策

| 风险 | 对策 |
|------|------|
| 第三方 Editor 插件可写场景/.meta | UnitySkills 用 auto 模式 + 高危操作 NeverInSemi 门禁 + JSONL 审计（`Library/UnitySkillsAudit.jsonl`）；高危操作前先 dryRun |
| 双桥同时改编辑器 | 约定分工（批量=Skills，验收=MCP） |
| `execute_code` 可执行任意 C# | 仅用于受信任的低风险自动化 |
| 与 GrumpyGrandma 端口冲突 | MCP 8080 两边都占，**两个工程不要同时开 Unity**；若必须同开，在面板改端口并同步改 `~/.workbuddy/mcp.json` |

## 装法（已执行）

```bash
# 两个包都从既有位置复制成本工程 embedded 包，零网络
cp -r "D:/Project/Project Unity/GrumpyGrandma/Library/PackageCache/com.coplaydev.unity-mcp@78ee541841" \
      "Packages/com.coplaydev.unity-mcp"
cp -r "D:/Downloads/Downloads WorkBuddy/Unity-Skills/Unity-Skills-main/SkillsForUnity" \
      "Packages/com.besty.unity-skills"
```

`Packages/manifest.json` 加 `"com.besty.unity-skills": "2.8.4"`。
客户端侧 `~/.workbuddy/mcp.json` 的 `unity-mcp` → `http://127.0.0.1:8080/mcp` 已存在，不用改。

## 启用步骤（需人工）

- MCP：Unity 菜单 `Window > MCP for Unity` → Auto-Setup → Start Bridge
- Skills：Unity 菜单 `Window > UnitySkills > Start Server`（首次需手动，之后域重载自动恢复）

> batchmode 下两个 server 都不会启动——命令行通道另走 `-executeMethod`（见 `无头验证通道.md`）。

## 验证记录

| 日期 | 项 | 结果 |
|------|-----|------|
| 2026-09-29 | 老奶奶项目双桥全链路（MCP 6 类操作 + Skills 805 skills /health） | 通过（ADR-0006） |
| 2026-10-07 | ECSProject 装入双包后编译 + Boids 自检 | 见 `Logs/skills_import.log` |
