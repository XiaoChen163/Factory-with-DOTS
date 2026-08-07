# ECS 性能优化策略与实施计划

## 1. 文档目的

本文档用于指导现有 Stage 3 ECS 工厂模拟的性能优化，覆盖传送网络解析、Job 调度、组件布局、结构变化、Transform 更新和表现层访问。

当前技术基线：

- Unity `6000.3.19f1`
- Entities `1.4.8`
- Entities Graphics `1.4.21`
- 固定模拟频率：`60 Tick/s`
- 当前 ECS 网格：`32 × 32`

优化目标不是只降低某个测试场景的耗时，而是把稳定运行路径改造成：

- 稳态 Tick 无托管 GC 分配；
- 传送解析随节点数量近似线性增长；
- 大部分模拟逻辑可以由 Burst 编译；
- 拓扑计算只在建造或拆除后发生；
- 逻辑模拟和视觉插值相互独立；
- 扩大网格和产线规模时不会出现算法复杂度突变。

## 2. 优化期间必须保持的不变量

任何性能优化都不得改变以下玩法语义：

1. 每格传送节点最多保存一个物品。
2. 所有逻辑状态只在固定 Tick 中推进，不受渲染帧率影响。
3. 跨节点移动继续使用统一快照、冲突仲裁和一次性提交。
4. 同一目标在一个 Tick 内最多接受一个来源。
5. 满环继续支持原子移动，不能因为提交顺序产生物品丢失或覆盖。
6. 合流器和分流器继续保持确定性的 round-robin 行为。
7. 一个合流器或分流器在单个 Tick 内最多处理一个物品。
8. 仲裁结果不能依赖 Job 线程执行顺序、HashMap 枚举顺序或 Entity Chunk 顺序。
9. ECS 组件仍然是正式运行时状态的唯一来源；GameObject 只负责创作和交互。

性能重构开始前，应先为直线、阻塞、合流、分流、满环、非满环和建筑端口转移建立自动化回归测试。

## 3. 当前瓶颈与优先级

### P0：传送解析主线程化且复杂度过高

`BeltTransferSystem` 每个固定 Tick 都会：

1. 为 Belt、Merger、Splitter 分别生成 Entity 和组件快照；
2. 将 NativeArray 再转换成托管数组；
3. 重建 `Dictionary<int2, TransportIndex>`；
4. 在主线程执行 `FactoryTransferResolver.Resolve`；
5. 逐实体调用 `EntityManager.SetComponentData` 写回；
6. 在本系统内立即 Playback ECB。

`FactoryTransferResolver.SelectIncomingCandidates` 对每个目标扫描全部源节点，单次解析复杂度为 `O(N²)`。外层最多执行 `MergerCount + SplitterCount + 1` 轮，因此上界接近：

```text
O((J + 1) × N²)

N = Belt + Merger + Splitter 节点数
J = Merger + Splitter 节点数
```

该路径还会在每次 Resolve 中创建 Node 数组、Dictionary、候选数组、状态数组、循环检测数组和 List，形成持续的 GC 压力。

### P0：Job 完成点集中在传送系统

`BeltProgressSystem`、`ItemProcessSystem` 和 `ItemPortAdapterSystem` 虽然使用 Burst Job，但 `BeltTransferSystem` 随后同步读取它们写入的组件和 Buffer。同步快照会等待相关 Job 完成，使主线程成为整个 Fixed Tick 的串行汇合点。

### P1：物品边界操作产生结构变化

建筑输出时执行：

```text
Instantiate Item Prefab
AddComponent<Item>
SetComponent<LocalTransform>
```

建筑输入时执行 `DestroyEntity`。当产线吞吐量升高时，结构变化、Chunk 迁移和 ECB Playback 成本会随每 Tick 的输入输出数量增长。

### P1：Transform 更新范围过大

- `GridPlacementTransformSystem` 每个渲染帧重写所有建筑的 `LocalTransform`，即使 GridPlacement 没有变化。
- `BeltItemPositionSystem` 在固定模拟组中运行，追帧时可能在一个渲染帧内重复写多次 Transform。
- 位置系统通过运输节点间接随机写 Item Entity，缓存局部性较差。
- `Item.Position` 与 `LocalTransform.Position` 保存了重复状态。

### P1：Belt 组件混合静态和高频状态

当前 `Belt` 同时包含：

- 静态或低频数据：Cell、Direction、NextCell、CellsPerSecond；
- 高频数据：CurrentItem、Progress；
- 拓扑诊断数据：IsLoop、HasOutput。

`BeltProgressJob` 每 Tick 写入 Belt 时会触碰整块组件。当前 ECS 代码中 `NextCell` 没有被读取，`HasOutput` 没有消费者，`IsLoop` 只用于解析阶段生成统计结果。这些字段不应留在高频组件中。

### P2：端口快照复制和托管缓存增长

- `ItemPortBufferSwapSystem` 每 Tick 清空并逐元素复制六类 Current/Next/Receipt Buffer。
- `acceptedInputs` 和 `acceptedOutputs` 是长期存在的托管 Dictionary；建筑删除后，历史 Entity Key 不会自动移除。
- 输入/输出 Owner 每 Tick重新排序，比较器中还会反复读取 GridPlacement。

### P2：拓扑和交互更新尖峰

- Belt 视觉拓扑刷新时先显示所有边，再重新隐藏连接边，可能对同一视觉 Entity 连续 Remove/Add `DisableRendering`。
- GridBuildCommandSystem 在每批命令前创建完整的托管 Placement 快照。
- `EcsGridInteractionController` 每帧创建并销毁 GridDefinition、FactoryDatabase 和 GridBuildResult 查询。
- `DotsTest` 下的自动创建系统可能进入正式 Default World，产生无效调度或干扰 Profiler 结果。

## 4. 目标架构

### 4.1 拓扑阶段：只在 Grid Revision 变化时运行

新增或重构一个拓扑系统，持久保存以下 Native 数据：

```text
Cell -> NodeIndex
NodeIndex -> NodeKind
NodeIndex -> OutputTarget(s)
NodeIndex -> IncomingSource(s)
NodeIndex -> Direction / Port Geometry
NodeIndex -> Loop / Connected Component
```

推荐容器：

- `NativeParallelHashMap<int2, int>`：格子到节点索引；
- `NativeList` 或持久化 `NativeArray`：节点拓扑和动态状态；
- 固定的 1～3 个输入索引：Belt、Merger、Splitter 的输入数量都有明确上限；
- 连通分量或环路描述：仅在建造、拆除或旋转后重建。

拓扑缓存必须以 `GridDefinition.Revision` 或显式 `TopologyRevision` 失效，不能依赖每 Tick 全量扫描。

### 4.2 Fixed Tick：线性解析动态状态

推荐 Tick 流程：

```text
BeltProgressJob
    -> BuildReadyRequestsJob
    -> SelectCandidateJob
    -> ResolveChainsAndLoopsJob
    -> CommitTransportStateJob
    -> PortReceipt/State Swap
```

要求：

1. 所有容器复用，不在 Tick 中创建托管数组、Dictionary、HashSet 或 List。
2. Ready Request 遍历每个源一次。
3. 候选选择只检查目标预计算的 1～3 个输入，复杂度为 `O(N)`。
4. 冲突优先级使用可重复的稳定键，例如 Grid 坐标、端口优先级和 Entity 稳定序号。
5. 第一版可以使用单线程 Burst `IJob` 保证确定性；验证正确后再按独立网络或连通分量并行。
6. 不再通过主线程逐 Entity 写回，使用 `IJobChunk`、`IJobEntity` 或连续 Native 状态数组批量提交。
7. 不再执行 `MergerCount + SplitterCount + 1` 轮全图 Resolve；如果需要二次传播，只处理受影响 frontier。

### 4.3 Presentation：每个渲染帧只更新一次

逻辑 Tick 只维护：

- 当前载体节点；
- 节点内逻辑进度；
- 上一 Tick 和当前 Tick 的必要快照。

物品 Transform 移到 `PresentationSystemGroup`，根据渲染插值系数计算。即使一帧执行多个 Fixed Tick，也只提交一次最终视觉 Transform。

远离相机或不可见的物品可以跳过 Transform 更新，但其逻辑状态必须继续推进。

## 5. 分项优化策略

### 5.1 Transfer Resolver

- 将 Cell Index、输入连接、输出连接、环路信息改为持久化 Native 拓扑。
- 将 `SelectIncomingCandidates` 改为目标读取预计算输入，而不是目标扫描所有源。
- 将递归 `ResolveCandidate` 改为显式栈、连通分量解析或预计算链路，避免长链递归深度风险。
- Loop 标记和 HasOutput 只在拓扑变化时计算。
- 保留确定性 round-robin 状态，禁止依赖并行原子操作的竞争先后顺序。

### 5.2 组件布局

建议拆分为：

```csharp
public struct BeltTopology : IComponentData
{
    public int2 Cell;
    public int2 Direction;
    public float CellsPerSecond;
}

public struct BeltState : IComponentData
{
    public Entity CurrentItem;
    public float Progress;
}
```

- 删除未使用的 `NextCell`，或只在拓扑缓存中保存 TargetIndex。
- 删除运行时未读取的 `HasOutput`。
- `IsLoop` 如果只用于调试，应放入低频诊断组件或拓扑缓存。
- 删除 `Item.Position`，视觉位置直接存放在 `LocalTransform`；如逻辑需要位置，则保留逻辑插值状态但不要与 Transform 双写。

### 5.3 Item 生命周期

短期：

- 在 Item Prefab 的 Baker 中预置 `Item`，运行时使用 SetComponent，避免实例化后的 AddComponent。
- 缓存 `ItemId -> Prefab Entity`，仅在 Catalog 变化时重建。
- 使用标准 ECB System 延迟并合并 Playback。

中期：

- 使用物品池和 `IEnableableComponent` 标记活跃物品；
- 建筑输入后将 Entity 返回池中，而不是 Destroy；
- 建筑输出优先复用池中 Entity，而不是 Instantiate。

大规模方案：

- 逻辑物品使用 Belt Buffer、批次或压缩状态表示；
- 只为可见物品创建渲染 Entity；
- 逻辑吞吐量不再直接决定结构变化次数。

### 5.4 Grid 和视觉拓扑

- 保留 `GridOccupancyIndexSystem` 的 Revision 门控。
- 建造频繁时，将 occupancy 改成命令提交时增量更新；完整重建保留为校验和恢复路径。
- Belt 视觉只刷新修改格及其四邻域。
- 先计算四条边的最终可见状态，再只对真实变化执行 Add/Remove。
- 建筑 Transform 只在新建、移动、旋转或 Grid 参数变化时更新。

### 5.5 Item Port

- 将 AppliedTransferCount 和 outstanding reservation 放入端口 ECS 状态，替代长期托管 Dictionary。
- 对固定数量端口，优先使用固定槽位组件或带双缓冲索引的单 Buffer。
- 如果继续保留 Current/Next，使用 Tick generation 选择有效版本，避免每 Tick逐元素复制。
- Processor 和 Storage 的端口 Job 在确认查询互斥和依赖安全后再并行化。

### 5.6 表现层和测试系统

- 缓存 `EcsGridInteractionController` 使用的 EntityQuery 和 Singleton Entity。
- Database Blob 只在 World 或 Catalog 变化时重新获取。
- BuildResult 只在收到 Revision/事件提示后读取，避免每帧轮询和创建查询。
- 将 `Assets/Scripts/DotsTest` 放入独立 asmdef、测试 World，或对演示系统使用 `[DisableAutoCreation]`。

4096 规模场景的首轮实测基线、原始数据和瓶颈结论保存在
[`PerformanceReports/4096-baseline/SUMMARY.md`](../PerformanceReports/4096-baseline/SUMMARY.md)。

## 6. 分阶段实施计划

### Phase 0：建立基线和正确性保护

任务：

- [ ] 为直线移动、下游阻塞、两个来源争抢同一目标建立自动化测试。
- [ ] 为 Merger 三输入 round-robin 建立测试。
- [ ] 为 Splitter 三输出 round-robin 和阻塞回退建立测试。
- [ ] 为满环原子移动、非满环阻塞和环路外部输入建立测试。
- [ ] 为建筑输入消费和建筑输出注入建立测试。
- [ ] 建立 128、512、1024、4096 节点的性能场景（4096 压力档已落地）。
- [ ] 在 Unity Profiler 中记录 Main Thread、Worker、GC Alloc、Job Wait、Structural Changes 和 Fixed Tick 数量。

退出条件：

- 所有确定性规则都有自动化覆盖；
- 保存一份未优化基线数据；
- Profiler 测试场景可以重复运行并得到相近结果。

### Phase 1：低风险止血优化

任务：

- [x] 缓存 Item Prefab Index。
- [x] 缓存端口 Owner 顺序，只在 Grid Revision 变化时重建。
- [x] 为 Item Prefab 预置 Item 组件。
- [x] 缓存表现层 EntityQuery 和 Singleton。
- [x] 为 GridPlacement Transform 增加 Dirty/Revision 门控。
- [x] 清理或隔离自动创建的 DotsTest Systems。
- [x] 移除稳态 Tick 中能直接消除的托管临时集合。

退出条件：

- 稳态 GC Alloc 显著下降；
- 建造、拆除和端口行为回归测试通过；
- Grid 未变化时不再遍历并写入全部建筑 Transform。

实施记录（2026-08-05）：

- `BeltTransferSystem` 持久复用 Item Prefab Index、Cell Index 和目标保留集合；
  输入/输出端口 Owner 只在 `GridDefinition.Revision` 变化时重新抓取和排序。
- Item Prefab 在烘焙阶段由 `ItemPrefabBakingSystem` 预置 `Item`，运行时实例化
  路径由 `AddComponent` 改为 `SetComponent`，性能场景初始化路径同步更新。
- `EcsGridInteractionController` 按 Default World 生命周期缓存 Grid/Database
  Query、Singleton Entity 和 Database Blob 引用。
- `GridPlacementTransformSystem` 只在 Grid Revision 变化时调度全量对齐 Job。
- DotsTest 下 4 个演示系统均标记 `[DisableAutoCreation]`。
- 新增建筑输入、建筑输出和端口 Owner Revision 失效测试。
- 当前机器的采集结果与原基准硬件隔离保存，不做跨硬件结论；采集说明见
  [`PerformanceReports/phase1-current-hardware/README.md`](PerformanceReports/phase1-current-hardware/README.md)。
- i9-12900HX 基线机器上的 Phase 1 同机复测与对照分析见
  [`PerformanceReports/phase1-local-20260805/README.md`](PerformanceReports/phase1-local-20260805/README.md)。

### Phase 2：拓扑缓存和 O(N) Resolver

任务：

- [x] 创建持久化 Native Transport Topology。
- [x] 以 Grid Revision 驱动拓扑重建。
- [x] 预计算每个目标的有效输入源。
- [x] 将候选选择改为 `O(N)`。
- [x] 将 Loop 检测移出每 Tick 路径。
- [x] 移除多轮全图 Resolve。
- [x] 保持旧 Resolver 作为临时对照实现，通过测试后再删除。

退出条件：

- 4096 节点下解析时间随节点数近似线性增长；
- 新旧 Resolver 在所有确定性测试中产生相同状态；
- Grid 不变化时不重建 Cell Dictionary、连接或 Loop 数据。

实施记录（2026-08-05）：

- 新增 `FactoryLinearTransferResolver`，使用 Persistent
  `NativeParallelHashMap` 和 `NativeList` 缓存 Cell Index、输入、输出与环路；
- `BeltTransferSystem` 由 `GridDefinition.Revision` 驱动缓存失效，并与建筑
  端口共用 Native Cell Index；
- 每个目标只检查预计算的 0～3 个输入，链路和满环改用显式栈解析；
- 外层 `JunctionCount + 1` 次 Resolve 已移除，Splitter 回退最多执行三个
  固定仲裁波次，因此解析工作量上界保持 `O(N)`；
- Unity EditMode 回归与新旧 Resolver 差分测试为 `23 passed / 0 failed`；
- 128、512、1024、4096 节点测试的候选检查次数分别为 127、511、1023、
  4095；当前 Editor 样本耗时分别为 0.0315、0.0913、0.2554、0.7313
  ms/Tick。详细记录见
  [`PerformanceReports/phase2-resolver-20260805/README.md`](PerformanceReports/phase2-resolver-20260805/README.md)。
- 三个完整性能场景已使用独立 Batch Mode 进程采集，结果见
  [`PerformanceReports/phase2-scenes-20260805/README.md`](PerformanceReports/phase2-scenes-20260805/README.md)。

### Phase 3：Burst 化和数据布局重构

任务：

- [x] 将 Resolver 迁移到 Burst 可编译的 Native Job。
- [x] 拆分 BeltTopology 和 BeltState。
- [x] 删除或迁移 NextCell、HasOutput、IsLoop 等低价值字段。
- [x] 用 Chunk/Entity Job 批量提交状态，删除主线程逐实体写回。
- [x] 移除 NativeArray 到托管 Array 的中间转换。
- [x] 消除 BeltProgress 到 Transfer 之间不必要的主线程等待。

退出条件：

- 稳态 Fixed Tick `GC.Alloc = 0 B`；
- Transfer 主路径可由 Burst 编译；
- Profiler 中不再由托管 `BeltTransferSystem.OnUpdate` 占据主要模拟时间；
- 不出现非必要的 `WaitForJobGroup`。

实施记录（2026-08-06）：

- `FactoryTransferArbitrationJob`（`[BurstCompile]` `IJob`）承担单个 Fixed
  Tick 的全部仲裁与建筑端口转移；持久拓扑、动态状态、候选/状态/已接受/
  传输队列和统计输出全部改为 Native 容器，`EnableInterface` 字节区分 ECS
  接口路径（`ComponentLookup`/`BufferLookup` + ECB）与 NativeArray 回归路径。
- `Belt` 拆分为静态 `BeltTopology`（`Cell`/`Direction`/`CellsPerSecond`）
  与高频 `BeltState`（`CurrentItem`/`Progress`）；删除低价值的
  `NextCell`、`HasOutput`、`IsLoop` 字段，环路数改为拓扑重建时统计。
- 主线程逐实体写回已删除：Job 通过 `ComponentLookup` 直接写回
  `BeltState`/`Merger`/`Splitter`；`BeltTransferSystem.OnUpdate` 不再持有
  托管快照数组、`HashSet` 或 `Dictionary`。
- 稳态 Tick 移除 NativeArray 到托管 Array 的中间转换，并取消
  `Dependency.Complete()` 的每 Tick 调用；拓扑与端口 Owner 缓存仅在
  `GridDefinition.Revision` 变化时重建，重建时才有少量托管分配。
- 新增 `TransferCommandBufferSystem`（`EntityCommandBufferSystem` 子类）
  延迟回放 Instantiate/Destroy，主线程不再在 `BeltTransferSystem.OnUpdate`
  内等待整个仲裁 Job 并立即 Playback。
- 建筑输出注入的物品通过 `ecb.SetComponent` 写回目标组件，使
  deferred entity 在 ECB playback 时被 remap 为真实实体；相关测试覆盖了
  playback 前后目标节点状态。
- 测试基建同步迁移：`TransportScenario` 改为 `List<BeltTopology>` +
  `List<BeltState>`，回归路径直接调用仲裁 Job；`FactoryWorldFixture`
  的 `UpdateTransferTick` 按 `BeltProgressSystem → BeltTransferSystem →
  TransferCommandBufferSystem` 顺序推进。
- Unity EditMode 回归为 `39 passed / 0 failed`，覆盖 Phase 1/2 既有规则
  与 Phase 3 新增的组件拆分、多 Tick 链路、统计输出和 ECB 生命周期测试。
- 性能场景进程采集结果见
  [`PerformanceReports/phase3-scenes-20260806/README.md`](PerformanceReports/phase3-scenes-20260806/README.md)：
  `BeltTransferSystem.OnUpdate` 主路径降至 0.034～0.051 ms/Tick，GC 降至
  47～48 KiB/Tick，三个核心场景均稳定 60+ Tick/s；稳态 `GC.Alloc = 0 B`
  尚未达成，残余分配主要来自每 Tick 的 ECB 创建/回放与帧基线分配。

### Phase 4：视觉解耦和 Item 池化

任务：

- [x] 将 Item Transform 更新移到 Presentation System。
- [x] 保存前后 Tick 状态并进行渲染插值。
- [x] 删除 Item.Position 与 LocalTransform.Position 的重复写入。
- [x] 改为以 Item 为中心的连续查询。
- [x] 实现 Item Entity Pool 和 Enableable Active 状态。
- [ ] 可选：增加视野裁剪，只更新可见物品 Transform。

退出条件：

- 一个渲染帧内即使执行多个 Fixed Tick，也只进行一次物品视觉更新；
- 稳态运输不再频繁 Instantiate/Destroy Item；
- 逻辑结果不受相机可见性和渲染帧率影响。

实施记录（2026-08-07）：

- 旧 `BeltItemPositionSystem` 从 `FixedStepSimulationSystemGroup` 移除；
  新增 `ItemVisualStateCaptureSystem`（Fixed Tick 末尾采样 From/To 位置）与
  `ItemTransformPresentationSystem`（`PresentationSystemGroup` 中以 Item 为
  中心的连续查询，按前后节点位置与 `Progress` 插值，每渲染帧写一次
  `LocalTransform`/`LocalToWorld`，并保留 `PostTransformMatrix` 非均匀缩放）。
- `Item` 改为 `IEnableableComponent`，删除 `Position` 字段；禁用状态表示
  池中空闲物品，启用状态表示正在运输或被建筑持有的物品。
- 新增 `ItemPool` / `ItemPoolEntry` 和 `ItemPoolInitializationSystem`，
  按 Item Prefab Catalog 创建每个物品类型的空闲池；`BeltTransferSystem`
  持久缓存 `ItemId -> Pool` Native 映射，仅在池数量变化时重建。
- `FactoryTransferArbitrationJob` 的建筑输入改为将物品放回池（无池时保留
  `DestroyEntity` 调试后备），建筑输出优先复用池中禁用实体，池空时仍走
  `Instantiate`；拆除建筑的 `GridBuildCommandSystem` 同样优先归还物品。
- 初始物品注入和 Item Prefab Baking 为实例写入 `ItemVisualState`，不再写
  `Item.Position`。
- Unity EditMode 回归为 `55 passed / 0 failed`，新增
  `BeltTransferSystemPhase4Tests` 覆盖池归还、池复用、无池回退、视觉快照
  采样、Presentation 进度插值和非均匀缩放保持。
- 性能采集见
  [`PerformanceReports/phase4-20260807/README.md`](PerformanceReports/phase4-20260807/README.md)。
- Phase 4 后 FullLoop 压力测试（2 → 1024）见
  [`PerformanceReports/StressTest/README.md`](PerformanceReports/StressTest/README.md)：
  极限档 65536 节点 TPS 28.16 → 31.78，帧 P95 733 ms → 618 ms。

### Phase 5：Port、建造和拓扑尖峰优化

任务：

- [x] 将 Port reservation 移入 ECS 状态。
- [x] 使用 generation 或缓冲索引替代 Current/Next 全量复制。
- [x] 建造命令直接增量更新 occupancy。
- [x] Belt 视觉只刷新受影响节点和邻居。
- [x] 消除一次刷新中对同一 DisableRendering 的反复 Add/Remove。

退出条件：

- 连续拖动建造传送带时没有明显主线程尖峰；
- Port Buffer Swap 不再是可见的内存带宽热点；
- 托管 accepted Dictionary 不再随建筑历史数量无限增长。

实施记录（2026-08-07）：

- `ItemInputPortSnapshot` / `ItemOutputPortSnapshot` 新增
  `ReservedTransferCount`，Port reservation 从 Resolver 的
  `NativeParallelHashMap` 移入 ECS 快照，不再存在随建筑历史增长的
  accepted Dictionary/Map。
- `ItemPortBufferSwapSystem` 不再复制 Current/Next 缓冲，只切换
  `ItemPortBufferGeneration`；`ItemPortAdapterSystem` 和仲裁 Job 按 generation
  通过 `Reinterpret` 选择当前缓冲。
- `GridBuildCommandSystem` 为新建建筑写入 `PendingOccupancyAdd`，拆除时直接
  从 `GridOccupancyIndexSystem` 增量移除占用；occupancy 不再每次建造全量重建。
- `BeltTopologyVisualSystem` 改为消费 `BeltVisualDirtyCell`，只刷新脏单元格
  和邻居；`DisableRendering` 变更先进入 ECB，循环结束后统一播放，避免
  Lookup 被结构变更失效和反复 Add/Remove。
- 新增 `Phase5OptimizationTests` 覆盖 generation 切换、reservation 落 ECS、
  occupancy 增量应用和 Belt 脏单元格清理；`Factory.Tests` 为
  `59 passed / 0 failed`。
- 默认 `scale=64` 连续建造/拆除对比见
  [`PerformanceReports/phase5-20260807/README.md`](PerformanceReports/phase5-20260807/README.md)。

## 7. 性能测试矩阵

每次 Phase 完成后至少测试以下组合：

| 节点数 | Junction 比例 | 物品状态 | 拓扑类型 |
|:---:|:---:|:---|:---|
| 128 | 0% | 空载、满载 | 直线 |
| 512 | 10% | 满载 | 多合流、多分流 |
| 4096 Belt | 0% | 50% 交错装载 | `64 × 64` 长蛇形、无接收端 |
| 5165 Belt + 16 Splitter | <1% | 1024 物品满载主干 | F 型 16 分叉、每叉 256 格 |
| 4097 Belt + 1 Storage | 0% | 满载、慢速尾端 | 4096 格四级主带接一级尾带与仓库 |

同时单独测量：

- 使用 `Perf_ContinuousBeltBuild` 连续拖动放置 `scale` 条 `scale` 格传送带，
  再逐条拆除；
- 连续绘制最长 Belt Path；
- 连续拆除 Belt Line；
- 大量建筑同时完成生产并输出；
- 低帧率下 FixedStep 追帧；
- 相机同时看到全部物品和只看到少量物品。

## 8. 验收指标

核心指标：

- 稳态 Fixed Tick：`GC.Alloc = 0 B`。
- 128 → 512 → 1024 → 4096 节点的解析耗时接近线性增长。
- 60 Hz 下模拟部分建议控制在 `3～5 ms`，为渲染和交互预留预算。
- Profiler 中不出现由 Transfer 快照导致的长时间主线程 Job 等待。
- 除建造、SubScene 加载和池容量扩张外，稳定运输阶段不发生大规模结构变化。
- 同一输入和初始状态在多次运行中产生一致结果。

如果目标硬件仍无法稳定执行 60 Hz，可以在完成算法重构和视觉插值后评估将逻辑频率降低到 20 或 30 Hz。降低 Tick 频率不能作为替代算法优化的第一手段，并且需要重新验证配方耗时、传送速度和 round-robin 公平性。

## 9. 风险与回滚策略

### 确定性风险

并行候选写入不能使用“先抢到者获胜”的逻辑。所有 winner 必须由稳定优先级计算。优化阶段保留旧 Resolver，通过同输入双跑比较结果。

### Entity 生命周期风险

Item Pool 必须区分逻辑活跃状态和渲染状态。返回池中的物品不能继续被 Belt、Merger、Splitter 或 Receipt 引用。

### 拓扑失效风险

所有建造、拆除、旋转、载入和 Grid 参数修改都必须推进 Revision。开发构建中可定期用全量扫描校验缓存拓扑，正式构建关闭校验。

### Job 安全风险

当前位置 Job 使用 `NativeDisableParallelForRestriction`，依赖“同一 Item 不会被两个节点引用”的隐含约束。重构后应显式保证 Item 唯一所有权，并增加断言或一致性检查，避免把逻辑错误变成数据竞争。

### 分阶段回滚

- 每个 Phase 独立提交；
- Phase 2 期间保留旧 Resolver 开关；
- Phase 4 池化前保留 Instantiate/Destroy 路径作为调试后备；
- 性能优化提交不得与玩法规则修改混在同一个提交中。

## 10. 完成定义

只有同时满足以下条件，才能认为本轮 ECS 性能优化完成：

- [ ] 所有传送、合流、分流、环路和端口回归测试通过。
- [ ] 稳态 Tick 无托管 GC 分配。
- [ ] Resolver 为近似 `O(N)`，且不在每 Tick 重建拓扑。
- [ ] Transfer 主路径由 Burst Native Job 执行。
- [ ] 逻辑状态提交不再逐 Entity 通过主线程 EntityManager 完成。
- [ ] Grid 和建筑 Transform 只在状态变化时更新。
- [ ] 物品视觉每渲染帧最多更新一次。
- [ ] Item 创建销毁不会随稳定吞吐量持续产生结构变化。
- [ ] 4096 节点压力场景达到目标帧预算且结果确定。
- [ ] Profiler 基线、优化后数据和测试场景一并保留。
