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

---

## 6. 客户端脚本与 `execute_code` 的能力边界（2026-10-10 实测补齐）

### 6.1 两个脚本（放在会话目录，可复用）

| 脚本 | 用法 | 为什么需要它 |
|------|------|-------------|
| `unity_mcp.py` | `python unity_mcp.py call '{"name":"read_console","arguments":{...}}'` | 极简 streamable-HTTP 客户端，直连 `http://127.0.0.1:8080/mcp` |
| `mcp_call.py` | `python mcp_call.py <tool> <args.json>` | **参数走文件**，绕开 `execute_code` 里 C# 代码的引号转义地狱。写代码到 `probeN.cs` → 生成 json → 调用 |

> `mcp_call.py` 内部 `importlib` 加载 `unity_mcp.py` 复用它的 `post()`，并自动完成 `initialize` + `notifications/initialized` 握手。
> 注意：`/tmp/xxx` 这类 POSIX 路径对 **Windows 版 Python** 无效（会 `FileNotFoundError`），一律用相对路径或 Windows 绝对路径。

### 6.2 `execute_code` 用 CodeDom（C# 6）编译，这些写法不可用

| 写法 | 结果 |
|------|------|
| `math.length(...)` | `The name 'math' does not exist` → 必须全限定 `Unity.Mathematics.math.length(...)` |
| `EntityManager.GetComponentLookup<T>(bool)` | **inaccessible（internal）**。想在探针里手动跑 Job 会卡在这里 |
| `WorldUnmanaged.GetTypeOfSystem` | **inaccessible（internal）**，只能反射 |
| `ComponentSystemGroup.Systems` | 不存在。枚举 unmanaged 子系统要读私有字段 `m_UnmanagedSystemsToUpdate`（`UnsafeList<SystemHandle>`，**不实现 `IEnumerable`**，要按 `Length` + `Item` 索引器取；对它 `foreach` 会抛 `NotImplementedException`） |
| `TimeData.FrameCount` | 不存在 |
| `Unity.Scenes.ResolveSceneReferenceSystem` | 不在 `Unity.Entities` 命名空间 |

**可用的关键 API**：`dw.Unmanaged.GetExistingSystemState<T>()` 可直接写 `.Enabled`——**用来临时停掉某个系统驱动测试**（本轮就是靠它停 `HeroIntentSystem`，手动置 `HeroIntent.Attack = true`，在真实世界里打出了 `kills=4`）。

### 6.3 三个必读陷阱

**① `World.GetExistingSystemManaged` 对 unmanaged 系统必然返回 `null`。**
本项目所有 System 都是 `ISystem`（unmanaged），所以用它逐个查会得到「10 个系统全 MISSING」的**假阴性**——2026-10-10 据此误报过一次。
判「系统挂没挂上」的正确姿势：读 `SimulationSystemGroup` 的 `m_UnmanagedSystemsToUpdate`，再反射 `WorldUnmanaged.GetTypeOfSystem` 取类型名。
旁证：`World.GetOrCreateSystemManaged(typeof(HeroAttackSystem))` 会抛 `cannot be constructed as it does not inherit from ComponentSystemBase`——这条报错就是「它是 unmanaged」的铁证。

**② `Debug.Log(RunAndReport())` 在 MCP 控制台里只能看到第一行。**
自检报告是多行字符串，读到的是被截断的标题。**结论必须走返回值**：直接 `return HanziRogue.Editor.CombatSmokeTest.RunAndReport();`。

**③ 采样「世界跑没跑」要用两个时间点。**
`World.Time.ElapsedTime` + `UnityEngine.Time.frameCount` 隔几秒采两次。本轮就是靠 `frameCount` 恒 = 2 定位到「Play 被暂停」。单次采样看不出冻结。
见 ADR-0018：编辑器失焦 → Play 暂停，已在 `ProjectSettings` 里持久化 `runInBackground: 1` 从根上关闭。

### 6.4 「用户说没反应」的分诊顺序（别从代码查起）

1. `Application.isPlaying` —— 不在 Play？→ 先按 Play
2. `Application.runInBackground` + 两次 `ElapsedTime` —— 帧在动吗？→ 暂停
3. HUD 是不是写着「突刺 J：未按」？→ Game 视图没焦点（先点一下 Game 窗口）
4. 屏幕上有「游戏结束」结算面板吗？→ 已阵亡，`HeroDead` 冻结了攻击链（R 键重开）

四条各有一眼可辨的特征，**都不需要读一行战斗代码**。完整三列表见 ADR-0018。
