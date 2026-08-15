# 多层稀疏网格建造系统分阶段改造方案

## 1. 文档目的

本文档定义把当前“唯一、单层、完整矩形网格”改造成“统一整数坐标、稀疏非矩形表面、
多高度平面和特殊跨层连接器”的实施方案。

方案覆盖：

- 数据地址和 ECS 组件迁移；
- 地基提供可建造表面的规则；
- 建筑占用、放置验证和输入拾取；
- 平面传送带、坡道传送带和垂直传送带；
- 区块存储、拓扑失效和局部更新；
- 自动化回归、性能门槛、存档兼容和旧代码退役。

本文档是 `Future-Plan.md` 中“多层建造、多网格区块、垂直运输”条目的详细实施依据。

当前技术基线：

- Unity `6000.3.19f1`
- Entities `1.4.8`
- Unity Physics (`com.unity.physics`) `1.4.7`
- 固定模拟频率：`60 Tick/s`
- 正式运行时程序集：`Factory.Runtime`
- 测试程序集：`Factory.Tests`

## 2. 已确定的架构决策

### 2.1 不创建需要合并、分裂的独立网格坐标系

本项目中的固定地基满足以下条件：

- 所有平面使用相同格子尺寸；
- X/Z 轴方向一致；
- 平面原点位于整数格；
- 平面高度差是统一层高的整数倍。

因此固定世界只使用一套全局整数坐标，不为每块地基创建独立 `Grid Entity`，也不在
相邻地基接触时执行坐标系合并。

统一空间地址为：

```text
GridCell = (X, Level, Z)
```

其中 `Level` 是离散高度层，不是普通建筑 footprint 的第三个尺寸。普通建筑仍然只在
同一 `Level` 上用二维偏移占格。

只有未来出现以下需求时，才新增真正独立的 `GridSpaceId + LocalCell`：

- 可旋转网格；
- 可移动平台或载具；
- 不同格子尺寸；
- 不能映射到统一世界整数格的局部空间。

这些需求不在本轮改造范围内。

### 2.2 区块是存储单位，不是逻辑网格身份

场景按固定 X/Z 尺寸切分区块，推荐初始值为 `16 × 16`。每个高度层分别保存表面数据，
区块键可表达为：

```text
SurfaceChunkKey = (ChunkX, Level, ChunkZ)
```

空区块不创建。区块负责：

- 稀疏表面存在性和表面权限；
- 局部 Revision；
- 局部 Mesh、Collider 和调试网格；
- 存档、加载和未来的流式管理。

区块边界不代表地基连通边界。跨区块的相邻表面仍然属于同一个世界坐标体系。

### 2.3 连通区域是派生缓存，不是永久身份

当玩法确实需要判断地基是否连通时，可以生成 `SurfaceRegionId`，但它必须满足：

- 可被重新计算；
- 不进入建筑永久 ID；
- 不进入存档引用；
- 区域合并或分裂时不修改建筑坐标；
- 删除桥接地基时允许分帧重算。

第一版如果没有供电、结构强度或区域归属等消费者，不实现连通区域系统。

### 2.4 表面、建筑占用和连接器占用相互独立

至少拆分以下空间索引：

```text
SurfaceRegistry
    表示哪些平面格存在地基，以及允许放置什么。

BuildingOccupancy
    表示普通机器、平面传送带等占用了哪些平面格。

RampConnectorOccupancy
    表示坡道上的一维传送带槽。

VerticalConnectorOccupancy
    表示同一 X/Z 柱中的垂直运输区间。
```

地基是建筑的承载表面，不与地基上方建筑争用同一个 `BuildingOccupancy`。

### 2.5 Revision 按职责拆分

不得继续使用一次普通建造就使所有缓存失效的单一 `GridDefinition.Revision`。目标 Revision
至少分为：

```text
SurfaceTopologyRevision
BuildingOccupancyRevision
TransportTopologyRevision
TransportVisualRevision
```

区块还应拥有局部 `SurfaceChunkRevision`。普通地基变化不能无条件重建全世界传送带拓扑，
普通机器变化也不能触发全部建筑重新对齐。

## 3. 目标数据模型

以下类型名称是推荐命名，实施时可以根据程序集现状微调，但职责不得重新混合。

### 3.1 全局坐标配置

```csharp
public struct WorldGridConfig : IComponentData
{
    public float CellSize;
    public float LayerHeight;
    public float3 Origin;
}

public struct GridCell : IEquatable<GridCell>
{
    public int X;
    public int Level;
    public int Z;
}
```

统一换算规则：

```text
worldX = Origin.x + (X + 0.5) * CellSize
worldY = Origin.y + Level * LayerHeight
worldZ = Origin.z + (Z + 0.5) * CellSize
```

`WorldGridConfig` 在 Authoring/Baking 阶段必须验证：`CellSize > 0`、`LayerHeight > 0`，
并保证固定世界的 Origin 与项目整数坐标约束对齐。任意一块连通或不连通的平面区域都直接
使用全局格坐标，因此其可选“局部原点”天然位于整数格，不再保存另一套浮点原点。

坐标负数区域的区块除法必须使用 floor division，不能直接依赖 C# 对负数的截断除法。

### 3.2 普通建筑占格

```csharp
public struct GridPlacement : IComponentData
{
    public GridCell AnchorCell;
    public int2 FootprintSize;
    public byte QuarterTurns;
    public BuildingKind Kind;
}
```

以下数据继续保持二维：

- `OccupiedCellOffset.Value : int2`
- `BuildingPort.CellOffset : int2`
- `BuildingPort.Direction : int2`
- 建筑旋转：绕 Y 轴四个直角方向
- 建筑 footprint：只覆盖 Anchor 所在高度层

建筑占格计算规则为：

```text
occupied.X     = Anchor.X + rotatedOffset.x
occupied.Level = Anchor.Level
occupied.Z     = Anchor.Z + rotatedOffset.y
```

### 3.3 稀疏表面

表面至少需要表达：

```csharp
[Flags]
public enum SurfaceFlags : byte
{
    None = 0,
    Flat = 1 << 0,
    AllowsBuildings = 1 << 1,
    AllowsBelts = 1 << 2
}
```

建议每个 `SurfaceChunkKey` 对应一个区块层实体或持久 Native 数据块。`16 × 16` 区块的表面
存在性可用 256 bit 位图表达；表面权限使用附加位图或紧凑数组。

需要追踪地基所有者时，单独维护局部索引：

```text
LocalCellIndex -> Foundation Entity
```

不得通过每次查询 ECS 世界中的全部地基实体来判断表面是否存在。

### 3.4 特殊连接器

坡道地基连接两个整数层端点，中间位置不成为普通平面格：

```csharp
public struct RampFoundation : IComponentData
{
    public GridCell StartCell;
    public GridCell EndCell;
    public int2 HorizontalDirection;
    public ushort Run;
    public ushort Rise;
}

public struct RampSlot : IBufferElementData
{
    public ushort Index;
    public Entity Belt;
}
```

垂直传送带连接相同 X/Z 的两个整数层端点：

```csharp
public struct VerticalBelt : IComponentData
{
    public GridCell BottomCell;
    public GridCell TopCell;
}
```

垂直区间与地基表面使用不同占用通道，因此允许穿过中间地基。垂直带之间的冲突通过
`(X, Z) + [BottomLevel, TopLevel]` 区间相交判断处理。

### 3.5 运输拓扑

平面运输节点至少把 `Cell` 从 `int2` 迁移到 `GridCell`。坡道和垂直带不依赖
`cell + direction` 隐式跨层，而是产生显式拓扑边。

目标关系为：

```text
平面带：同层相邻 GridCell 自动推导边
坡道带：Ramp Connector 显式连接两端和内部槽
垂直带：Vertical Connector 显式连接上下端点
Merger/Splitter：继续使用确定性的有限输入/输出端口
```

运输模拟仍保持现有不变量：单节点单物品、固定 Tick、确定性仲裁、满环原子移动和
Merger/Splitter round-robin。

## 4. 分阶段实施总览

| 阶段 | 目标 | 对玩家可见变化 |
|---:|---|---|
| 0 | 冻结基线、补齐回归和性能采样 | 无 |
| 1 | 全链路迁移到统一 `GridCell`，旧场景固定 `Level=0` | 无 |
| 2 | 持久空间索引和 Revision 解耦 | 无，建造尖峰应下降 |
| 3 | 稀疏区块表面和地基规则，先支持单层非矩形 | 可自由扩展非矩形地基 |
| 3.5 | 地基接入建筑资源、UI 和统一矩形命令 | 从空白工作平面手动批量铺设地基 |
| 4 | 多高度平面、实际表面拾取和跨层 UI | 可在多个整数高度层建造 |
| 5 | 运输拓扑地址升级和显式连接边基础设施 | 平面传送带行为不变 |
| 6 | 坡度地基及坡道专用传送带槽 | 支持 `1`、`1/2`、`1/4` 坡道 |
| 7 | 垂直传送带和竖直区间占用 | 支持上下层直接运输 |
| 8 | 连通缓存、区块流式、存档定稿和旧代码退役 | 大规模世界稳定化 |

不得同时跳过阶段 1、2，直接在现有 `int2` 单例网格上增加多个 Grid Entity。阶段 3～7
可以按可玩内容优先级调整，但阶段 5 必须先于坡道和垂直运输。

## 5. 阶段 0：冻结行为与性能基线

### 5.1 目标

在改变空间地址前，把当前单层行为固定为可自动验证的契约，并采集后续比较所需基线。

### 5.2 工作项

1. 补齐现有网格行为测试：
   - 世界坐标与格子坐标双向换算；
   - 四个方向的建筑 footprint 旋转；
   - 越界、占用冲突、放置和拆除；
   - 单格和多格建筑；
   - L 形传送带 X 优先和 Z 优先；
   - 路径任意格失败时整条回滚；
   - Belt、Merger、Splitter、端口、满环和阻塞行为。
2. 为 `GridBuildCommandSystem` 增加诊断计数：
   - 每批命令扫描的 Placement 数；
   - 创建的临时记录数；
   - 路径验证格数。
3. 为拓扑系统记录：
   - 重建次数；
   - 重建节点数；
   - 重建耗时。
4. 采集当前 32×32 场景和现有压力场景的：
   - Fixed Tick CPU；
   - 建造操作主线程耗时；
   - GC Alloc；
   - Persistent Native 内存；
   - 渲染帧耗时和 Draw Call。
5. 明确当前是否已有正式存档；如果存在，固定旧版本号和迁移样本。

### 5.3 验收标准

- 所有现有自动化测试通过。
- 性能报告可重复采集，并记录运行机器、Unity 版本、场景和采样参数。
- 后续阶段可以判断回归来自坐标迁移、空间索引还是新增玩法。

## 6. 阶段 1：统一三维地址，保持单层玩法

### 6.1 目标

把所有具有“世界格位置”语义的数据迁移到 `GridCell`，但旧场景和旧建造操作全部使用
`Level = 0`。本阶段不引入地基表面，不改变矩形边界规则。

### 6.2 数据迁移范围

必须检查并迁移：

- `GridPlacement.AnchorCell`
- `GridBuildCommand.StartCell/EndCell`
- `GridBuildPlayerCommand.StartCell/EndCell`
- `GridBuildResult.Cell`
- `RecipeSelectionCommand.BuildingCell` 及结果
- `BeltTopology.Cell`
- `Merger.Cell`
- `Splitter.Cell`
- `BeltVisualDirtyCell`
- Occupancy、Build Snapshot、Transport Resolver 中的坐标 HashMap/Dictionary
- UI Snapshot、悬停建筑查询和调试输出
- 性能场景、测试 Fixture 和测试断言

以下类型暂不迁移为三维：

- 建筑局部占格偏移；
- 建筑端口偏移和方向；
- 平面路径方向；
- footprint 宽高。

### 6.3 运行时 ID

停止使用只从 `int2 cell` 生成容器 ID 的逻辑。优先采用独立、单调分配并可持久化的
`BuildingRuntimeId`。如果阶段 1 暂时保留坐标派生 ID，必须包含 Level、明确位宽范围并有
碰撞测试；该过渡方案不得进入最终存档格式。

### 6.4 兼容方式

- `GridDefinitionAuthoring` 继续生成旧矩形范围。
- `WorldGridConfig` 可以先与旧定义并存，但坐标换算只能有一个实现入口。
- 所有旧内容自动映射到 `Level = 0`。
- 禁止长期维护 `int2` 和 `GridCell` 两套并行建造逻辑。

### 6.5 验收标准

- 原场景的建筑位置、方向、端口和运输行为像素级/逻辑级不变。
- 所有旧测试改用 `Level = 0` 后通过。
- 在相同 32×32 基线下稳态 Fixed Tick 无持续 GC。
- 坐标迁移本身的稳态 CPU 回归以阶段 0 基线为准，暂定中位数不超过 5%；超出时必须
  先定位 HashMap、组件尺寸或 Burst 退化原因。

## 7. 阶段 2：持久索引和失效范围解耦

### 7.1 目标

在世界规模扩大前消除现有全局扫描和不必要的全局缓存失效。

### 7.2 Building Occupancy

将现有增量 Occupancy 扩展为 `GridCell -> Entity` 的持久 Native 索引，保留：

- 创建建筑时增量添加；
- 拆除时增量移除；
- 冲突检测和开发期全量校验；
- 只读 Job 访问接口。

`GridBuildCommandSystem` 不再为每批命令构造全世界 `List<PlacementRecord>` 和
`Dictionary<GridCell, PlacementRecord>`。候选建筑只查询：

- 自己的占格；
- 端口和特殊建筑规则需要的有限邻居；
- 当前命令批次内尚未 Playback 的暂存变更。

批次内暂存可以使用可复用 Native 容器或生命周期明确的临时容器，不得为世界中的每栋
建筑创建托管对象。

### 7.3 Revision 拆分

新增独立 Revision，并迁移消费者：

| Revision | 写入者 | 消费者 |
|---|---|---|
| `SurfaceTopologyRevision` | 地基/表面变化 | Surface Mesh、Collider、连通缓存 |
| `BuildingOccupancyRevision` | 普通建筑占用变化 | 查询缓存、UI Snapshot |
| `TransportTopologyRevision` | 运输节点/方向变化 | Belt Transfer Resolver |
| `TransportVisualRevision` 或 Dirty Cells | 运输外观变化 | Belt Topology Visual |

普通机器、地基或 UI 变化不得触发运输拓扑全量重建。

### 7.4 Transform 更新

- 新建筑在创建时直接得到最终 Transform。
- 固定世界的 `WorldGridConfig` 不因普通建造而变化。
- 移除“任意 Grid Revision 变化就重新对齐所有 `GridPlacement`”的依赖。
- 如果需要修复或编辑器强制重对齐，使用显式 `GridTransformDirty` 标记。

### 7.5 验收标准

- 在远处增加普通建筑时，扫描的既有 Placement 数不随全世界建筑数线性增长。
- 普通地基或非运输建筑变化不增加 `TransportTopologyRebuildCount`。
- 新增一个建筑不会重写全部建筑 Transform。
- 相同基线场景的建造操作无新增托管 GC；稳态 Tick 保持无持续 GC。

## 8. 阶段 3：单层稀疏表面和非矩形地基

完成状态（2026-08-15）：已实现单层稀疏 Surface Registry、统一地基命令、确定性区块
Mesh/Compound Collider、旧矩形迁移和身份恢复；完整回归与运行时采样见
[`PerformanceReports/multilevel-stage3-20260815`](../PerformanceReports/multilevel-stage3-20260815/README.md)。

### 8.1 目标和本阶段固定契约

在 `Level = 0` 上用稀疏表面替代完整矩形 `Contains()`，实现玩家通过地基逐格扩展可建造
区域。此阶段先不开放多层和坡道，但地基体积、网格生成和碰撞生成接口必须能直接扩展到
阶段 4 的垂直堆叠，不得在阶段 4 再引入第二套地基表示。

本阶段开始前固定以下契约：

- 标准地基是轴对齐、实心、不透明的 `1 × 1 × 1` 立方体，不允许通过 Transform Scale
  改变其物理尺寸。
- `WorldGridConfig.CellSize = 1`，`WorldGridConfig.LayerHeight = 1`；如果未来允许其他值，
  标准地基尺寸始终为 `CellSize × LayerHeight × CellSize`，所有系统只从统一配置读取。
- `GridCell` 表示地基提供的顶面和建筑放置平面。其世界高度为
  `SurfaceY = Origin.y + Level * LayerHeight`；地基中心高度为
  `SurfaceY - LayerHeight * 0.5`，体积范围为 `[SurfaceY - LayerHeight, SurfaceY]`。
- 每格地基继续拥有独立玩法实体和稳定身份，但该实体不拥有独立 Renderer 或
  `PhysicsCollider`。渲染和碰撞都是可丢弃、可重建的区块缓存。
- 地基可以拥有不同的视觉材质 `VisualMaterialId`，但所有地基固定使用同一种全局
  `FoundationPhysicsMaterial` 和同一组 Foundation `CollisionFilter`。不为单格保存
  `PhysicsMaterialId`，不按物理材质分支或拆分 Collider。

### 8.2 稀疏表面和地基数据

实现：

- `SurfaceChunkKey -> Chunk Entity/Data` 的持久索引；
- 表面存在位图；
- `AllowsBuildings`、`AllowsBelts` 等权限；
- `LocalCellIndex -> Foundation Entity` 所有者映射；
- 每个已占用格子的 `VisualMaterialId` 和面遮挡属性；
- 局部 `SurfaceChunkRevision`、渲染脏队列和物理脏队列；
- 负坐标区块换算测试；
- 跨区块 footprint 查询。

初始 X/Z 区块尺寸固定通过一个配置常量提供，第一版使用 `16 × 16`，不要把尺寸散落硬编码
在多个系统中。`SurfaceChunkKey` 仍为 `(ChunkX, Level, ChunkZ)`，空区块不创建。

表面查询和地基体积查询第一版可以读取同一份占用数据，但 API 必须区分：

```text
HasSurface(GridCell)
HasFoundationVoxel(GridCell)
IsOpaqueFoundationVoxel(GridCell)
```

这能避免阶段 4 出现“下层表面仍存在，但已被上层地基体积占据”时，把表面存在误当成建筑
一定可放置。普通建筑的最终放置验证仍需检查其体积净空。

### 8.3 地基建造命令和局部失效

地基建造是现有 `GridBuildCommand` 的一种，不新增平行的 Foundation Command、命令 Buffer、
Command Bus 或独立命令处理系统。在 `GridBuildCommandType` 中增加明确的
`PlaceFoundation` 和 `RemoveFoundation`，并继续复用现有的 RequestId、Player、命令提交、
结果回传和确定性批处理管线。`GridBuildCommandSystem` 内部可以把地基分支拆成私有方法或纯
Utility，但事务边界和命令顺序仍由同一个建造命令系统控制。

`PlaceFoundation`/`RemoveFoundation` 操作应当以事务方式：

1. 验证目标地基体积和顶面是否允许创建；
2. 验证结构或地形支撑规则；
3. 创建只承载玩法数据的地基实体；
4. 向 Surface Registry 登记顶面、体积、所有者和视觉材质 ID；
5. 更新 `SurfaceChunkRevision` 和全局 `SurfaceTopologyRevision`；
6. 将所属渲染区块加入 dirty 队列；若修改发生在 X/Z 区块边界，同时标记对应邻区块；
7. 将所属物理区块加入 dirty 队列；
8. 由后续重建系统批量更新区块 Mesh 和 `PhysicsCollider`，命令执行过程不逐格创建表现对象。

第一版地基拆除规则固定为：地基提供的任意表面格上存在普通建筑时拒绝拆除。级联拆除如果
以后需要，必须作为明确命令并先计算完整影响集合，不能隐式执行。

### 8.4 区块渲染网格和相邻面剔除

地基视觉使用体素外表面提取，不为每个地基实例化完整立方体 Mesh。对每个地基体素检查
`-X/+X/-Y/+Y/-Z/+Z` 六个相邻位置：

```text
相邻位置存在不透明、完整地基体素 -> 不生成当前方向的面
相邻位置为空或不遮挡当前面       -> 生成当前方向的四边形
```

阶段 3 只有 `Level = 0`，但生成器和测试使用完整六方向接口。阶段 4 增加上、下层后，低层
地基的顶面和高层地基的底面自动按同一规则剔除。

两个相邻立方体原本共有 12 个单位面，接触处的两个面均不生成，结果为 10 个单位四边形、
20 个三角形。可在正确性稳定后增加 Greedy Meshing，把连续、共面且视觉材质/UV 规则相同
的面合并；上述两个立方体最终可化为 6 个四边形、12 个三角形。

渲染重建还必须满足：

- X/Z 区块边缘的相邻测试查询真实邻区块，不能把区块外无条件视为空；
- 修改边界格时重建当前渲染区块和必要邻区块，避免重复面和裂缝；
- 两个视觉材质不同但都不透明的地基相邻时，接触面仍然剔除；
- 透明、非完整方块等未来类型通过显式 `OccludesFaces`/面遮挡规则决定是否遮挡；
- 不根据当前摄像机逐帧重建 Mesh。CPU 只移除永远不可见的内部面，摄像机视锥、遮挡和
  背面剔除继续由渲染管线按区块处理。

### 8.5 视觉材质保留

第一版区块 Render Mesh 按 `VisualMaterialId` 生成 SubMesh。Entities Graphics 1.4 的正式
落地方式是：区块 Render Entity 使用一个区块 Mesh、一个包含本区块实际材质的
`RenderMeshArray`，并为每个 SubMesh 建立对应的 `MaterialMeshIndex`；
`MaterialMeshInfo.FromMaterialMeshIndexRange(...)` 选择整段材质/Mesh/SubMesh 组合，使一个
区块 Render Entity 发出所需的多材质 Draw Command。不得套用传统 GameObject
`MeshRenderer.sharedMaterials` 作为正式运行时路径，也不得为每个地基创建 Render Entity。

地基实体删除独立 Renderer 后，其 `VisualMaterialId` 仍必须保存在 Surface 数据或可 O(1)
查询的地基所有者数据中，以便被遮挡面重新暴露时恢复正确材质。

面合并只能跨越视觉材质、Shader 变体和 UV 规则都相同的面。SubMesh 数量等于该区块实际
使用的视觉材质数量，而不是全局材质数量。若压力测试发现材质切换或 Draw Call 超过预算，
再迁移到纹理图集或 Texture Array；不得通过丢失逐地基材质来换取单一 SubMesh。

### 8.6 Unity Physics 碰撞架构

本阶段正式使用 `com.unity.physics 1.4.7`，并给 `Factory.Runtime`、`Factory.Tests` 和需要
物理查询的 PlayMode 测试程序集增加 `Unity.Physics` 引用。

禁止每个地基实体拥有一个 `PhysicsCollider`。每个物理区块创建一个无 Parent 的静态物理
实体，至少包含：

```text
FoundationPhysicsChunk
LocalTransform
LocalToWorld
PhysicsWorldIndex(Value = 0)
PhysicsCollider
```

该实体不包含 `PhysicsVelocity` 或 `PhysicsMass`，因此作为无限质量静态体参与默认物理世界。
创建区块实体时预先添加 `PhysicsCollider { Value = default }`，后续重建只替换 Collider Blob，
避免每次地基变化都增加/删除组件。

第一版 Collider 生成固定使用“Greedy Box + CompoundCollider”：

1. 从物理区块内的 `int3` 地基体素集合生成互不重叠的轴对齐长方体；固定按
   `(Level, Z, X)` 选择最小未消费体素，并按 `+X`、`+Z`、`+Level` 顺序扩展，保证相同输入
   产生相同结果；目标是稳定压缩，不要求求解全局最少 Box；
2. 所有相邻地基体素都使用统一的 Foundation `CollisionFilter` 和
   `FoundationPhysicsMaterial`，因此 Greedy 合并不读取视觉材质，也没有物理材质分组分支；
3. 每个长方体创建一个 `Unity.Physics.BoxCollider`，`BevelRadius = 0`；
4. 所有 Box 作为 `CompoundCollider.ColliderBlobInstance` 子项；
5. `CompoundCollider.Create()` 生成一个持久 Blob，并赋给区块实体唯一的 `PhysicsCollider`；
6. Compound 创建完成后立即释放临时子 Box Blob，因为 Compound 已复制子 Collider 数据。

这表示“一整个区块是一个静态刚体和一个 `PhysicsCollider` Blob”，但内部仍保留少量 Box
叶节点。所有叶节点都写入同一份 `FoundationPhysicsMaterial` 配置。视觉材质完全不参与
碰撞合并，因此不同视觉材质的相邻地基仍可合并成同一个 Box child。

阶段 3 的物理区块只含 `Level = 0`，可以与 `SurfaceChunkKey` 一一对应。碰撞生成器从第一版
起必须接收 `int3` 体素而不是二维矩形专用输入。阶段 4 开放垂直堆叠前，引入独立的有限高度
分带键：

```text
FoundationPhysicsChunkKey = (ChunkX, LevelBand, ChunkZ)
LevelBand = FloorDiv(Level, PhysicsLevelBandSize)
```

`PhysicsLevelBandSize` 使用统一配置，初始建议为 `8`。同一物理区块内上下堆叠的立方体可被
三维 Greedy 合并成一个 Box 或一个 Compound；高度分带用于避免单个静态体的 AABB 跨越过多
空层，也避免任意高度修改重建整根世界柱。分带边界与 X/Z 区块边界一样是缓存边界，不改变
地基身份或全局坐标。

`Unity.Physics.MeshCollider` 不作为阶段 3 默认方案。只有性能实测证明外表面 Mesh Collider
比 Greedy Box Compound 更合适时，才允许以报告和回归测试为依据替换实现；单一物理材质
约束保持不变。

### 8.7 Collider 更新时序和 Blob 生命周期

物理重建系统运行在 `BeforePhysicsSystemGroup`，只消费物理 dirty 队列。不得在
`PhysicsInitializeGroup` 与当次物理管线结束之间增加、删除或替换物理实体组件。

每个运行时创建的 Collider Blob 都必须有唯一、明确的所有者：

- 区块记录当前拥有的 Compound Blob；
- 替换前完成或串联所有可能读取旧 Blob 的 Job 依赖；
- 新 Blob 写入 `PhysicsCollider` 后，安全释放旧 Blob；
- 区块变空、实体销毁或 World 退出时，通过 Cleanup 组件/专用释放系统回收最后一个 Blob；
- 禁止把运行时唯一 Blob 注册为多个区块共享后再由单一区块释放；
- 持续建造、拆除测试必须验证 Blob 数和 Persistent Native 内存不会单调增长。

渲染 dirty 与物理 dirty 分开处理。边界面变化通常要求邻渲染区块重建，但固定分区内的
Compound Box 不需要为了尝试跨区块合并而重建邻物理区块；Box 只在自己的物理区块内合并。

### 8.8 物理查询和地基身份恢复

本阶段完成合并 Collider 的查询契约、解析函数和自动化测试；玩家输入从旧无限 Plane 正式
切换到实际地基 Collider 仍属于阶段 4。所有使用地基 Collider 的查询统一读取
`PhysicsWorldSingleton`，并使用集中定义的 `CollisionFilter`。禁止在调用点散落 `~0u`
掩码；至少为地基、建筑、运输物和查询射线定义稳定的碰撞类别。

区块合并后，Raycast 命中的 `Entity` 是物理区块实体，不一定是独立地基实体。删除或选择
地基时按以下流程恢复身份：

1. 使用 `hit.Position - hit.SurfaceNormal * Epsilon` 得到碰撞体内部一点；
2. 由该点换算 `GridCell`/地基体素地址；
3. 在 Surface Registry 中 O(1) 查询 `Foundation Entity`；
4. 使用 `hit.SurfaceNormal` 区分顶面、底面和侧面，侧面命中不得错误解析成相邻空格。

`ColliderKey` 和 Compound child 的 `Entity` 字段可用于调试或未合并的一对一形状，但不得
作为持久地基 ID。Greedy 结果每次重建都可能改变 child 顺序和 `ColliderKey`。

### 8.9 旧场景兼容

旧 `GridDefinition.Size` 在 Baking 或启动初始化时生成一组完整矩形的 Surface Chunk、地基
所有者/材质数据以及对应的渲染和物理 dirty 请求，使所有旧场景仍然可用。完成迁移后，运行
时放置判断只读取 Surface Registry，不再同时读取矩形 `Contains()`。

旧矩形初始化不得为每格创建 Renderer、GameObject Collider 或独立 `PhysicsCollider`。它与
玩家逐格放置最终必须进入相同的区块 Mesh 和 CompoundCollider 重建路径。

### 8.10 实施顺序和交付物

阶段 3 按以下顺序实施，每一步保持项目可编译、可运行：

1. 给 `Factory.Runtime`、`Factory.Tests` 和相关 PlayMode asmdef 增加 `Unity.Physics` 引用，
   建立集中维护的 Foundation Collision Category、Filter 和唯一
   `FoundationPhysicsMaterial` 配置。
2. 新增地基实体、Surface Chunk、材质 ID、所有者映射、局部 Revision 和独立 dirty 队列
   数据类型；补齐坐标、负区块和 `1 × 1 × 1` 体积单元测试。
3. 实现 Surface Registry 和旧矩形场景初始化，先让所有旧放置验证只读新 Registry，再移除
   运行时 `GridDefinition.Contains()` 双读。
4. 给现有 `GridBuildCommandType`、命令适配器和 `GridBuildCommandSystem` 增加
   `PlaceFoundation`/`RemoveFoundation` 分支，实现事务式放置、拆除和承载建筑拒拆规则；
   此时可先使用调试表现，但不得创建逐格 Collider 或独立地基命令队列。
5. 实现六方向外表面提取、跨区块邻居查询、SubMesh 分组和确定性 Greedy 面合并测试。
6. 实现托管表现提交系统：消费生成结果，更新 Unity Mesh、`RenderMeshArray`、
   `MaterialMeshIndex` 范围和渲染 Bounds；回收被替换的旧运行时 Mesh。
7. 实现确定性 Greedy Box 生成、统一 Foundation Filter/Material 和 Compound Blob Builder。
8. 实现 `BeforePhysicsSystemGroup` 中的物理区块创建/替换/清理系统，以及 Collider Blob
   所有权、依赖和退出清理测试。
9. 实现基于 `PhysicsWorldSingleton` 的地基查询解析函数和自动化测试；正式玩家输入切换留到
   阶段 4。
10. 运行完整 EditMode/PlayMode 回归和新增压力场景，记录渲染、Collider 重建、静态 Body、
    Blob 内存和连续建造尖峰后再宣布阶段完成。

推荐新增或拆分的职责名称如下，最终文件名可按现有程序集约定调整：

```text
FoundationSurfaceComponents
SurfaceChunkUtility
SurfaceRegistrySystem
GridBuildCommandSystem（扩展现有系统）
SurfaceChunkMeshBuildSystem
SurfaceChunkRenderApplySystem
FoundationGreedyBoxUtility
FoundationPhysicsRebuildSystem
FoundationColliderBlobCleanupSystem
FoundationPhysicsHitResolver
```

Mesh 顶点/索引、Greedy 面和 Greedy Box 的纯数据计算应尽量在 Burst Job 中完成；
`UnityEngine.Mesh` 创建/更新、Entities Graphics 托管对象注册和需要 EntityManager 结构变更的
部分集中在提交阶段，不进入每个 Fixed Tick 的稳态热路径。

### 8.11 测试和验收标准

功能验收：

- 支持凹形、环形、分离岛屿和跨区块地基；
- 建筑 footprint 只要有一个格子缺少表面就整体失败；
- 地基相邻不会触发 Grid 合并或建筑坐标重写；
- 拆除桥接地基不会修改两侧建筑身份；
- 单次地基修改只重建受影响区块和必要邻渲染区块；
- 地基逻辑实体无独立 Renderer 和 `PhysicsCollider`；每个非空物理区块恰有一个静态
  `PhysicsCollider`；
- 两个水平相邻地基的普通表面提取结果为 10 个单位四边形，启用 Greedy 后为 6 个四边形；
- 两个相邻地基无论视觉材质是否相同，都生成一个 `2 × 1 × 1` Box child，并且所有 child
  使用唯一的 `FoundationPhysicsMaterial`；
- 地基放置和拆除只通过 `GridBuildCommandType.PlaceFoundation`/
  `GridBuildCommandType.RemoveFoundation` 进入现有命令管线，不存在第二套地基命令 Buffer
  或独立命令处理系统；
- 不同视觉材质在区块 Mesh 中保持正确，拆除邻格后新暴露面恢复所属地基材质；
- 跨区块相邻地基不生成重复渲染面，区块边缘不出现裂缝；
- Raycast 顶面和四个侧面均能恢复正确 `GridCell` 和地基所有者；
- 空区块删除、反复替换 Collider 和 World 销毁后不存在 Collider Blob 泄漏。

为阶段 4 提前固定但暂不开放给玩家的测试：两个垂直相邻的 `int3` 体素在同一物理高度分带
内可合并成一个 `1 × 2 × 1` Box，六方向表面提取会剔除它们之间的两个面。

性能报告除原有指标外增加：

- 每个区块的外露 Quad、三角形和 SubMesh 数；
- 每个物理区块的输入体素数、Greedy Box child 数和压缩率；
- Render Mesh 重建耗时、Compound Blob 创建耗时和旧 Blob 释放数；
- Physics 静态 Body 数、Collider Blob 持久内存和建造操作同步点耗时；
- 连续放置/拆除时的主线程尖峰、GC Alloc 和 Persistent Native 内存趋势。

## 阶段 3.5：地基接入现有建筑资源、UI 与统一命令系统

### 3.5.1 目标和架构边界

阶段 3 已完成地基的稀疏 Surface、区块 Mesh、Compound Collider 和专用事务逻辑，但地基
尚未成为现有建筑内容管线中的正式建造项。本阶段把地基接入建筑 CSV、纯表现 Prefab、图标
烘焙、建造目录、快捷选择和玩家命令入口，并把正式 ECS 场景从预铺 32×32 地面改为玩家
手动铺设地基。

本阶段采用以下固定分层：

```text
buildings.csv / building_levels.csv / 纯表现 Foundation Prefab
                          ↓
              名称、图标、菜单、材质绑定
                          ↓
             统一玩家建造命令与 GridBuildCommand
                    ├─ Place
                    ├─ PlaceBeltPath
                    └─ PlaceFoundationArea
                          ↓
          Surface Registry + 区块 Mesh/Compound Collider
```

地基在资源层和 UI 层兼容普通建筑，在执行层使用同一个 `GridBuildCommand` 中明确的地基
矩形命令分支。不得把地基伪装成普通 `Place`，也不得创建独立 Foundation Command Buffer、
Command Bus、结果通道或命令处理系统。

### 3.5.2 接入建筑数据表和纯表现 Prefab

正式增加：

- `BuildingKind.Foundation`；
- `FactoryBuildingBehavior.Foundation`；
- `buildings.csv` 中的 `foundation` 建筑类型；
- `building_levels.csv` 中的 `foundation_mk1` 建造项；
- `Assets/Prefabs/Buildings/Foundation.prefab` 纯表现 Prefab；
- 由现有建筑图标烘焙器生成的 Foundation 图标。

Foundation Prefab 继续遵守现有纯表现资源规则，只允许 `Transform`、`MeshFilter` 和
`MeshRenderer`。标准地基 Prefab 表示 `1 × 1 × 1` 立方体，使用单一视觉材质，不携带逻辑
Authoring、Collider 或运行时脚本。UI 名称必须显示为“地基”，不得把未解析的
`building.foundation.mk1` Key 直接显示给玩家。

Prefab 的职责是：

- 作为建筑等级的正式资源绑定；
- 为建造目录和快捷栏提供自动烘焙图标；
- 提供该地基等级使用的视觉材质；
- 必要时作为选中项或预览的美术来源。

Prefab 不作为逐格运行时 Renderer。地基放置成功后仍由阶段 3 的区块网格生成器提取外表面，
按 `VisualMaterialId` 生成 SubMesh，并由一个区块 Render Entity 提交。不得因为接入建筑表而
退回“每个地基实例化一个表现 Prefab”的路径。

建筑 Prefab Baker 对 Foundation 等级建立稳定的：

```text
BuildingLevelId -> VisualMaterialId -> Material
```

映射。`GridBuildCommandSystem` 从 `BuildingLevelId` 解析地基材质，不能信任 UI 任意填写的
`VisualMaterialId`。第一版 Foundation Prefab 只允许一个材质；以后增加地基类型或等级时，
每个等级仍可映射到不同材质 ID。

### 3.5.3 稳定 ID 和无端口建筑规则

当前建筑导入器按 Key 或菜单顺序重新生成 ID，直接插入 Foundation 会改变现有
`BuildingTypeId`/`BuildingLevelId`。本阶段必须先给 `buildings.csv` 和
`building_levels.csv` 增加显式稳定 `id` 列，将当前已生成 ID 固化，再为 Foundation 分配新
且未使用的 ID。导入器必须验证 ID 非零、唯一并保持 Blob 索引可查询；后续新增行不得改变
已有 ID。

Foundation 没有输入/输出端口、机器类型或行为专属等级统计。数据导入器应允许
`FactoryBuildingBehavior.Foundation` 使用空 `port_layout_key`，生成 `PortCount = 0`；不得
为满足旧校验而添加无意义的虚假端口。Foundation 也不得出现在 Belt、Processor 或 Storage
等级统计表中。

### 3.5.4 统一命令中的地基矩形事务

新增或最终定名为：

```text
GridBuildCommandType.PlaceFoundationArea
```

命令复用现有字段和基础设施：

```text
RequestId
Player
BuildingLevel
StartCell
EndCell
```

`StartCell` 和 `EndCell` 定义包含两端的轴对齐 X/Z 矩形；两者相同表示单格地基。阶段 3.5
仍只接受 `Level = 0`，且起点、终点必须在同一 Level。矩形与传送带路径的区别固定为：

```text
PlaceBeltPath       -> 起点到终点的两条正交线段
PlaceFoundationArea -> 起点、终点包围的完整矩形区域
```

命令必须按“先验证、后提交”的原子事务执行：

1. 解析 `BuildingLevel`，确认它是 Foundation 等级并取得 `VisualMaterialId`；
2. 计算规范化的 `minX/maxX/minZ/maxZ` 和总格数，检查尺寸与命令批次上限；
3. 遍历矩形内所有目标格；
4. 任意目标格已存在地基体素时，以 `FoundationAlreadyExists` 拒绝整个矩形；
5. 任意目标格不满足世界边界、层级或其他固定规则时，拒绝整个矩形；
6. 全部验证成功后，在同一事务内创建所有地基玩法实体并登记 Surface Registry；
7. 合并相同区块的 Revision 和 dirty 请求，避免每格重复入队；
8. 统一生成一个命令结果，`AffectedCount` 等于成功创建的地基格数；失败时为 0。

不得先放置空格、遇到已有地基后留下部分结果。命令在同一批次中继续遵守现有确定性顺序，
因此可以正确表达“先拆建筑、再拆地基”或“先铺地基、再建建筑”。

### 3.5.5 空白位置建造和普通建筑解锁

地基是 Surface 的来源，因此它的验证规则与普通建筑相反：

```text
普通建筑：目标格必须已有允许该类型的 Surface，且 Building Occupancy 为空
地基地块：目标格必须没有 Foundation Voxel；不要求目标格预先存在 Surface
```

阶段 3.5 的第一块以及后续地基允许放在 `Level = 0` 明确建造工作平面上的任意整数格，
不要求与已有地基相邻，也不要求命中旧地板 Collider。地基矩形成功提交并完成 Registry
登记后，同批次中后续普通建筑命令和后续帧玩家操作必须立刻能够在这些新 Surface 上通过
放置验证。

普通建筑仍不得建在没有地基的空白格。地基不写入 `BuildingOccupancyIndex`；它使用独立的
Foundation 所有者索引，所以“地基存在”和“地基顶面承载普通建筑”可以同时成立。

### 3.5.6 建造目录、快捷栏和矩形预览

`FactoryDatabaseBlob.BuildingLevelMenu` 应包含 Foundation 等级，因此现有
`UiSnapshotExportSystem`、`BuildCatalogView` 和 `FactoryPresentationCatalog` 可以继续通过
`BuildingLevelId` 显示其名称和图标。地基必须出现在完整建造目录中；快捷栏槽位不足时使用
明确、可测试的菜单顺序选择前九项，不得因为增加 Foundation 而产生越界或错误图标绑定。

选择 Foundation 后，玩家控制器进入地基矩形模式：

1. 第一次左键记录 `StartCell`；
2. 鼠标移动时填充显示起点与当前格包围的整个矩形；
3. 已有地基的任意格使整个预览显示为无效；
4. 第二次左键提交 `PlaceFoundationArea`；
5. `Escape` 取消当前矩形起点；
6. `R` 对轴对齐矩形没有意义，不改变地基预览；
7. 成功或失败后均通过现有命令结果通道反馈，不直接从 MonoBehaviour 修改 ECS。

地基预览不得复用普通建筑的 `HasSurface()` 前置条件；它应查询
`!HasFoundationVoxel(cell)`。射线继续使用阶段 3.5 明确的 `Y = 0` 建造工作平面，使玩家在
完全空白的位置也能得到整数 `GridCell`。阶段 4 再把常规拾取切换为实际多层地基 Collider。

### 3.5.7 正式 ECS 场景和旧矩形迁移

正式 `Ecs` 场景当前包含名为 `Factory Floor` 的 GameObject：单位 Cube、位置
`(16, -0.5, 16)`、缩放 `(32, 1, 32)`，并带有 `MeshRenderer` 和 `BoxCollider`。本阶段必须
删除该对象，不允许把它保留为隐藏的视觉地板、碰撞地板或建造支撑。

同时，正式 ECS 场景必须关闭阶段 3 的 `GridDefinition.Size -> 32×32 Surface` 自动兼容
初始化。进入正式场景后初始 Surface Registry 为空，玩家只能先手动放置地基，再在其上
建造普通建筑。

阶段 3 的旧矩形初始化能力保留为显式兼容选项，只允许旧存档迁移、专门测试场景或迁移工具
主动开启；不得再由所有 `GridDefinition` 隐式触发。建议在 Authoring/启动配置中使用明确的
`InitialSurfaceMode.Empty` 与 `InitialSurfaceMode.LegacyRectangle`，正式 `Ecs` 固定为
`Empty`。

### 3.5.8 性能场景兼容

现有性能场景在没有地基命令的情况下直接放置 Belt、Processor 和 Storage，关闭隐式矩形后
会全部失败。性能 Bootstrap 必须在提交普通建筑前，通过相同的
`PlaceFoundationArea` 命令创建覆盖场景布局的最小矩形或明确的目标 Surface 集合，并等待
地基结果成功后再提交普通建筑。

性能场景不得绕过 Registry 直接写入位图，也不得重新启用正式场景的隐藏 32×32 地板。
性能报告应区分地基初始化阶段和稳态采样阶段，避免把一次性 Mesh/Collider 重建计入运输
稳态指标；另设地基矩形建造场景记录批量事务、区块重建和 Blob 替换尖峰。

### 3.5.9 实施顺序

1. 固化建筑和建筑等级显式 ID，扩展 Foundation Kind/Behavior 和无端口导入规则；
2. 新增 Foundation 纯表现 Prefab、CSV 行、名称和自动烘焙图标；
3. 从 Foundation Prefab/Level 建立材质映射，替换独立且不可由建筑表寻址的材质入口；
4. 实现 `PlaceFoundationArea` 的矩形计算、批次上限、完整预验证和一次性提交；
5. 接入玩家命令适配、建造目录、快捷栏、两次点击状态和矩形预览；
6. 删除正式场景 `Factory Floor`，把正式初始 Surface 模式切换为 `Empty`；
7. 修改性能 Bootstrap，通过正式地基命令先铺设所需 Surface；
8. 更新 EditMode、PlayMode、场景内容和性能回归后，再进入阶段 4。

### 3.5.10 验收标准

- Foundation 是建筑 CSV 和建筑等级 CSV 中的正式、稳定 ID 条目；
- Foundation Prefab 只含表现组件，图标由现有建筑图标管线生成；
- 建造目录正确显示“地基”和对应图标，选中后进入矩形模式；
- 玩家能在原来没有地基的任意 `Level = 0` 工作平面位置选择起点和终点；
- `StartCell == EndCell` 成功放置一个地基；不同起终点填满包含边界的矩形；
- 矩形内任意格已有地基时，命令整体失败且 `AffectedCount == 0`；
- 成功建造地基后，普通建筑和传送带可在新 Surface 上建造；相邻空白格仍拒绝普通建筑；
- 地基不产生逐格 Render Entity、GameObject Collider 或独立 `PhysicsCollider`；
- 正式 `Ecs` 场景不再包含 `(16,-0.5,16)`、缩放 `(32,1,32)` 的 `Factory Floor`；
- 正式场景启动时 Surface Registry 为空，不隐式生成 32×32 地基；
- 性能场景通过正式地基命令准备 Surface，并继续达到既有确定性实体计数和稳态门槛；
- 同一命令系统内的混合批次、矩形原子回滚、UI 名称/图标、空白预览和场景内容均有自动化测试。

## 9. 阶段 4：多高度平面和实际表面拾取

### 9.1 目标

允许相同 X/Z 上存在不同整数 `Level` 的平面表面，并保持普通建筑完全二维占格。

### 9.2 放置验证

对普通建筑 footprint 的每个格子检查：

```text
SurfaceCell 存在
SurfaceCell 是 Flat
SurfaceCell 允许该建筑类型
所有占格与 Anchor.Level 相同
BuildingOccupancy 为空
```

不得因为 footprint 覆盖多个 X/Z 就自动跨层寻找“最近表面”。建筑只能使用明确选中的
Anchor Level。

### 9.3 输入拾取

替换当前唯一无限水平 Plane：

- 射线命中实际地基/区块 Collider；
- 命中信息解析为 `GridCell`；
- 同一 X/Z 多层时选择射线首先命中的实际表面；
- 从空地创建第一块地基时，使用地形命中或明确的建造工作平面；
- 提供当前选中层的 UI/调试显示，必要时支持层过滤。

不得每帧遍历所有 Grid 或所有 Level 分别做 Plane Raycast。

### 9.4 视觉和相机

- 放置预览使用 Anchor Level 计算 Y。
- 被上层遮挡时提供隐藏上层、切层或透明化入口；具体交互可后续迭代，但数据接口必须支持。
- Grid Debug View 只生成可见区块和选中层附近内容。

### 9.5 验收标准

- 同一 X/Z 的两层可以分别放置建筑且不发生 Occupancy 冲突。
- 普通建筑不能跨层占格。
- 射线可以稳定选中上下层表面，不依赖 Grid 创建顺序。
- 层高换算在正负 Level 上保持可逆并通过边界测试。

## 10. 阶段 5：运输拓扑三维地址和显式连接边

### 10.1 目标

先让现有平面传送带在多层环境中完全正确，再为坡道和垂直带提供统一连接接口。

### 10.2 平面运输迁移

- `BeltTopology.Cell`、`Merger.Cell`、`Splitter.Cell` 使用 `GridCell`。
- `Cell -> NodeIndex` 索引使用包含 Level 的键。
- 平面邻居只在相同 Level 上通过 X/Z 方向查找。
- 现有 L 形路径第一版限定在同一 Level；起点和终点层不同返回明确失败原因。
- 旧的确定性输入选择、回路检测和仲裁语义保持不变。

### 10.3 显式连接边

为特殊运输结构提供由拓扑构建阶段写入的连接描述。平面节点仍可自动推导相邻边，坡道和
垂直节点通过 Connector 注册边。Fixed Tick 只读取已经构建好的连续拓扑数据，不在每 Tick
查询 Surface Registry 或执行三维寻路。

建议将拓扑失效限制到：

- 变化节点所在 Transport Region；或
- 第一版先全局重建运输节点，但只由 `TransportTopologyRevision` 触发。

### 10.4 路径命令演进

当前 `StartCell + EndCell + HorizontalFirst` 可以继续服务同层 L 形路径。跨层路径不得把
坡道和垂直连接隐式编码成分数坐标。后续有两种可选接口：

1. 客户端提交明确的 Route Segment 列表，服务端逐段验证；
2. 服务端在平面邻接和 Connector Edge 组成的图上寻路。

第一版坡道和垂直带采用明确放置特殊段，不在本阶段实现自动跨层寻路。

### 10.5 验收标准

- 同一 X/Z 不同 Level 的传送节点不会在索引中覆盖。
- 多层平面运输网络互不串线。
- 平面直线、转弯、合流、分流、建筑端口和满环测试全部通过。
- 普通非运输建筑和地基变化不触发 Transport Topology 重建。

## 11. 阶段 6：坡度地基和坡道传送带

### 11.1 目标

实现连接不同整数层的坡道地基，初始允许斜率：

```text
1/1
1/2
1/4
```

坡道上禁止普通建筑，只允许坡道传送带。

### 11.2 放置参数与验证

坡道放置必须明确：

- 起点 `GridCell`；
- 水平轴向方向；
- Rise；
- Run；
- 上升或下降方向。

验证规则：

- 两端落在整数层；
- `Run/Rise` 属于允许的斜率集合；
- 水平投影不与不允许穿越的建筑体积冲突；
- 两端接口存在或满足新建规则；
- Ramp Connector Occupancy 没有冲突。

### 11.3 坡道槽和视觉

- 中间坡道位置使用 `RampSlot`，不注册为 `Flat SurfaceCell`。
- 普通建造工具不能选择 Ramp Slot。
- 传送带视觉 Y 按 `slotIndex / run` 插值。
- 坡道地基 Mesh 和 Collider 按 connector 生成，可按区块或实例合批。

### 11.4 运输接入

- 每个坡道槽可以对应一个运输节点，保持与现有每格传送带进度语义一致。
- 两端通过显式边连接对应平面节点。
- 运输方向、速度、反向放置和拆除必须确定性处理。
- 第一版不允许普通分流器、合流器位于坡道槽上。

### 11.5 验收标准

- 三种斜率上下行均可正确放置和拆除。
- 普通建筑无法放置在坡道上。
- 物品可以从平面进入坡道、通过坡道并进入目标层。
- 坡道中间高度不需要成为全局分数格坐标。
- 拆除坡道或任一坡道带后拓扑无悬空边和物品丢失。

## 12. 阶段 7：垂直传送带

### 12.1 目标

实现相同 X/Z 上整数高度层之间的直接运输，并允许垂直段穿过中间地基。

### 12.2 空间占用

垂直带使用独立柱状区间索引：

```text
Column = (X, Z)
Interval = [BottomLevel, TopLevel]
```

规则：

- 中间地基表面不构成冲突；
- 起点和终点接口必须满足传送方向规则；
- 两条不允许重叠的垂直带进行区间相交检查；
- 是否允许穿过普通建筑体积由建筑碰撞阶段另行定义，第一版至少禁止与明确占用竖井的建筑冲突。

### 12.3 运输和物品表现

- 垂直带向运输拓扑注册上下端显式边。
- 逻辑进度仍在 Fixed Tick 推进。
- 物品表现根据 Bottom/Top 世界位置插值，不改变逻辑地址。
- 拆除时明确处理带内物品：拒绝拆除、返还或销毁必须采用项目统一规则，不得静默丢失。

### 12.4 验收标准

- 可跨一个或多个整数层运输。
- 中间层地基不阻止垂直带。
- 同柱冲突检测稳定，边界相接不被误判为重叠。
- 上行、下行、阻塞、满载和与平面带交接测试通过。

## 13. 阶段 8：规模化、连通缓存和旧系统退役

### 13.1 可选连通区域

只有出现实际消费者后才实现：

- 新增地基通过四邻接做增量合并；
- 删除地基将受影响 Region 标记 dirty；
- 区块级图先判断大范围连通，局部格子 flood fill 精化；
- 大区域分裂允许分帧预算；
- Region 重算期间消费者获得明确的 Pending 状态。

### 13.2 区块流式和存档

- 存档以全局 `GridCell`、Chunk Key 和独立 Building Runtime ID 为稳定地址。
- 不保存临时 Region ID、NodeIndex、HashMap bucket 或 Entity Index。
- 保存地基提供的表面、普通建筑、Ramp Connector 和 Vertical Connector。
- 加载顺序为：World Config → Surface Chunks/Foundation → Building Occupancy → Transport Nodes → Topology Rebuild。
- 远处区块卸载前处理跨区块运输边和带内物品状态。

### 13.3 旧代码退役

完成所有场景迁移后删除：

- 运行时矩形 `GridDefinition.Size` 边界判定；
- “必须恰好一个可建造矩形 GridDefinition”的错误路径；
- `int2` 世界格地址重载；
- 从格子坐标派生永久容器 ID 的兼容逻辑；
- 依赖全局 Grid Revision 的 Transform 和 Transport 失效；
- 旧的无限单平面拾取路径。

### 13.4 验收标准

- 旧场景和新多层场景都通过自动化测试。
- 运行时不存在双写的旧/新空间状态。
- 存档加载后重建的拓扑与保存前逻辑等价。
- 大规模地基、建筑和运输压力场景满足第 16 节性能门槛。

## 14. 系统改动清单

### 14.1 数据层

重点文件：

- `Assets/Scripts/ECS/Data/EcsGridComponents.cs`
- `Assets/Scripts/ECS/Data/EcsBuildingComponents.cs`
- `Assets/Scripts/ECS/Data/EcsFactoryComponents.cs`
- `Assets/Scripts/ECS/Data/EcsPlayerCommandComponents.cs`
- `Assets/Scripts/ECS/Data/EcsPlayerComponents.cs`

主要工作：引入 `GridCell`、拆分 Revision、增加 Surface/Connector 数据，并保持建筑局部二维偏移。

### 14.2 Authoring 和 Baking

重点文件：

- `Assets/Scripts/ECS/Authoring/GridDefinitionAuthoring.cs`

主要工作：从烘焙唯一矩形网格改为烘焙 `WorldGridConfig` 和初始 Surface Chunk。旧矩形参数在
迁移期只作为生成初始完整表面的 Authoring 输入。

### 14.3 建造与索引

重点文件：

- `Assets/Scripts/ECS/Systems/GridOccupancyIndexSystem.cs`
- `Assets/Scripts/ECS/Systems/GridBuildCommandSystem.cs`
- `Assets/Scripts/ECS/Systems/PlayerGridCommandAdapterSystem.cs`
- `Assets/Scripts/ECS/Systems/GridPlacementTransformSystem.cs`

主要工作：三维地址、持久索引、在现有 `GridBuildCommand` 中增加地基命令类型、事务式表面
修改、局部脏标记和 Transform 增量更新；不得建立独立地基命令管线。

### 14.4 输入和表现

重点文件：

- `Assets/Scripts/ECS/Presentation/EcsGridInteractionController.cs`
- `Assets/Scripts/Prototype/Presentation/GridDebugView.cs` 及后续正式 ECS 替代实现
- `Assets/Scripts/ECS/Systems/BeltTopologyVisualSystem.cs`

主要工作：实际表面拾取、多层预览、区块网格显示、坡道/垂直预览和局部视觉刷新。

### 14.5 运输

重点文件：

- `Assets/Scripts/ECS/Systems/BeltTransferSystem.cs`
- `Assets/Scripts/ECS/Systems/LinearBeltTransferResolver.cs`
- `Assets/Scripts/ECS/Systems/BeltTopologyVisualSystem.cs`

主要工作：三维节点地址、独立 Transport Revision、显式 Connector Edge、局部或受控全局重建。

### 14.6 测试和性能

重点目录：

- `Assets/Tests`
- `Assets/Scripts/ECS/Performance`
- `Assets/Scenes/Performance`
- `PerformanceReports`

不得把正式实现放入 `Assets/Scripts/Prototype`。Prototype 只能作为行为参考，不能成为新空间
系统的状态来源。

## 15. 自动化测试矩阵

### 15.1 坐标测试

- X/Z/Level 正负边界；
- World ↔ Cell 往返；
- 区块边界 `15/16`、`-1/0`、`-16/-17`；
- 不同 LayerHeight；
- 大坐标哈希稳定性。

### 15.2 表面和占用测试

- 单地基、相邻地基、凹形、环形、孤岛；
- 跨区块 footprint；
- 同 X/Z 不同 Level；
- 缺一格表面时多格建筑整体失败；
- 地基与上方建筑使用不同占用通道；
- 有建筑时拒绝拆除承载地基；
- 批次内多个命令的冲突和回滚。

### 15.3 输入测试

- 射线首先命中上层；
- 上层缺口可以命中下层；
- 地基侧面不会错误解析为顶面格；
- 从地形创建首块地基；
- 层过滤和预览高度。

### 15.4 运输测试

- 同层直线和 L 形；
- 不同层相同 X/Z 不串线；
- 坡道三种斜率、上下行、阻塞和拆除；
- 垂直带上下行、跨多层和穿地基；
- 平面—坡道—平面组合；
- 平面—垂直—平面组合；
- 跨区块和跨层满环；
- Merger/Splitter round-robin 确定性；
- 相同输入命令在不同 Job 调度顺序下结果一致。

### 15.5 存档测试

- 旧单层矩形场景迁移；
- 多层建筑 ID 稳定；
- 区块卸载/加载；
- Connector 拓扑重建；
- 保存前后带内物品和处理器状态一致。

## 16. 性能验证方案

阶段 0 完成后根据实测基线确认最终数值。以下为初始门槛：

1. 旧 32×32 场景在阶段 1 后 Fixed Tick 中位数回归不超过 5%。
2. 稳态 Fixed Tick 不产生持续托管 GC 分配。
3. 单个普通建筑放置检查不扫描远处无关建筑。
4. 单块地基修改只使有限区块进入 dirty 队列。
5. 非运输建筑和地基变化不重建运输拓扑。
6. 单个运输节点变化的工作量不得与地基总格数相关。
7. 相同 X/Z 面积下增加空高度层不应增加稳态模拟成本。
8. Surface 数据内存应与实际存在的区块/格子相关，而不是与世界包围盒体积相关。

压力场景至少覆盖：

| 场景 | 目的 |
|---|---|
| 单层密集地基 | 测试 Surface 位图、Mesh 和 Collider 重建 |
| 大范围稀疏岛屿 | 验证内存不随包围盒膨胀 |
| 同 X/Z 多层堆叠 | 验证多层索引和拾取 |
| 跨区块长传送带 | 验证边界拓扑和局部失效 |
| 大量短独立运输网络 | 验证 Transport Region 管理 |
| 坡道/垂直混合网络 | 验证特殊边和物品插值 |
| 删除桥接地基 | 测试最坏连通分裂尖峰 |

报告除帧时间外还要记录：

- Occupied Cell 数；
- Surface Chunk 数和实际表面格数；
- Transport Node/Edge 数；
- 每次操作触碰的 Chunk 数；
- Topology Rebuild 节点数和次数；
- Persistent Native 内存；
- Mesh/Collider 重建次数；
- 主线程等待 Job 的时间。

## 17. 迁移纪律与提交边界

每个阶段应拆成可审查的提交，推荐顺序：

```text
测试和诊断
→ 数据类型
→ 索引/系统
→ 输入和表现
→ 场景与 Authoring
→ 性能报告
→ 删除兼容路径
```

执行规则：

- 一个提交不得同时引入新空间地址、坡道玩法和存档格式三类变化。
- 先迁移消费者并通过测试，再删除旧字段。
- 临时兼容字段必须标注删除阶段。
- 不把 Entity Index、Region ID 或 Transport NodeIndex 当作永久 ID。
- 所有 Native 容器明确创建、Dependency 完成和 Dispose 生命周期。
- Fixed Tick 中禁止引入托管 Dictionary/List 分配。
- HashMap 枚举顺序不得影响命令结果和运输仲裁。
- 区块尺寸、层高和允许坡度由统一配置定义。

## 18. 需要在实施前确认的玩法决策

以下问题不阻塞阶段 0～2，但必须在对应功能阶段开始前定稿：

1. 地基是否需要结构支撑，还是允许悬空扩展。
2. 拆除承载建筑的地基是拒绝还是显式级联拆除；当前方案默认拒绝。
3. 坡道是否允许跨越普通建筑体积，以及其碰撞包围盒规则。
4. 坡道传送带是否按每个 Ramp Slot 收费和拆除。
5. 垂直带内有物品时的拆除规则。
6. 玩家如何在被上层遮挡时选择下层：层过滤、隐藏上层或相机模式。
7. 自动传送带路径是否需要跨层寻路；第一版默认手动放置 Connector。
8. 是否有实际玩法依赖地基连通 Region；没有消费者时不实现。

## 19. 完成定义

本轮多层稀疏网格改造在满足以下条件时完成：

- 固定世界使用唯一全局整数 `GridCell` 坐标规则；
- 可建造表面由稀疏区块表达，不依赖完整矩形边界；
- 普通建筑保持二维 footprint，并可在任意整数 Level 放置；
- 地基、普通建筑、坡道和垂直连接使用独立占用通道；
- 平面、坡道和垂直运输统一进入确定性运输拓扑；
- 不存在地基相邻时合并 Grid、拆除时重写全部建筑 GridId 的流程；
- 建造和表现更新局限于候选格、相关节点和受影响区块；
- 旧单层场景行为、自动化回归、存档迁移和性能门槛全部通过；
- 旧的单矩形、单平面、`int2` 世界地址兼容代码已经退役。
