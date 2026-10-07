# AI 编辑器双桥（MCP + UnitySkills）

> 状态：两个包都已嵌入工程，**各需一次手动点击**才生效（Unity 编辑器内启动 server）。
> 决策记录见 `01-治理/ADR/ADR-0005-ai-editor-dual-bridge.md`。

## 1. 两条通道，分工不同

| 通道 | 包 | 版本 | 端口 | 协议 | 什么时候用 |
|------|-----|------|------|------|-----------|
| 主力 | `com.coplaydev.unity-mcp` | 9.7.1 | **8080** | MCP streamable_http | 日常：查层级/Console、改物体、截图、execute_code |
| 备用 | `com.besty.unity-skills` | 2.8.4 | **8090** | REST HTTP | 批量搭建：805 skills、batch 事务、dryRun、失败回滚 |

**分工约定：批量搭建走 Skills，验收/微调走 MCP**——避免双桥交叉写入同一批对象。

两个包都是 embedded（放在 `Packages/`），不走 UPM 下载。本机 shell 带沙箱代理，Unity 联网拉包必 `Curl error 28` 超时。

## 2. 客户端配置（已存在，不用动）

`C:/Users/Administrator/.workbuddy/mcp.json`：

```json
"unity-mcp": {
  "type": "streamable_http",
  "url": "http://127.0.0.1:8080/mcp"
}
```

默认端口 8080 见 `Packages/com.coplaydev.unity-mcp/Editor/Helpers/HttpEndpointUtility.cs:22`。
（README 里写的 6500 是错的，别照它改。）

Cursor 侧同样已配（`~/.cursor/mcp.json` 的 `unityMCP`）。
UnitySkills 走 REST，不走 MCP 配置——AI 直接调 `http://127.0.0.1:8090`。

## 3. 你要做的（各一次）

1. 用 Unity Hub 打开 `D:/Project/Project Unity/ECSProject`
2. **MCP**：`Window > MCP for Unity` → Auto-Setup → 见到 `Connected ✓`，Bridge 若 Stopped 点 Start Bridge
3. **Skills**：`Window > UnitySkills > Start Server`（首次手动，之后域重载自动恢复）

前置依赖 `uv` / `uvx`（`C:/Users/Administrator/.local/bin/`）与 Python 3.13.14 都在，不用装。

## 4. 三个坑

**① 端口冲突**：GrumpyGrandma 的 MCP 也占 **8080**。两个 Unity 工程**不要同时开**，后开那个的 Bridge 起不来。
真要同开，在面板改端口并同步改 `mcp.json`。（UnitySkills 有自动端口发现，不冲突。）

**② batchmode 下两个 server 都不启动**。建场景、跑自检这类活走 `-executeMethod` 批处理通道，详见 `无头验证通道.md`。两条通道互补。

**③ 残留进程锁工程**：表现为 Unity 崩溃 + `HandleProjectAlreadyOpenInAnotherInstance`，日志里**没有任何 `error CS`**。
遇到先 `taskkill //F //IM Unity.exe`，必要时删 `Temp/UnityLockfile`。这条今天已经踩了三次。

## 5. 安全约定

UnitySkills 能写场景和 `.meta`，用 **auto 模式** + 高危操作 NeverInSemi 门禁，审计日志在 `Library/UnitySkillsAudit.jsonl`。
**高危操作前先 dryRun**。`execute_code` 可执行任意 C#，只用于受信任的低风险自动化。

## 6. 装法留档（已执行，供重建参考）

```bash
# 两个包都从本机既有位置复制，零网络
cp -r "D:/Project/Project Unity/GrumpyGrandma/Library/PackageCache/com.coplaydev.unity-mcp@78ee541841" \
      "Packages/com.coplaydev.unity-mcp"
cp -r "D:/Downloads/Downloads WorkBuddy/Unity-Skills/Unity-Skills-main/SkillsForUnity" \
      "Packages/com.besty.unity-skills"
# manifest.json 加 "com.besty.unity-skills": "2.8.4"
```

> 老奶奶项目用的是 `file:` 依赖指向 Downloads 目录；本项目改为 embedded，好处是不依赖 Downloads 长期存在，
> 代价是 47MB 进工程目录（提交前记得确认 `.gitignore` 是否要排除）。
