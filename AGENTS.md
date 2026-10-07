# HanziRogue — Agent 仓库地图

> **L0 常驻**。AI 编码代理首次进入本仓库必读。读完再动手。
> 版本 v0.3 · 2026-10-07 · **千兵追英雄闭环可玩**（1000 个「兵」字追 1 个「英雄」二字单位，方向键操作）
> v0.3 变更：工程事实与数值外置，本文件不再复制它们，改为指针（ADR-0007）

---

## 🚦 开工必读（L0，不得跳过）

| 顺序 | 文件 | 为什么 |
|------|------|--------|
| 1 | `AIDocs/00-宪法/CONSTITUTION.md` | 最高约束，违反即错 |
| 2 | `AIDocs/01-治理/CONSTRAINTS.md` | 可检查的硬边界 C1-C5 / C7 |
| 3 | `AIDocs/01-治理/工程事实.md` | 引擎版本、路径、仓库、命令、进度 |
| 4 | 本文件 | 仓库结构与常见任务路径 |
| 5 | `01-治理/开发规则总纲.md` | 写代码前读 |

**主源约定**：未定项只在宪法 §9，规模数字只在 CONSTRAINTS C1，版本/路径/进度只在 `工程事实.md`，数值只在 `数值预算表.md`。本文件一律引用，不复制——发现复制请改回指针。

L1 按需：`01-治理/*`、ADR。
L2 参考：`02-知识库/*`。**不得一次性全灌进上下文**（宪法 §4.2）。

---

## 项目一句话

三国题材 · 汉字机制肉鸽 · 万级同屏割草 · Unity ECS。
**用正确的字，把一整片错误的字扫成漫天笔画。**

---

## 当前进度

**状态摘要**：千兵追英雄闭环可玩（四层 asmdef / Boids / 英雄控制 / 字形实例化渲染 / 跟随相机 / 两场景 / 自检 PASS）。字库、五行相克、序列帧美术未开工；真实规模性能未实测。

**完整进度表见 `AIDocs/01-治理/工程事实.md` §5**（主源）。本处不复制，避免两处漂移。

---

## 目录结构

```
ECSProject/
├── AIDocs/
│   ├── 00-宪法/CONSTITUTION.md          最高约束
│   ├── 01-治理/
│   │   ├── CONSTRAINTS.md               硬边界（只存不变量）
│   │   ├── 工程事实.md                  版本/路径/仓库/进度（可变，免修订；命令见本文件）
│   │   ├── 数值预算表.md                可调数值与占位值（免 ADR，须留痕）
│   │   ├── 开发规则总纲.md               编码细则
│   │   └── ADR/                         架构决策记录 0001-0007（不可修改，只废弃/取代）
│   ├── 02-知识库/
│   │   ├── 术语表.md                    统一口径
│   │   ├── 系统地图.md                  模块与交互矩阵
│   │   ├── 汉字机制库.md                 五行/合成/战役映射
│   │   ├── ecs-规模与渲染.md             技术底座
│   │   ├── unity-mcp-接入.md            MCP 装法与端口坑
│   │   └── 无头验证通道.md               batchmode 命令与踩坑录
│   ├── 03-规格/模板/                    Spec 模板（待建）
│   ├── 04-记忆/                         项目记忆（待建）
│   └── 05-质量/                         质量检查（待建）
├── Assets/
│   ├── Scenes/                          MetaScene / BattleScene
│   └── _HanziRogue/                     全部游戏代码，四层程序集
│       ├── Core/                        Components / ScaleConfig / SpatialHash / SceneNames
│       ├── Gameplay/                    HeroInput / HeroMove / Boids / EnemySpawn
│       ├── Presentation/                BattleBootstrap / InstancedEnemyRenderer / GlyphQuadMesh / BattleCamera / HeroView / Hud / Meta
│       ├── Editor/                      SceneBuilder / BoidsSmokeTest / GlyphAssetBuilder
│       └── Data/                        ScaleConfig.asset / Glyphs(*.png) / Materials(*.mat) / Shaders(GlyphInstanced.shader)
├── Tools/                               离线烘焙脚本（Python，不参与 Unity 编译）
│   ├── glyph_bake.py                    字形 → 透明底 PNG（ADR-0006）
│   └── glyph_check.py                   PNG alpha 自检，防烘出空图
├── Packages/                            manifest.json + 嵌入式 MCP 包
├── Logs/                                Unity 日志 + 渲染取证截图（shot_*.png）
└── .workbuddy/memory/                   工作日志（追加式）
```

---

## Unity 工程约定

**版本号、编辑器路径、场景名、配置入口** → 见 `AIDocs/01-治理/工程事实.md` §1-§2（主源，本处不复制）。

两条常驻铁律：

| 铁律 | 说明 |
|------|------|
| 程序集单向依赖 | `Core ← Gameplay ← Presentation`，`Editor` 可引用全部。**不得反向**（C5.7，asmdef 已强制） |
| managed/ECS 桥接唯一 | 只有 `BattleBootstrap` 一处写 ECS；表现层**只读不回写**（宪法 §3.1） |

---

## AI 怎么操作 Unity：两条通道

| 通道 | 适用 | 状态 |
|------|------|------|
| **MCP**（`Window > MCP for Unity`，端口 8080） | 编辑器开着时实时操作：读 Console、改组件、调场景、跑代码、离屏截图取证 | 包已装，需人工点一次 Auto-Setup |
| **批处理**（`-batchmode -executeMethod`） | 不开界面：编译验证、建场景、跑逻辑自检 | 随时可用 |

> **MCP 通道的第一大坑**：编辑器窗口不在前台时主循环几乎不走（实测 60 秒只推进 0.07 秒），
> 且 `AssetDatabase` 不自动刷新——改完代码会一直跑到旧编译上，极易误判成逻辑 bug。
> 对策：先 `AssetDatabase.Refresh(ForceUpdate)` 等编译，再手动 `world.Update()` 步进。详见踩坑录 §2 坑④。

批处理命令：

```bash
cd "D:/Project/Project Unity/ECSProject" && \
env -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
"D:/Software/Unity/Unity/UnitySetup64-2022.3.62f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "D:/Project/Project Unity/ECSProject" \
  -executeMethod HanziRogue.Editor.BoidsSmokeTest.Run \
  -logFile Logs/smoke.log
```

`-executeMethod` 可选：`HanziRogue.Editor.SceneBuilder.Build`（建场景）、`HanziRogue.Editor.BoidsSmokeTest.Run`（跑自检）、`HanziRogue.Editor.GlyphAssetBuilder.RebuildAll`（重建字形材质）。

> **两个硬性前提**：必须 `env -u` 清代理，且必须非沙箱运行。
> 遇到 `EXIT=1` 但日志里没有 `error CS`，先 `taskkill //F //IM Unity.exe`——是残留进程占了工程锁。
> 详见 `02-知识库/无头验证通道.md`。

---

## 六条最容易犯的错

| 错误 | 后果 | 正确做法 |
|------|------|---------|
| 用 TMP 渲染战场文字 | 万级必崩 | 走 instanced sprite sheet（C4.1／ADR-0006） |
| 真去模拟十万实体 | 性能崩 | 三层 LOD，真实上限 16,500（C1.1） |
| Component 里放 string | Burst 编译失败 | 存索引查表（C5.2） |
| 凭代码观感断言性能 | 谎报 | 必须给实体数 + 帧率实测数字（C1.7） |
| `Random` 结构体按值传参 | 所有实体生成在同一坐标，且「数量对／无 NaN／距离下降」三条断言全过，极难自查 | 必须 `ref Random`；断言要覆盖**分布**，不只**数量** |
| 俯视相机下 quad 整片消失 | 看不到字，但 Console 无报错 | 字形 shader 必须 `Cull Off`；顶点着色器必须有 `UNITY_SETUP_INSTANCE_ID(v)` 否则实例全叠在原点 |

**写代码时额外注意**：中文注释里禁止用半角 `"`，会把 C# 字符串截断（用「」）。

---

## 未定项（阻塞，别替用户拍板）

**主源：宪法 §9**。本处只留快照日期提醒，不复制表格。

- U1 平台 ⚠ 未定（默认 Steam PC）——定了才能动 C1.1 / C1.7
- U2 单局时长 ⚠ 未定（默认 20-30 分钟）
- U3 项目命名 ⚠ 未定（代号 HanziRogue）
- U4 Unity / Entities 版本 ✅ 已定，见 `工程事实.md`

未定前，相关文档一律标 `[待定]`（宪法 §9）。

---

## 常见任务指引

| 任务 | 先读 |
|------|------|
| 加一个新字进字库 | `02-知识库/汉字机制库.md` §5 登记表模板 + CONSTRAINTS C3 |
| 加一个新字的**贴图**（先跑通画面） | `Tools/glyph_bake.py` 用法 + `GlyphAssetBuilder`；注意「一贴图一字」只在全场同字时成立，多字要转图集（ADR-0006 复核条件） |
| 写一个新的 ECS System | `01-治理/开发规则总纲.md` §2 + CONSTRAINTS C5 |
| 调整数值 | `01-治理/数值预算表.md`（C6 已迁出 CONSTRAINTS）+ 宪法 §2.8（必须有 rationale） |
| 查版本/路径/命令/进度 | `01-治理/工程事实.md` |
| 讨论规模/性能 | `02-知识库/ecs-规模与渲染.md` + C1 |
| 新增关卡 | `02-知识库/汉字机制库.md` §3（必须回答"历史上怎么赢的"） |
| 改完代码要验证 | 跑 `BoidsSmokeTest` + `SceneBuilder.Build`（见上） |
| 想实时看编辑器状态 | `02-知识库/unity-mcp-接入.md` |
| 验证画面到底长什么样 | `02-知识库/无头验证通道.md` §5「渲染取证」 |
| 架构级改动 | 先写 ADR，再动手（宪法 §6.1） |

---

## 验证纪律

声称"完成"必须同时给出：

1. Console 无 error（`Logs/*.log` 里搜 `error CS` 为空）
2. 热路径无新增堆分配
3. 性能门数字（实体数 + 帧率）
4. 复现步骤（怎么跑、看什么）

缺任一项 → 状态为「进行中」，不得报「完成」（宪法 §5）。

> 逻辑改动至少跑一次 `BoidsSmokeTest`；**它的耗时数字不能当性能结论**（Editor 未开 Burst）。

---

## 记忆纪律

实质性工作完成后，追加 `.workbuddy/memory/YYYY-MM-DD.md`（追加式，不改旧内容）。
