# 后续玩法开发框架指南

## 1. 目的

本文档定义后续新增玩法、建筑、物品、传送网络和交互系统时必须遵循的工程框架。
目标是让新功能直接落在现有 ECS 运行时上，保持确定性、可测试性和稳态性能，
不把原型逻辑继续堆进旧目录。

当前技术基线：

- Unity `6000.3.19f1`
- Entities `1.4.8`
- 固定模拟频率：`60 Tick/s`
- 正式运行时程序集：`Factory.Runtime`
- 测试程序集：`Factory.Tests`

## 2. 目录与程序集边界

### 2.1 目录职责

| 目录 | 职责 |
|---|---|
| `Assets/Scripts/DataBase` | 数据模型、数据库 Asset、CSV 导入 |
| `Assets/Scripts/ECS/Data` | ECS 组件、Buffer、Blob 结构 |
| `Assets/Scripts/ECS/Systems` | 模拟系统、建造、拓扑、端口、表现采集 |
| `Assets/Scripts/ECS/Authoring` | GameObject 到 ECS 的 Baker |
| `Assets/Scripts/ECS/Presentation` | 玩家输入、UI Toolkit 界面、预览交互 |
| `Assets/Scripts/ECS/Performance` | 性能场景定义、Bootstrap、Profiler 采集 |
| `Assets/Tests` | 所有 EditMode 测试与测试 Fixture |
| `Assets/Scenes/Performance` | 性能测试场景 |
| `PerformanceReports` | 性能报告、压力报告、验收报告 |
| `Tools/FactoryStress` | 压力测试脚本 |
| `Docs` | 当前开发、测试、性能与框架文档 |

### 2.2 程序集规则

- 游戏逻辑只进入 `Factory.Runtime`。
- 编辑器工具只进入 `Factory.Editor`。
- 测试只进入 `Factory.Tests`，并放在 `Assets/Tests`。
- `Assets/Scripts/Prototype` 和 `Assets/Scripts/DotsTest` 是早期遗留目录，
  不作为正式玩法代码的落点；新功能必须直接写 ECS 运行时。
- 性能场景和采集脚本是运行时的一部分，但不得修改正式玩法规则。

## 3. 运行时架构

### 3.1 状态唯一来源

ECS Entity 和 ECS 组件是正式运行时状态的唯一来源。GameObject 只负责：

- 创作和 Baking；
- 玩家输入；
- 编辑器工具；
- 视觉表现接入。

不得在 `MonoBehaviour.Update` 中维护与 ECS 重复的玩法状态。

### 3.2 固定 Tick 流程

所有运输、加工、端口和物品逻辑只在 `FixedStepSimulationSystemGroup` 中推进：

```text
ItemProcessSystem
  -> ItemPortAdapterSystem
  -> BeltProgressSystem
  -> BeltTransferSystem
  -> TransferCommandBufferSystem
  -> ItemPortBufferSwapSystem
  -> ItemVisualStateCaptureSystem
```

`ItemTransformPresentationSystem` 在 `PresentationSystemGroup` 中每渲染帧最多
写一次物品 Transform。

### 3.3 建造流程

玩家交互通过 `GridBuildCommand` 提交意图，禁止玩法系统直接修改 Grid 或
逐实体创建建筑：

```text
Input / UI
  -> GridBuildCommand
  -> GridBuildCommandSystem
  -> GridOccupancyIndexSystem（增量 occupancy）
  -> GridPlacementTransformSystem（Revision 变化时对齐）
  -> BeltTopologyVisualSystem（脏单元格局部刷新）
```

### 3.4 正式 UI 与操作

正式 `Ecs` 场景只使用一个 `UIDocument` 和 UI Toolkit 根树，不再挂载
`Stage3PrototypeHud` 或由 `EcsGridInteractionController.OnGUI` 绘制入口。
所有输入由 `PlayerInputModeController` 的 Input System Action Map 仲裁：

| 操作 | 按键/方式 |
|---|---|
| 打开建造目录 | `Q` |
| 选择建造项 | 点击目录卡片 |
| 放置建筑/设置传送带起止点 | 鼠标左键 |
| 旋转建筑 | `R` |
| 旋转建筑/切换传送带拐弯顺序 | `R`；传送带选定起点后切换横竖优先 |
| 拆除单个建筑 | `Delete` |
| 拆除连通传送带 | `Ctrl + Delete` |
| 打开/关闭背包 | 非建造模式下按 `Tab` |
| 打开建筑窗口 | 非建造模式下左键点击建筑 |
| 取消拖拽/关闭窗口/退出建造 | `Escape` |

建造目录打开、拖拽或指针位于可交互 UI 上时，世界放置和建筑选择输入会被
阻断。性能场景 Additive 加载 `Ecs` 后会停用整个 `GameUiRoot`，保持无 UI
采样路径；性能驱动仍可通过网格控制器的模拟入口提交建造命令。

## 4. 新增玩法的标准步骤

### 4.1 新增建筑

1. 在 `Assets/Data/FactoryTables` 的 CSV 中补充建筑、等级、端口、配方数据；
2. 运行 `FactoryDatabaseCsvImporter` 重新生成 `FactoryDatabase`；
3. 在 `Assets/Scripts/ECS/Data` 添加该建筑需要的 ECS 组件；
4. 在 `GridBuildCommandSystem.AddLogicComponents` 中接入创建逻辑；
5. 如果建筑有输入/输出端口，按现有 `BuildingPort` 和 Port Snapshot 规则接入；
6. 如果需要加工，接入 `ItemProcessSystem` 和 `ItemPortAdapterSystem`；
7. 补充 Prefab/Visual Catalog；
8. 在 `Assets/Tests` 增加系统级回归测试；
9. 涉及性能时，在 `Assets/Scenes/Performance` 增加可参数化场景。

### 4.2 新增物品或配方

1. 只通过数据库表定义物品和配方，不硬编码 ItemId；
2. 使用 Item Prefab Catalog 和 `ItemPool` 管理生命周期；
3. 稳态 Tick 中复用池化物品，不持续 `Instantiate/Destroy`；
4. 表现状态使用 `ItemVisualState`，逻辑状态使用 `Item`；
5. 物品归还池时同步添加 `DisableRendering`，复用时移除，避免残影；
6. 增加物品搬运、加工、池化回归测试。

### 4.3 新增交互

1. 输入层只把意图转换成 ECS 命令或请求；
2. 不在输入层直接写 ECS 组件；
3. 预览对象不得进入正式 ECS 状态；
4. 交互和预览使用 `EcsGridInteractionController` 或等价入口；
5. 交互后的视觉更新走脏标记，不整图刷新。

## 5. 必须保持的不变量

1. 每格传送节点最多保存一个物品。
2. 逻辑状态只在固定 Tick 推进，不受渲染帧率影响。
3. 跨节点移动使用统一快照、确定性仲裁和一次性提交。
4. 同一目标在一个 Tick 内最多接受一个来源。
5. 满环支持原子移动，不因提交顺序丢失或覆盖物品。
6. Merger/Splitter 保持确定性 round-robin。
7. 仲裁结果不依赖 Job 线程顺序、HashMap 枚举顺序或 Chunk 顺序。
8. ECS 组件是正式运行时状态的唯一来源。
9. 建造、拆除、旋转和 Grid 参数变化必须推进 `GridDefinition.Revision`。
10. 稳态 Tick 不产生持续结构变化和托管 GC 分配。

## 6. 性能规则

### 6.1 稳态路径

- Transfer 主路径使用 Burst Native Job，不在主线程逐 Entity 写回。
- 拓扑缓存只在 Revision 变化时重建，不在每 Tick 全量扫描。
- Port 使用 `ItemPortBufferGeneration`，禁止恢复 Current/Next 全量复制。
- Port reservation 保存在 Port Snapshot 中，不建立长期增长的管理端
  accepted Dictionary。
- occupancy 使用增量更新，不因一次建造重建整个网格。
- Belt 视觉只刷新脏单元格和邻居，使用 ECB 合并 `DisableRendering` 变更。
- 物品生命周期走 `ItemPool`，稳定吞吐下不持续结构变化。
- Transform 更新只在状态变化时发生，物品视觉每渲染帧最多一次。

### 6.2 GC 与确定性

- Fixed Tick 中不创建托管数组、Dictionary、HashSet、List 或字符串。
- 需要可复用容器时使用持久 Native 容器。
- 所有优先级、round-robin 和赢家选择必须使用稳定键。
- 新增性能代码后，在 `Perf_4096_Mk4_FullLoop` 或对应稳态场景检查
  `perTickNet` 和 `ItemPortBufferSwapSystem` 时间。

## 7. 测试与报告

### 7.1 测试要求

- 测试文件放在 `Assets/Tests`，使用 `Factory.Tests` 命名空间。
- Resolver 测试使用 `TransportScenario`。
- System 测试使用 `FactoryWorldFixture`。
- 修改模拟逻辑后必须运行完整 `Factory.Tests`；UI 入口变更还必须运行
  `Factory.PlayModeTests`。
- 新玩法必须带回归测试，不能只用手动验证替代。

### 7.2 性能报告

- 固定规模报告写入 `PerformanceReports/<phase>-<date>/`。
- 压力测试结果写入 `PerformanceReports/StressTest/`。
- 报告必须包含基线、优化后数据和测试场景说明。
- 不把报告混入 `Docs`，`Docs` 只保留开发指南。

## 8. 常见反模式

- 把玩法逻辑继续写进 `Assets/Scripts/Prototype` 或 `Assets/Scripts/DotsTest`。
- 在 Fixed Tick 中调用 `EntityManager.CreateEntity/DestroyEntity` 作为主路径。
- 在稳态 Tick 中逐实体 `SetComponentData`。
- 每次建造都全量重建 occupancy 或 Belt 视觉。
- 使用托管 Dictionary 长期保存 Port reservation。
- 在固定 Tick 中更新渲染 Transform。
- 把测试脚本放进 `Factory.Runtime` 或 `Assets/Scripts`。
- 硬编码 BuildingLevelId、ItemId 或建筑端口。
- 修改玩法后不运行完整 `Factory.Tests` 就提交。

## 9. 提交与维护

- 每个 Phase 或独立玩法功能单独提交。
- 性能优化提交不得与玩法规则修改混在一起。
- 提交前检查 `Docs` 是否仍然只保留当前指南，旧文档放入
  `Archive/old-docs`。
- 性能测试场景、报告和测试代码一并保留。
