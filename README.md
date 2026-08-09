# Factory with DOTS

一个使用 Unity DOTS 技术栈开发的高性能工厂模拟游戏原型。项目围绕传送带、
分流/合流、生产加工、仓储与网格建造搭建了一套可扩展的 ECS 运行时，并把
确定性模拟、自动化测试和可量化性能优化作为核心工程目标。

> 当前项目处于原型阶段。正式玩法入口为 `Assets/Scenes/Ecs.unity`；
> `Assets/Scripts/Prototype` 与 `Assets/Scenes/Prototype.unity` 保留早期实现，
> 不作为后续功能的开发入口。

## 项目亮点

- **高性能 DOTS 运行时**：基于 Entities、Burst、C# Job System 和
  Entities Graphics，大规模传送网络通过面向数据的连续内存布局和并行 Job
  执行，避免传统逐 GameObject/MonoBehaviour 更新的高额开销。
- **确定性运输模拟**：以 60 Tick/s 固定步长推进，使用统一状态快照、双缓冲、
  稳定优先级和一次性提交，支持直线、阻塞、满环原子移动以及
  Merger/Splitter 的确定性 round-robin 仲裁。
- **逻辑与表现分离**：模拟系统只处理 ECS 数据组件；表现系统在渲染帧读取固定
  Tick 快照并插值更新 Transform，渲染帧率不会反向影响玩法逻辑。
- **数据表驱动玩法**：物品、建筑、等级、端口和配方均由 CSV 定义，导入后生成
  紧凑 Blob 数据库。扩展内容通常不需要修改既有系统控制流。
- **自动化质量保障**：回归测试保护玩法不变量；固定规模性能测试量化优化收益；
  参数化压力测试定位 TPS、帧时间、GC、内存和系统级 Profiler 瓶颈。
- **自动图标渲染烘焙**：从物品/建筑表现 Prefab 自动渲染 Sprite，支持依赖哈希、
  增量重建和统一构图设置，减少美术截图、裁切、导入和策划绑定工作。

## 技术栈

| 组件 | 版本/用途 |
|---|---|
| Unity | `6000.3.19f1` |
| Entities | `1.4.8`，ECS 数据与系统框架 |
| Entities Graphics | `1.4.21`，实体批量渲染 |
| Burst Compiler + C# Job System | Native 数据并行、SIMD 友好的模拟主路径 |
| Universal Render Pipeline | `17.3.0` |
| Input System | `1.19.0` |
| UI Toolkit | 正式游戏 HUD、背包、建造目录与建筑面板 |
| Unity Test Framework | `1.6.0`，EditMode/PlayMode 自动化测试 |

## 快速开始

### 1. 拉取项目

```bash
git clone https://github.com/XiaoChen163/Factory-with-DOTS.git
cd Factory-with-DOTS
```

建议使用与项目完全一致的 Unity Editor 版本，避免 Entities、Baking 或序列化
格式因版本差异产生问题。

### 2. 打开编辑器并进入场景

1. 在 Unity Hub 中选择 **Add project from disk**，添加本项目根目录。
2. 使用 Unity `6000.3.19f1` 打开项目，等待 Package、脚本和 SubScene 导入完成。
3. 打开 `Assets/Scenes/Ecs.unity`。
4. 点击 Editor 顶部的 **Play** 进入游戏。

如果修改 CSV 后数据没有自动刷新，可执行
`Factory > Rebuild Static Database`。首次调整或新增表现 Prefab 后，可执行
`Factory > Icons > Bake All` 重新生成全部图标。

### 3. 操作说明

| 操作 | 按键/方式 |
|---|---|
| 移动镜头 | `WASD` 或方向键 |
| 升高/降低镜头 | `Space` / `Shift` |
| 旋转镜头 | 按住鼠标右键并水平移动 |
| 打开/关闭建造目录 | `Q` |
| 选择建筑 | 点击建造卡片，或使用数字键 `1`～`9` |
| 放置建筑 | 鼠标左键 |
| 放置传送带 | 左键设置起点，再左键设置终点 |
| 旋转建筑 | `R` |
| 切换传送带拐弯顺序 | 选定传送带起点后按 `R`，切换横向/纵向优先 |
| 切换拆除模式 | `F` |
| 拆除 | 拆除模式下左键点击目标；按住 `Ctrl` 可拆除连通传送带 |
| 打开/关闭背包 | 非建造模式下按 `Tab` |
| 打开建筑窗口 | 非建造模式下左键点击建筑 |
| 取消拖拽/关闭窗口/退出当前操作 | `Escape` |

建造目录、背包、建筑窗口或物品拖拽处于活动状态时，世界输入会被自动阻断，
避免一次点击同时触发 UI 和场景操作。

## 技术原理

### 1. DOTS 如何带来大规模性能提升

传统 Unity 工厂模拟常将每条传送带、每个物品实现为独立 GameObject，并让大量
MonoBehaviour 分散执行 `Update`。这种对象式布局容易产生随机内存访问、虚函数
调度、主线程瓶颈和托管 GC。当前运行时改为：

- 用小型、无托管引用的 `IComponentData` 表达传送带拓扑、运输状态、物品、
  加工器和网格占用；
- 由 `ISystem/SystemBase` 按组件查询批量处理同构数据；
- 使用 `[BurstCompile]` 的 `IJobEntity` 和 Native 容器并行推进进度、捕获视觉
  状态和执行仲裁；
- 通过拓扑缓存与 `GridDefinition.Revision` 失效机制，只在网格变化时重建拓扑，
  稳态 Tick 不重复扫描静态关系；
- 传送解析器采用近似 `O(N)` 的单轮路由，候选检查量在测试中严格保持 `N - 1`，
  避免目标数 × 来源数的 `O(N²)` 搜索；
- 使用 ECB 延迟合并结构变化，并通过 Item Pool 复用物品实体，避免稳定生产线
  持续 `Instantiate/Destroy`。

项目不是只依赖理论优势，而是保存每个优化阶段的原始报告。在同一台
i5-9500F、相同 Batch Mode 与采样口径下，从 Phase 1 到 Phase 3：

| 场景 | FixedStep ms/Tick | BeltTransfer ms/Tick | Tick/s |
|---|---:|---:|---:|
| 4096 半载蛇形 | `20.190 → 0.630`（-96.9%） | `20.147 → 0.046`（-99.8%） | `49.27 → 62.10` |
| F16 分叉网络 | `58.067 → 0.688`（-98.8%） | `58.020 → 0.034`（-99.9%） | `17.19 → 61.90` |
| 4096 满载阻塞 | `28.405 → 0.646`（-97.7%） | `28.334 → 0.040`（-99.9%） | `35.07 → 62.10` |

这些数据说明 DOTS 化后的主路径不只是“可以并行”，而是让原本无法稳定达到
60 Tick/s 的压力场景全部达到 60+ Tick/s。完整测试环境、采样方法和原始数据见
[`PerformanceReports/phase3-scenes-20260806`](PerformanceReports/phase3-scenes-20260806/README.md)。
不同硬件或 Editor/Player 模式的绝对数值不可直接横向比较。

### 2. 双缓冲 + 固定 Tick 的确定性传送带模拟

所有运输、加工和端口逻辑运行在 `FixedStepSimulationSystemGroup` 中，固定频率为
`60 Tick/s`。因此相同输入以相同 Tick 序列执行，不受渲染 FPS、机器快慢或一帧内
补跑多个 Fixed Tick 的影响。

每个 Tick 的核心流水线为：

```text
ItemProcessSystem
  → ItemPortAdapterSystem
  → BeltProgressSystem
  → BeltTransferSystem
  → TransferCommandBufferSystem
  → ItemPortBufferSwapSystem
  → ItemVisualStateCaptureSystem
```

传送算法遵循“读取旧状态、计算意图、统一仲裁、原子提交”的规则：

1. `BeltProgressSystem` 并行推进物品进度，只有 `Progress == 1` 的节点能够申请移动。
2. `BeltTransferSystem` 从 Tick 开始时的统一快照生成候选，不在遍历过程中直接
   修改下游状态。
3. Resolver 使用稳定拓扑键、来源优先级和 round-robin cursor 处理竞争；结果不
   依赖 Entity 创建顺序、Chunk 顺序、HashMap 枚举顺序或 Job 调度顺序。
4. 同一目标每 Tick 最多接受一个来源；所有胜出移动通过 Native Job/ECB
   一次性写回，因此满载闭环也可以整体旋转，不会因逐节点提交而覆盖或丢失物品。
5. 建筑 Item Port 使用 `Current/Next` 双缓冲。当前代只读，下一代收集容量、预订
   与 Receipt；Tick 末尾 `ItemPortBufferSwapSystem` 只翻转 generation 位，而不
   全量复制 Buffer。下一 Tick 才能观察上一 Tick 的提交结果。

这一设计同时解决了两个问题：双缓冲隔离 Tick 内读写，保证决策基于同一世界
快照；固定 Tick 隔离渲染时间，让模拟结果可复现、可回放、可测试。

系统级测试持续保护以下不变量：物品不丢失、不复制；阻塞时不覆盖下游；满环
原子移动；Merger/Splitter 确定性轮询；Receipt 只应用一次；拓扑只在 Revision
变化时重建。

### 3. 自动化回归、性能与压力测试

工厂模拟的错误往往只在特定拓扑、满载阻塞或长时间运行后出现，手工 Play Mode
很难稳定复现。本项目将验证分为三层：

| 层级 | 解决的问题 | 主要输出 |
|---|---|---|
| 回归测试 | 玩法规则是否被破坏，结果是否确定 | EditMode/PlayMode 通过、失败与断言 |
| 固定规模性能测试 | 某次优化究竟提升或退化了多少 | TPS/FPS、帧时间、GC、系统 ms/Tick、吞吐 |
| 参数化压力测试 | 系统在什么规模开始失效，瓶颈在哪 | 最后通过规模、首次失败规模、Profiler 摘要、内存 |

回归测试通过隔离 World 和场景 Fixture 构造直线、阻塞、合流、分流、满环、端口、
池化与 UI 输入案例，排除当前场景、实体创建顺序和执行顺序等不可控因素。修改
模拟逻辑后应运行完整 `Factory.Tests`；修改 UI/Presentation 时还应运行
`Factory.PlayModeTests`。

在 Unity 中运行：

1. 打开 `Window > General > Test Runner`。
2. 在 EditMode 中运行 `Factory.Tests`。
3. 涉及 UI 或渲染帧时，在 PlayMode 中运行 `Factory.PlayModeTests`。

命令行 EditMode 示例（运行前关闭已打开同一项目的 Editor）：

```powershell
$unityExe = 'D:\Application\Unity\6000.3.19f1\Editor\Unity.exe'
$projectPath = (Resolve-Path '.').Path

& $unityExe `
  -batchmode `
  -nographics `
  -projectPath $projectPath `
  -runTests `
  -testPlatform EditMode `
  -assemblyNames Factory.Tests `
  -testResults (Join-Path $projectPath 'Temp/Factory.Tests.xml') `
  -logFile (Join-Path $projectPath 'Temp/Factory.Tests.log')
```

仓库还提供多个正式 ECS 性能场景，包括可扩展直线、4096 满环、混合 Junction、
生产消费链和连续建造/拆除。场景到达 `READY` 后才开始采样，将加载、Baking 和
批量初始化尖峰排除在稳态数据之外。采集器记录 FixedStep、BeltTransfer、吞吐、
GC、内存、实体规模及关键系统 Profiler marker，使每次优化都有可复核的基线和
优化后结果，也能快速判断瓶颈位于 Resolver、ECB Playback、端口、网格占用还是
视觉更新。

压力测试使用倍增规模的独立 Unity 进程，直到 TPS/FPS 低于阈值：

```powershell
& .\Tools\FactoryStress\Run-FactoryStressTest.ps1 `
  -UnityExe 'D:\Application\Unity\6000.3.19f1\Editor\Unity.exe' `
  -Scene Perf_4096_Mk4_FullLoop `
  -StartScale 2 `
  -MaxScale 128 `
  -Multiplier 2 `
  -LoadPercent 100 `
  -TpsThreshold 50
```

结果会归档到 `PerformanceReports/StressTest`，记录 `lastPassingScale`、
`firstFailingScale` 和各轮指标。详细流程见：

- [自动化回归测试指南](Docs/Automated-Regression-Testing-Guide.md)
- [自动化性能与压力测试指南](Docs/Automated-Performance-Stress-Testing-Guide.md)
- [性能场景说明](Assets/Scenes/Performance/README.md)
- [性能报告索引](PerformanceReports/README.md)

### 4. 逻辑与表现分离

ECS 组件是正式玩法状态的唯一来源。GameObject/MonoBehaviour 只承担创作、
Baking、输入、UI 和表现接入，不在 `Update` 中维护一份与 ECS 重复的工厂状态。

模拟层和表现层通过 `ItemVisualState` 快照连接：

```text
Fixed Tick：BeltState / Merger / Splitter
                ↓ 捕获
            ItemVisualState
                ↓ 每个渲染帧插值一次
Render Frame：LocalTransform / LocalToWorld / Entities Graphics
```

- `ItemVisualStateCaptureSystem` 在 Fixed Tick 结束时捕获物品逻辑位置；
- `ItemTransformPresentationSystem` 位于 `PresentationSystemGroup`，只在渲染帧并行
  写入 Transform；
- `BeltTopologyVisualSystem` 只消费发生变化的脏单元格及其邻居，不因一次建造
  刷新整张地图；
- UI 通过只读 Snapshot 获取展示数据，通过 Command Bus 提交玩家意图，不直接
  篡改模拟组件。

因此模拟 System 可以专注数据正确性和吞吐，渲染 System 可以独立调整插值、
Mesh、材质和可见性策略；两者不会因更新频率不同而互相干扰。

### 5. 数据表驱动的玩法逻辑

`Assets/Data/FactoryTables` 下的 CSV 是物品、建筑与配方的唯一编辑源：

| 数据 | 内容 |
|---|---|
| `items.csv` | 稳定 Item ID、显示 Key、堆叠、分类、Prefab/Icon Key |
| `buildings.csv` / `building_levels.csv` | 建筑类型、等级、占地、表现与菜单顺序 |
| `building_ports.csv` | 输入/输出端口位置、方向与布局 |
| `belt/processor/storage_level_stats.csv` | 各类建筑等级的专属数值 |
| `recipes.csv` / `recipe_inputs.csv` / `recipe_outputs.csv` | 配方、耗时与物料关系 |

CSV 保存后，Editor 导入器会校验跨表引用、稳定 ID、端口和配方约束，并生成
`Assets/Data/Generated/FactoryDatabase.asset`。Baking 阶段再把数据转换为紧凑、
只读、Burst 可访问的 `FactoryDatabaseBlob`，运行时可以通过 ID 直接索引数组，
无需字符串查找或散落在 System 中的硬编码分支。

这种结构把“添加内容”和“修改引擎逻辑”分开：新增物品、建筑等级、速度、容量、
端口或配方主要是表格操作；稳定 key 又允许存档和资源绑定不依赖易变化的数组
下标。详细字段与约束见
[Factory static data tables](Assets/Data/FactoryTables/README.md)。

### 6. 自动图标渲染烘焙

图标管线直接从 `Assets/Prefabs/Items` 和 `Assets/Prefabs/Buildings` 中的表现
Prefab 渲染透明背景 Sprite，输出到：

```text
Assets/Art/Icons/Items
Assets/Art/Icons/Buildings
```

管线由以下部分组成：

- `FactoryIconRenderer`：创建隔离预览场景、相机和灯光并渲染 Prefab；
- `FactoryIconBakeSettings`：统一控制分辨率、视角、光照和构图边距；
- `FactoryIconBakeManifest`：记录 Prefab 依赖哈希、设置和 Baker 版本；
- `FactoryIconBakePostprocessor`：资源变化后排队执行增量烘焙；
- `FactoryIconBaker`：只重建变更项，配置 Sprite 导入参数，并刷新数据绑定。

常用菜单：

| 菜单 | 用途 |
|---|---|
| `Factory > Icons > Bake Changed` | 只重建依赖或设置发生变化的图标 |
| `Factory > Icons > Bake All` | 强制重建所有物品和建筑图标 |
| `Factory > Icons > Create or Select Settings` | 创建/选择全局烘焙设置 |

默认 Icon Key 与 Prefab Key 相同，策划通常无需逐条填写图标路径。美术只需维护
表现 Prefab，管线会自动完成渲染、透明通道、裁切构图、Sprite 导入和数据库刷新。

## 项目结构

```text
Assets/
├─ Data/FactoryTables/          # CSV 玩法数据
├─ Data/Generated/              # 自动生成的数据库和图标 Manifest
├─ Prefabs/                     # 纯表现建筑、物品与端口 Prefab
├─ Scenes/Ecs.unity             # 正式 ECS 游戏场景
├─ Scenes/EcsEntitiesSubscene.unity
├─ Scenes/Performance/          # 参数化性能场景
├─ Scripts/ECS/Data/            # ECS Component、Buffer、Blob
├─ Scripts/ECS/Systems/         # 模拟、建造、运输和表现采集系统
├─ Scripts/ECS/Authoring/       # Baker 与创作入口
├─ Scripts/ECS/Presentation/    # 输入、UI 与渲染表现
├─ Scripts/ECS/Performance/     # 性能场景、指标采集与压力驱动
├─ Scripts/DataBase/Editor/     # CSV 导入、图标烘焙等 Editor 工具
└─ Tests/                       # EditMode 与 PlayMode 自动化测试

Docs/                           # 架构、开发、测试与性能指南
PerformanceReports/             # 各优化阶段原始报告与验收数据
Tools/FactoryStress/             # 自动化压力测试脚本
```

## 开发约定

- 新玩法进入 `Factory.Runtime` ECS 运行时，不继续扩展旧 `Prototype` 目录。
- Fixed Tick 内不创建托管 `List/Dictionary/HashSet`，不依赖非稳定遍历顺序。
- 输入只提交命令，玩法 System 才能修改正式 ECS 状态。
- 修改模拟规则时先补回归测试，再运行完整 `Factory.Tests`。
- 性能优化必须保存同场景、同参数、同机器的基线与优化后报告。
- 不要手工编辑 `Assets/Data/Generated/FactoryDatabase.asset`。

更多架构与扩展规则见
[后续玩法开发框架指南](Docs/Gameplay-Development-Framework.md)。
