# Unity MCP 接入说明

> 状态：包已嵌入工程，**需一次手动点击**才生效（Unity 编辑器内启动 Bridge）。

## 1. 装了什么

| 项 | 值 |
|---|---|
| 包 | `Packages/com.coplaydev.unity-mcp`（embedded 本地包，不走 UPM 下载） |
| 版本 | 9.7.1 |
| 源 | 从 `GrumpyGrandma/Library/PackageCache` 复制，避免联网拉包被代理卡死 |
| Unity 要求 | 2021.3+（本机 2022.3.62f1 OK） |
| 依赖 | `com.unity.nuget.newtonsoft-json` 3.2.1、`com.unity.test-framework` 1.1.33 |

> 为什么用 embedded 而不是 manifest 里写 git URL：本机 shell 有沙箱代理，Unity 走 git 拉包会 `Curl error 28 timeout`。embedded 是零网络的稳妥解法。

## 2. 客户端配置（已存在，不用动）

`C:/Users/Administrator/.workbuddy/mcp.json`：

```json
"unity-mcp": {
  "type": "streamable_http",
  "url": "http://127.0.0.1:8080/mcp"
}
```

端口 8080 是包的默认值，见 `Packages/com.coplaydev.unity-mcp/Editor/Helpers/HttpEndpointUtility.cs:22`。
**README 里写的 6500 是错的**，不要照它改。

Cursor 侧同样已配（`~/.cursor/mcp.json` 的 `unityMCP`）。

## 3. 你要做的三步

1. 用 Unity Hub 打开 `D:/Project/Project Unity/ECSProject`
2. 菜单 **Window > MCP for Unity**
3. 点 **Auto-Setup** → 见到 `Connected ✓`；Bridge 若显示 Stopped，点 **Start Bridge**

前置依赖 `uv` / `uvx` 已装（`C:/Users/Administrator/.local/bin/`），Python 3.13.14 也在，不用重装。

## 4. 两个坑

**端口冲突**：GrumpyGrandma 也用 8080。两个 Unity 工程同时开，后开那个的 Bridge 起不来。
→ 解法：只开一个工程，或在 MCP for Unity 窗口里给 ECSProject 换端口（换完要同步改 `mcp.json`）。

**Batchmode 不启动 Bridge**：命令行 `-batchmode -nographics` 下 MCP server 不会起来。
→ 建场景这类活继续走 `SceneBuilder.Build` 的批处理通道；MCP 只在编辑器 GUI 开着时可用。两条通道互补，不冲突。

## 5. 批处理通道（MCP 之外的备用手段）

```bash
cd "D:/Project/Project Unity/ECSProject" && \
env -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
"D:/Software/Unity/Unity/UnitySetup64-2022.3.62f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "D:/Project/Project Unity/ECSProject" \
  -executeMethod HanziRogue.Editor.SceneBuilder.Build \
  -logFile Logs/build.log
```

要点：
- **必须清代理**（`env -u HTTP_PROXY...`）且**非沙箱运行**，否则 Unity 联网请求会被沙箱代理挡住
- **同一工程只能有一个 Unity 实例**。残留进程会导致 `HandleProjectAlreadyOpenInAnotherInstance` 崩溃 —— 遇到 `EXIT=1` 先 `taskkill //F //IM Unity.exe`
- 日志看 `Logs/build.log`，搜 `error CS` 定位编译错误
