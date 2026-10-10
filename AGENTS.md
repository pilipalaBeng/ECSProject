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

**状态摘要**：千兵追英雄闭环可玩（四层 asmdef / Boids / 英雄控制 / 字形实例化渲染 / 跟随相机 / 单场景双模式 / 自检 PASS）。**战斗闭环已落地**：英雄突刺、兵 10 血、英雄 200 血、受击闪白与闪红、阵亡冻结（R 键 / 按钮重开），`CombatSmokeTest` 9 项断言全 PASS（ADR-0014 / 0017）。字库、五行相克、序列帧美术未开工；真实规模性能未实测。

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
│   │   └── ADR/                         架构决策记录 0001-0018（不可修改，只废弃/取代）
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
│   ├── Scenes/                          MainScene（单场景：MetaRoot / BattleRoot 双模式，ADR-0013）
│   └── _HanziRogue/                     全部游戏代码，四层程序集
│       ├── Core/                        Components / ScaleConfig / CombatConfig / SpatialHash / SceneNames / GameMode
│       ├── Gameplay/                    HeroIntent / HeroMove / HeroFacing / HeroAttack / EnemyAttack / ApplyDamage / CombatInit / HitFlashDecay / Boids / EnemySpawn
│       ├── Presentation/                BattleSession / ModeSwitcher / InstancedEnemyRenderer / GlyphQuadMesh / BattleCamera / HeroView / HeroWeaponView / Hud / Meta
│       ├── Editor/                      SceneBuilder / BoidsSmokeTest / CombatSmokeTest / SceneFlowSmokeTest / GlyphAssetBuilder
│       └── Data/                        ScaleConfig.asset / CombatConfig.asset / Glyphs(*.png) / Materials(*.mat) / Shaders(GlyphInstanced.shader 字潮 / GlyphFlat.shader 单实例 / GridFloor.shader 地面)
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
| managed/ECS 桥接唯一 | 只有 `BattleSession` 一处写 ECS；表现层**只读不回写**（宪法 §3.1） |
| 一局边界唯一 | 「一局战斗」的开与关只在 `BattleSession.Enter/Exit` 里定义；别的类不得自行增删战斗实体（ADR-0013） |

---

## AI 怎么操作 Unity：两条通道

| 通道 | 适用 | 状态 |
|------|------|------|
| **MCP**（`Window > MCP for Unity`，端口 8080） | 编辑器开着时实时操作：读 Console、改组件、调场景、跑代码、离屏截图取证 | 包已装，需人工点一次 Auto-Setup |
| **批处理**（`-batchmode -executeMethod`） | 不开界面：编译验证、建场景、跑逻辑自检 | 随时可用 |

> **MCP 通道的第一大坑**：编辑器窗口不在前台时主循环几乎不走（实测 60 秒只推进 0.07 秒），
> 且 `AssetDatabase` 不自动刷新——改完代码会一直跑到旧编译上，极易误判成逻辑 bug。
> **已从根上关闭**：`ProjectSettings` 里 `runInBackground: 1`（ADR-0018）。
> 不要再在运行时临时设 `Application.runInBackground = true`——那只修好「AI 操作的那一次 Play」，
> 用户自己按 Play 时又回到默认暂停态，而他看到的就是那一次。
> 仍要保留的习惯是「先 `AssetDatabase.Refresh(ForceUpdate)` 等编译」（下一坑）。
>
> **MCP 通道的第二大坑**：**读控制台拿不到 info 级日志**——`Debug.Log` 读不出来（只有 warning / error 能读）。
> 2026-10-07 实测确认：同一次执行里 `Debug.LogWarning` 能读到、`Debug.Log` 读不到。
> 所以自检结论必须走**返回值**：用 `BoidsSmokeTest.RunSummary()`（返回整份报告）而不是 `Run()`（只写日志）。
> 这类「工具通道的能力边界」必须在动手前用小探针验一次，别等到查不出问题才发现输出根本没出来。
>
> **MCP 通道的第三大坑**：`execute_code` 里**绝不能用 `Thread.Sleep` 等帧**——它跑在 Unity 主线程上，
> sleep 期间主循环完全冻结。2026-10-08 实测：sleep 1500ms 后 `Time.time` / `frameCount` 一字未动，
> 于是 `heroes=0`，看起来像「生成系统挂了」，其实只是采样时帧被自己冻住了。
> 同时要修正第一大坑的适用条件：**开了 `Application.runInBackground = true` 时，窗口不在前台也照跑**
> （同一次实测 t 从 8.36 → 41.94、26264 帧）。所以「实体没生成」十有八九是**采样时机**的错觉。
> 正确姿势：**分两次调用**——第一次下指令就返回，编辑器在两次调用的间隙自己推进，第二次回来采样。

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

`-executeMethod` 可选：`HanziRogue.Editor.SceneBuilder.Build`（建场景）、`HanziRogue.Editor.BoidsSmokeTest.Run`（跑字潮自检）、`HanziRogue.Editor.CombatSmokeTest.Run`（跑战斗自检）、`HanziRogue.Editor.GlyphAssetBuilder.RebuildAll`（重建字形材质）。

> **两个硬性前提**：必须 `env -u` 清代理，且必须非沙箱运行。
> 遇到 `EXIT=1` 但日志里没有 `error CS`，先 `taskkill //F //IM Unity.exe`——是残留进程占了工程锁。
> 详见 `02-知识库/无头验证通道.md`。

---

## 最容易犯的错

| 错误 | 后果 | 正确做法 |
|------|------|---------|
| 用 TMP 渲染战场文字 | 万级必崩 | 走 instanced sprite sheet（C4.1／ADR-0006） |
| 真去模拟十万实体 | 性能崩 | 三层 LOD，真实上限 16,500（C1.1） |
| **在 ±50 场地里把 `EnemyCount` 调到容量以上** | **不是掉帧，是字直接压成一坨糊掉**——10,000 实体时字间距从设计的 1.15 米塌到 0.016 米（完全重合）。而且**自检照样 PASS**，只是阈值宽松到抓不住 | 先看 `ScaleConfig.PhysicalCapacity`（±50 场地 = 6,858）。设计规模取容量的 **0.7 倍以下**，别卡着容量设计（1.0x 时最近点对已劣化到 0.4~0.5 米）。`OnValidate` 已加守卫会报警（ADR-0012） |
| Component 里放 string | Burst 编译失败 | 存索引查表（C5.2） |
| 凭代码观感断言性能 | 谎报 | 必须给实体数 + 帧率实测数字（C1.7） |
| `Random` 结构体按值传参 | 所有实体生成在同一坐标，且「数量对／无 NaN／距离下降」三条断言全过，极难自查 | 必须 `ref Random`；断言要覆盖**分布**，不只**数量** |
| 俯视相机下 quad 整片消失 | 看不到字，但 Console 无报错 | 字形 shader 必须 `Cull Off`；顶点着色器必须有 `UNITY_SETUP_INSTANCE_ID(v)` 否则实例全叠在原点 |
| `IJobEntity` 里往共享缓冲的**任意下标**写 | 抛 `IndexOutOfRangeException: Index N is out of restricted IJobParallelFor range [0...-1] in ReadWriteBuffer`。**开着 Burst 反而看不到这个异常**（托管安全检查被剥离），表现成另一种更难查的崩溃 | 给该字段加 `[NativeDisableParallelForRestriction]`，自己用 `Interlocked` 保证线程安全（ADR-0011 §4）。`[ReadOnly]` 的数组不受此限，故报错只在写入侧 |
| 全部邻居的推力**矢量求和**后限幅 | 均匀密实介质里邻居对称分布、矢量和趋于零 → 「被压到极近的两个兵」这一笔也被抵消，两个完全重合的兵永远分不开。兜底分支只管 `dist < 1e-4`，0.015 米走不到它 | 把「穿透最深的那一对」独立出来，与平滑项取更优者；**但要加启动门槛**（`DeepPenetrationFactor = 0.4`），否则健康配置下会恒常接管、把门禁从 1.09 米/0.18 米劣化到 0.75 米/1.03 米（ADR-0012） |
| **手动 `Dispose` 了 `SystemAPI.QueryBuilder().Build()` 出来的查询** | 抛 `InvalidOperationException: EntityQuery cannot be disposed`。它发生在**生成实体之前**，于是表现是「场上一个实体都没有」而编译零错误——极易误判成「系统没跑」或「排序不对」 | 两种查询的所有权规矩**相反**：`SystemAPI.QueryBuilder().Build()` / `state.GetEntityQuery()` 归**系统**所有，**不许 Dispose**；`EntityManager.CreateEntityQuery()` 归**调用者**所有，**必须 Dispose**。2026-10-07 实测：这个 bug 同时打挂了 `EnemySpawnSystem` 与 `BoidsSmokeTest` |
| 生成系统与初始化系统之间**没有排序约束** | 只写 `[UpdateBefore(扣血系统)]` 是不够的：生成系统可能排在初始化之后，「实体刚建出来、血量还是 0」的那一帧照样被结算系统看到并判死。整条战斗链从第一帧起被冻结，看起来像「攻击功能没实现」 | 初始化系统必须同时写 `[UpdateAfter(typeof(生成系统))]` **和** `[UpdateBefore(扣血系统)]`。两条缺一条都会出事 |
| 「未初始化」判据在**多处各写一份** | `Health.Max ≤ 0` 这个哨兵要在三处一致使用：① 初始化填血 ② 伤害结算不结算 ③ 死亡判定不算死。漏掉 ②③ 任一处，开局第一帧血量 0 的英雄就被判死，`HeroDead` 当场置位。它出的是**静默 bug**——不报错，只是判定结果与预期不同 | 收敛成 `CombatRules.IsInitialized(in Health)` 一个函数，三处都调它（`Core/Components/Combat.cs`） |
| 把「阵亡即销毁」无差别套到英雄 | 英雄一销毁，全场所有依赖 `HeroTag` 的查询（移动/朝向/攻击/表现层多个读取）在同一帧集体落空，相机跟随对象直接消失、画面跳一下，之后每帧都要先判断「英雄还在不在」 | 英雄**不销毁**：血量压到 0，由 `BattleProgress.HeroDead` 冻结整条战斗链。留一个血量 0 的英雄实体，比留一堆空引用判断便宜得多（ADR-0014） |
| **让英雄和字潮共用 `GlyphInstanced` 着色器** | **字块被钉死在世界原点、尺寸恒为 `_GlyphSize`（1.1 米，和「兵」一样大），而武器材质认 Transform、跟着真身跑了 → 看起来是「英雄不动、武器自己走自己的」。更坑的是它不报任何错**：`SV_InstanceID` 恒为 0、`_GlyphInstances` 又只绑在敌人材质上，读出来是零，属于合法输入 | 两种单位两种着色器：字潮走 `GlyphInstanced`（`DrawMeshInstancedIndirect`，顶点不读 Transform），**英雄走 `GlyphFlat`（普通 `MeshRenderer`，`UnityObjectToClipPos`）**。选型收口在 `GlyphAssetBuilder` 里由 `instancing` 布尔决定，别在调用方各写一份 shader 名（ADR-0015） |
| 把英雄的伤害着色写成**两个** MonoBehaviour | `MaterialPropertyBlock` 是挂在 Renderer 上的一份共享状态，两个组件各写一份就互相覆盖，谁最后写谁赢——表现变成「闪红时有时无」，且随脚本执行顺序变 | 英雄外观的**唯一写入方**是 `HeroWeaponView.ApplyHeroTint`：`_Color` 管底色（常态金橙 → 阵亡灰），`_Flash` 只叠受击那一层。加新表现时扩字段，不新开组件（ADR-0015） |
| 表现层自己判「血量 ≤ 0 就是死了」 | 会把「未初始化（`Max = 0`）」也算成死亡——开局第一帧整块英雄字就是灰的。而且它与 `ApplyDamageSystem` 的判据**必须永远一致**，两份判据迟早漂移 | 阵亡判据唯一来源是 `BattleProgress.HeroDead`（由 `CommitBattleProgressJob` 写）。表现层只读这一个 bool，不自己比血量（宪法 §3.1） |
| **把修法挂在一个需要人记得执行的动作上**（典型句式：「重跑一次菜单就修好了」） | 代码、shader、编译**三样全干净**，编辑器里看不出任何异常，只有一进 Play 才是坏的。因为材质的 `m_Shader` 是**存在 .mat 资产里的持久字段**，C# 改了它不会跟着变。2026-10-10 实测：上一轮修法本身正确，但没人点那个菜单，于是「还是不好使」，白等一轮 | 让对账**自动发生**：`Editor/GlyphMaterialSelfHeal`（`[InitializeOnLoad]` + `EditorApplication.delayCall`）在每次脚本编译完后，把两张字建材质跟代码约定比一次，不一致就纠回来。规则：**修复不许依赖人记得点菜单；资产上的字段必须由代码在编译周期内主动对齐**（ADR-0016）。改 `.mat` / `.asset` / `manifest` 这类资产口径时的通用自查 |
| **判定体积与所见不符**：只让「朝向前方的细带」吃伤害 | 兵停在 `StopRadius`（1.5 米）处，而「英雄」二字半宽也是 1.5 米——**这些字本来就画在英雄身上，却因为不在朝向上而完全免疫**；判定带（半宽 0.55）在 1.5 米环上只覆盖 ±20°，而兵是 44° 间隔的离散密堆，带里经常一个字都没有；刀身画到 3.3 米、判定却只到 2.6 米。玩家看到的是「**武器碰到小兵却不掉血**」，HUD 实测整局**击杀 0**（2026-10-10）。伤害链本身是通的（8 项自检全 PASS）——难查正在于此 | 判定必须**两段**：① 枪身胶囊（沿朝向 `Reach` 米、半宽 `HalfWidth`）② **贴身圆 `PointBlankRadius`，不看朝向**。三条对齐不变式：`HeroReach = thrustDistance + 刀身半长`；`HalfWidth` ≥ 一个字宽；`StopRadius < PointBlankRadius < 自检用例里「背后的兵」的距离`（ADR-0017）。**任何「视觉上碰到就该生效」的判定，都要先问：判定体积和画出来的东西是同一个吗** |
| 靠肉眼判断「这一下到底打中没有」 | 这个项目连着两轮把时间花在「玩家以为在攻击、其实按键/相位没进去」上：闪白 0.12 s 混在 1000 个字里肉眼抓不住，判定有没有生效全凭猜 | 把状态摊在 HUD 上自证：`HudController` 增加「突刺 J：未按 / 蓄势 / 出枪（判定帧）/ 收枪」一行——**未按**说明输入层断了，**已按但相位停在待机**说明相位机没跑。表现层只读不写（宪法 §3.1） |
| **把「编辑器失焦会暂停」当成查 bug 时临时开一下的手段** | 这个坑（下节 §1）**早就写在文档里**，但 `Application.runInBackground = true` 一直只在运行时临时设。后果：AI 那一次 Play 是好的，用户自己再按 Play 又回到**默认暂停**——而他看到的就是那一次。表现是「武器压在兵身上，一动不动，也不掉血」，与「判定体积不对」长得一模一样。2026-10-10 实测：`Time.frameCount` 冻在 **2**、`ElapsedTime` 冻在 0.0199，而 `CombatBalance` / 系统注册 / 自检全对 | **默认态必须是可观测态**：写进 `ProjectSettings.asset`（`runInBackground: 1`），不要靠人记得设。这与 ADR-0016 是同一课——**修复不许依赖人记得执行某个动作**（ADR-0018） |
| 把三种「**什么都没有发生**」混成一种来查 | ① Play 被暂停（失焦）② 英雄已阵亡（`HeroDead` 让攻击链永久早退）③ Game 视图没焦点（legacy `Input` 只送给有焦点的 Game 窗口）。三者在画面上**完全一样**（武器叠在兵身上、按 J 没反应），而这一轮三种同时存在 | 给这三条各配一个一眼可辨的特征，并**按顺序分诊**，别从代码查起：**帧率/时间在动吗** → 暂停；**HUD 写「突刺 J：未按」** → 焦点；**有结算面板 / 字变灰** → 已阵亡（R 键重开）。完整三列表见 ADR-0018 |

**写代码时额外注意**：中文注释里禁止用半角 `"`，会把 C# 字符串截断（用「」）。
另：C# 9 的插值字符串 `$"..."` 里不能再嵌 `"`（那要 C# 11 的原始字符串），
带格式的数值必须先在洞外转成 `string` 再插。

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
| 改完代码要验证 | 跑 `BoidsSmokeTest`（字潮）+ `CombatSmokeTest`（战斗）+ `SceneBuilder.Build`（见上） |
| 改战斗逻辑（伤害/血量/死亡/命中） | 先跑 `CombatSmokeTest`（**9 项**断言，含「一刀结算两次」「杀光即重刷」「贴身不看方向也挨打」这类只在特定时刻出现的坑）；数值改动见 `数值预算表.md` §5。**跑它时会刷 10 条 `Ignoring invalid [UpdateBeforeAttribute]` 警告，那是自检世界裁剪导致的，不是 bug**（ADR-0018 §4） |
| **用户报「按了没反应 / 打不掉血」** | 先按 ADR-0018 的三列表**分诊**，别从代码查起：① HUD 帧率/时间在动吗（编辑器失焦 → Play 暂停）② HUD 写「突刺 J：未按」吗（Game 视图没焦点）③ 有结算面板 / 字变灰吗（已阵亡，`HeroDead` 冻结攻击链；R 键重开）。这三条在画面上完全一样，但都不需要读一行战斗代码 |
| 要规模性能曲线（1 千 / 1 万 / 5 万 / 10 万） | `BoidsSmokeTest.Measure(n, warmup, frames)`；恒定密度对照加第 4 参 `fieldSizeOverride`，单独覆盖网格加第 5 参 `gridHalfExtentOverride`。性能曲线见 `ADR-0011`，**容量曲线（最近点对 vs 占用率）见 `ADR-0012`** |
| 怀疑「字糊了 / 互相压住」 | `Measure` 会打印 `[Ring]` 径向环带占用表 + `[Closest]` 全场最近点对**及其离英雄多远** + `[Identity]` 身份冲突统计。**先看最近点对在哪**——在 20 米外说明是全场超容量，在 2 米内才是英雄区问题。别凭观感猜（ADR-0012 两次猜错，两次都被这两个探针否掉） |
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

> 逻辑改动至少跑一次 `BoidsSmokeTest`；**它的耗时数字不能当真机性能结论**（Editor 内实测，且随 Burst 开关差 4~9 倍）。
> 多档规模曲线用 `BoidsSmokeTest.Measure(n, warmup, frames)`——它自动跳过 O(n²) 的邻近断言，
> 1 万以上才不会把自检挂死。恒定密度对照加第 4 个参数 `fieldSizeOverride`。
> 已实测曲线与「什么被否掉了」见 `ADR-0011` 的「实测回填」一节。
>
> **战斗改动跑 `CombatSmokeTest`**。它开头会先打一行 `[env]` 环境探针（只挂生成系统的
> 最小世界），把「世界搭起来没有」和「战斗逻辑对不对」分开报——因为八条断言共用同一个
> 建世界函数，只要生成环节断掉，后面每一条都会以「数组索引越界」的形式崩，
> 真正的失败原因会被埋在最后一条的堆栈里。看到 `[env]` 里英雄/兵是 0，别往下看断言，
> 直接查生成环节。另有 `[env]` 之外的 `EnvironmentProbe` 同源于此。
>
> **PlayMode 里的时间不可靠**：编辑器窗口不在前台时主循环推进是**断断续续**的
> （实测过两次 MCP 调用之间推了 8.96 秒，也实测过 60 秒只推进 0.07 秒）。
> 所以「我写入了状态 → 下一步截图」这种验证必须**先把时间条件固定住**
> （例：把 `HitFlashDuration` 临时调到 60 秒再截，否则 0.12 秒的闪白在你截之前早就过期了，
> 会让你误判成「闪白没生效」）。2026-10-07 实测踩过这个坑。
>
> **「超容量」这类缺陷的自检盲区**：它不改数量、不出 NaN、不抬平均距离、也不掉帧，
> 只是把字压成一坨——而所有阈值都宽松到照样 PASS。所以**不要只用 PASS/FAIL 下结论**，
> 要读 `Measure` 打出的 `[Ring]` / `[Closest]` 原始数字，并与设计值（字间距 1.15 米）对比。
> 判据：占用率 ≤0.75x 健康 / ≈1.0x 临界（0.4~0.5 米）/ ≥1.4x 崩坏（0.02 米级）。见 `ADR-0012`。

---

## 记忆纪律

实质性工作完成后，追加 `.workbuddy/memory/YYYY-MM-DD.md`（追加式，不改旧内容）。
