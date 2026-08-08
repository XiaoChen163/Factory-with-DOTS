# UI Toolkit UI 系统与玩家命令通道实现文档

## 1. 文档目的

本文定义 Factory-with-DOTS 正式运行时的 UI、玩家交互、物品拖放和命令通道实现方案。
方案基于当前项目实际版本和代码，而不是另起一套原型：

- Unity `6000.3.19f1`
- Entities `1.4.8`
- Input System `1.19.0`
- 正式运行时程序集 `Factory.Runtime`
- ECS 是玩法状态唯一来源
- 玩家控制器、窗口编排和输入模式继续使用 OOP + `MonoBehaviour`
- 固定模拟频率保持 `60 Tick/s`

本方案覆盖：生产建筑、存储建筑、玩家背包、建筑列表、跨窗口拖放、配方选择、
建造模式、多人命令身份、图标数据、扩展新窗口和性能约束。

## 2. 结论摘要

采用以下技术组合：

| 层 | 技术与职责 |
|---|---|
| UI 视图 | UI Toolkit Runtime，UXML 定义结构，USS 定义样式，一个 `UIDocument` 承载所有游戏 UI |
| 窗口层 | OOP `UiWindowManager`，管理左、右、浮层三个窗口区域，不保存权威玩法状态 |
| 展示模型 | 普通 C# ViewModel/Snapshot；窗口只绑定快照，不直接访问 ECS |
| ECS 读取桥 | `UiSnapshotExportSystem` 在 `PresentationSystemGroup` 中只导出已订阅玩家/建筑的数据 |
| 玩家输入 | `TopDownPlayerController`、`PlayerInteractionController`、`PlayerBuildController` 均为 `MonoBehaviour`，使用 Input System Action Map |
| 命令入口 | 统一 `PlayerCommandBus`，所有 UI 和玩家控制器操作都带 `PlayerId + RequestId` |
| 权威执行 | 配方选择、物品移动、建造/拆除由 Fixed Step ECS 系统验证并原子提交 |
| 静态数据 | 现有 CSV + `FactoryDatabaseAsset`/Blob；图标引用保留在托管表现资产，不进入 Blob |

核心边界如下：

```text
UI Toolkit Pointer/Input events
        │
        ▼
View / DragManipulator / MonoBehaviour controllers
        │  只提交意图
        ▼
PlayerCommandBus ──> PlayerCommandMailbox
        │
        ▼
PlayerCommandIngressSystem
        │
        ├──> RecipeSelectionCommandSystem
        ├──> ManualItemTransferSystem
        └──> GridBuildCommandSystem
                  │
                  ▼
             ECS 权威状态
                  │
                  ▼
UiSnapshotExportSystem ──> UiDataHub ──> ViewModel ──> UI Toolkit
```

禁止从 View、拖放处理器或 `MonoBehaviour.Update` 直接修改建筑 Buffer、背包 Buffer、
配方状态或 Grid 状态。

## 3. 现状审查与必须先解决的问题

### 3.1 可以直接复用的部分

- `FactoryDatabaseBlob` 已包含物品、建筑、建筑等级、配方、配方输入输出和建造菜单。
- `BuildingLevelMenu` 已按 `menu_order` 排序，可直接作为建筑列表的数据来源。
- `GridBuildCommand` / `GridBuildResult` 已形成建造意图与执行结果通道。
- `GridOccupancyIndexSystem` 已能由格子定位建筑 Entity，可用于玩家点击建筑。
- `ItemProcessState` 已包含加工状态、耗时和进度所需字段。
- `StorageState`、`StoredItemCount` 已有基础存储状态。
- 已有 `PanelSettings.asset`、背包 UXML/USS、槽位模板和 HUD UXML/USS，可作为视觉起点。

### 3.2 当前实现与需求的冲突

| 当前情况 | 冲突 | 处理 |
|---|---|---|
| 新加工建筑的 `SelectedRecipeIndex = 0` | 建筑从未处于“未选择配方”状态 | 初始值改为 `-1`，未选择时显示配方页 |
| CSV/Blob 支持多输入、多输出 | `ItemProcessSystem` 只读取第一个输入和第一个输出 | UI 接入前将加工缓存改为多槽模型 |
| `PendingOutputCount` 是单一整数 | 无法表示多种产物 | 改为输出槽 Buffer，每种产物独立计数 |
| `ItemInputPortSnapshot` 只表达一个精确物品 | 单物理输入口不能正确表示多个配方输入 | 扩展为按 `PortIndex + ItemId` 的容量快照或允许列表 |
| 仓库使用按物品聚合的 `StoredItemCount` | 无法稳定表示网格槽位和拖放目标 | 迁移为通用固定槽位 Buffer，或增加稳定槽位适配层；推荐迁移 |
| 尚无玩家背包 ECS 状态 | UI 无权威数据源 | 创建 Player Entity 和玩家背包组件 |
| `EcsGridInteractionController` 同时处理选择、预览、建造和 IMGUI | 难与建筑列表、窗口输入优先级协作 | 拆为交互控制器和建造控制器，移除正式运行时 IMGUI |
| `TopDownPlayerController` 使用旧 `UnityEngine.Input` | UI/Gameplay/Build 输入难分层 | 保持 MonoBehaviour，改用现有 Input System Action Asset |
| `InventoryView.uxml` 是全屏遮罩且窗口居中 | 无法实现左建筑、右背包同时打开 | 保留槽位视觉语言，重做根布局为左右 Dock |
| 性能场景只建造 Miner/Furnace，不显式选配方 | 默认配方改为 `-1` 后，生产场景中的加工建筑全部 Idle | 同步修改场景定义和 Bootstrap，显式为测试建筑选择配方 |

多输入/多输出不是纯 UI 问题。如果只新增界面而不修改加工状态，界面会展示无法由模拟
正确消费或产出的槽位，因此该改造是功能前置条件。

### 3.3 测试与性能场景影响

修改建筑配方逻辑会影响所有隐含“建成后自动使用第一条配方”的测试代码。当前直接受影响的是：

- `FactoryPerformanceScenarioLayout.cs` 的 `CreateProducerConsumer()` 会生成 Miner、Furnace 和 Storage；
- `FactoryPerformanceScenarioBootstrap.cs` 只提交 `GridBuildCommand`，建造完成后没有显式选择配方；
- `Perf_ProducerConsumer` 没有初始 Item，完全依赖 Miner 持续生产、Furnace 持续加工来形成吞吐。

因此 `SelectedRecipeIndex` 默认改为 `-1` 后，如果不同步修改测试场景，`Perf_ProducerConsumer`
虽然仍能通过建筑数量检查并进入 READY，但所有加工建筑都会保持 Idle，Item 数量和传输吞吐会降为 0，
得到的性能报告不再测试“生产—消费链”。这属于测试失真，不能只修改断言绕过。

同步修改方案：

1. 给 `FactoryPerformancePlacement` 增加可选 `RecipeKey`，Miner 使用 `mine_iron`，Furnace 使用
   `smelt_iron`；不要保存 `SelectedRecipeIndex = 0`，也不要依赖按字母排序生成的运行时索引；
2. `FactoryPerformanceScenarioBootstrap` 在建筑全部创建后，按 `GridPlacement.AnchorCell` 找到对应
   Processor Entity，通过数据库把 RecipeKey 解析成 `RecipeId`，再以明确的测试 `PlayerId` 和场景
   初始化权限提交正式配方选择命令；不要直接改回默认索引 0；
3. Bootstrap 必须等待所有配方选择结果成功，并确认 Processor 的 Selected Recipe 正确后，才能
   设置 `READY` 和开始采样；任一配方不存在或选择失败都应使场景 Setup 失败；
4. `PerformanceScenarioLayoutTests` 增加断言：每条 ProducerConsumer Lane 的 Miner/Furnace 都携带
   正确 RecipeKey，Storage 和传送带不携带配方；
5. 以后在 `FactoryWorldFixture` 增加 Processor 创建 Helper 时，必须把“未选择配方”或明确
   `RecipeId` 作为必填意图，禁止 Helper 默认为第 0 条配方；直接构造 `ItemProcessState` 的测试也要
   显式填写 `SelectedRecipeIndex`；
6. 修改完成后重新运行 `Perf_ProducerConsumer`，验收 `acceptedTransferCount > 0`、Storage 有输入，
   并与历史生产—消费基线对比。仅校验 Processor/Storage Entity 数量不足以证明场景有效。

当前其余性能场景只测试传送网络或建造压力，`FactoryBuildStressDriver` 只生成传送带，不受默认配方
变化直接影响。但所有未来包含 Processor 的场景都必须在场景定义中显式声明配方。

## 4. 总体分层

### 4.1 ECS 权威层

负责：

- 玩家背包、仓库存储、加工输入/输出和配方状态；
- 物品移动合法性、容量、槽位过滤、权限和距离校验；
- 加工、建造、拆除的实际执行；
- 为 UI 可见状态维护低成本 Revision；
- 为每条玩家命令返回可关联的结果。

该层不得引用 `VisualElement`、`Sprite`、`UIDocument` 或 ViewModel。

### 4.2 Presentation Bridge 层

负责将少量、已订阅的 ECS 状态转成托管只读快照。它是所有窗口统一获取动态数据的入口。

原则：

- 关闭的窗口没有订阅，不读取对应动态 Buffer；
- 只观察当前本地玩家和当前打开的建筑，不扫描所有玩家或所有建筑；
- 背包/容器槽位只在 Revision 变化时复制；
- 加工进度每渲染帧只读取当前建筑的一个 `ItemProcessState`；
- 静态目录只初始化一次；
- 不在 Fixed Tick 中创建托管对象或调用 UI。

### 4.3 Application 层

由普通 C# 对象组成：

- `UiDataHub`：数据源注册、订阅和快照分发；
- `PlayerCommandBus`：统一命令提交与结果关联；
- `UiWindowManager`：窗口注册、打开、关闭和 Dock；
- ViewModel：把 ECS 快照转成适合 UI 的文本、图标和槽位状态；
- `ItemDragController`：只维护一次拖拽的临时表现状态。

Application 层可以持有运行期快照，但不得成为玩法状态的第二份真相。

### 4.4 UI View 层

负责 UXML 克隆、元素缓存、样式类切换、Pointer 事件和渲染。View 只做两件事：

1. 根据 ViewModel 更新显示；
2. 把用户操作转换成 Application 命令。

## 5. UI Toolkit 结构

### 5.1 单 `UIDocument`

正式场景只创建一个 `GameUiRoot` GameObject：

- `UIDocument`
- `GameUiController`
- `PanelSettings` 引用现有 `Assets/Art/UI/PanelSettings.asset`
- 根 UXML 为 `GameUiRoot.uxml`

使用一个文档可以共享焦点、拖拽浮层、样式和输入优先级，也避免多个 Panel 的排序与事件穿透问题。

根视觉树：

```text
game-ui-root                    picking-mode=Ignore
├── hud-layer                   picking-mode=Ignore
├── window-layer               picking-mode=Ignore
│   ├── left-dock               建筑信息
│   ├── center-overlay          配方弹层、提示、确认框
│   └── right-dock              玩家背包
├── build-catalog-dock          建筑列表
├── drag-layer                  拖拽图标，picking-mode=Ignore
└── notification-layer         命令失败、容量不足等反馈
```

`window-layer` 自身忽略 Picking，真正的窗口面板使用 `PickingMode.Position`。因此空白区域仍可操作世界，
窗口内部会阻止世界点击。

### 5.2 窗口规则

`UiWindowManager` 按 `UiWindowId` 注册窗口，每个窗口声明一个 `UiDockRegion`：

```csharp
public enum UiDockRegion : byte
{
    Left,
    Right,
    Bottom,
    Overlay
}
```

首批窗口：

| Window | Dock | 打开方式 | 可共存 |
|---|---|---|---|
| `BuildingWindow` | Left | 与生产/存储建筑交互 | 可与 Backpack 共存 |
| `BackpackWindow` | Right | Tab 或打开建筑时按玩家设置自动打开 | 可与 Building 共存 |
| `BuildCatalogWindow` | Bottom/左下 | 建造热键 | 默认关闭 Building，可与 Backpack 共存 |
| `RecipePickerOverlay` | Building 内部页面，不单独占 Dock | 建筑未选配方或点击更换配方 | 不关闭 Backpack |

窗口关闭只取消数据订阅并设置 `display: none`；常用窗口不反复销毁视觉树。

当只开一个窗口时，它仍留在自己的 Dock：建筑在左，背包在右。这样同时打开时不会跳位，
也不会让拖拽目标在操作中移动。

### 5.3 可复用控件

建立以下自定义 `VisualElement`/控制器：

- `ItemSlotView`：图标、数量、虚影、禁用态、等待确认态；
- `ItemSlotGridView`：固定列数的槽位池，不为每次刷新重新 Clone；
- `RecipeCardView`：配方图标、名称、输入、输出、耗时；
- `ProgressArrowView`：箭头底图 + 填充遮罩，接收 `0..1`；
- `WindowFrameView`：标题、关闭按钮、内容根；
- `BuildOptionView`：建筑图标、名称、等级和选中态；
- `ItemDragManipulator`：Pointer 捕获与拖放生命周期。

背包和仓库应使用槽位池而不是每次 `Clear()` 后重新 Clone。建筑列表、配方列表数量可能增长，
可使用 `ListView` 虚拟化；如果最终表现是多列网格，则使用与背包相同的池化 Grid。

### 5.4 现有 UI 资产的处理

- 复用 `InventoryStyle.uss` 的颜色、边框和槽位风格；
- 复用 `InventorySlot.uxml`，增加 ghost、pending 和 invalid-drop 三种状态；
- 将 `InventoryView.uxml` 的全屏半透明根背景移到可选模态层，窗口本身改为右 Dock；
- `GameplayHud.uxml` 改为从 Input Action 显示当前绑定，不在 UXML 中硬编码错误的键位说明；
- 新 UXML/USS 使用序列化 `VisualTreeAsset` 引用，不依赖运行时字符串 `Resources.Load`。

## 6. 各窗口详细行为

### 6.1 生产建筑窗口

窗口标题显示建筑名称、等级和图标。内容有两个互斥页面。

#### 未选择配方

当 `SelectedRecipeIndex == -1` 时显示 `RecipePickerView`：

- 数据来自建筑 `MachineType` 对应的 `RecipeRangesByMachine`；
- 每张卡显示输入、输出、基础耗时和图标；
- 选择后提交 `SelectRecipePlayerCommand`；
- 收到成功结果前保持当前页面，并给所选卡增加 pending 状态；
- 失败时保持页面并显示原因。

#### 已选择配方

显示三栏加工布局：

```text
┌─────────────┬──────────────────┬─────────────┐
│ 输入原料槽   │    加工进度箭头    │  输出产物槽   │
│ Iron Ore ×2 │      63%         │ Ingot ×1   │
│ ...         │ Processing/Blocked│ ...         │
└─────────────┴──────────────────┴─────────────┘
```

- 左栏按配方输入顺序一物品一种槽；
- 右栏按配方输出顺序一物品一种槽；
- 空槽仍显示对应物品的半透明图标，即“虚影”；
- 实际物品图标完全不透明，右下角显示当前数量；
- 输入槽只接受 `AcceptedItemId`；
- 输出槽拒绝玩家放入，只允许玩家拿出；
- 箭头显示 `ElapsedTicks / DurationTicks`；
- 状态文字区分 Idle、Processing、Completed、OutputBlocked；
- 提供“更换配方”按钮，但只有 Idle 且所有输入/输出槽为空时允许切换；其余情况由 ECS 拒绝，
  不由 UI 自行判定为成功。

如果要支持“切换配方时自动退回物品”，应作为独立的事务命令实现，第一版不隐式搬动物品。

### 6.2 存储建筑窗口

- 固定列数网格显示全部槽位；
- 空槽可作为拖放目标；
- 同类物品优先堆叠，再使用空槽；
- 标题显示 `已用数量/总容量` 和 `已用槽位/总槽位`；
- 从仓库拖到背包表示主动拿出；
- 从背包拖到仓库表示主动放入；
- 自动运输和玩家拖放都更新同一份 ECS 容器状态与 Revision。

### 6.3 玩家背包窗口

- 数据来自当前 `PlayerId` 对应的 Player Entity；
- 网格固定显示所有背包槽；
- 可以单独打开；
- 建筑窗口关闭后仍可保持打开；
- 第一版拖拽默认移动整个堆叠；数量拆分作为后续命令参数扩展，不改变总体架构。

### 6.4 建筑列表

- 数据直接来自 `FactoryDatabaseBlob.BuildingLevelMenu`；
- 用 `FactoryBuildingLevelBlob.MenuOrder` 保持顺序；
- 每个选项使用建筑等级图标和本地化 `NameKey`；
- 点击后调用统一命令总线中的本地 `EnterBuildModeCommand`；
- `PlayerBuildController` 保存当前“工具状态”，但不保存已建建筑状态；
- 随后的格子点击提交带 `PlayerId` 的 `PlaceBuildingPlayerCommand`；
- ECS 校验成功后才算建造完成，失败通过命令结果提示；
- Escape 取消建造模式，R 旋转，传送带路径规则继续复用现有实现。

## 7. 物品与容器 ECS 数据模型

### 7.1 玩家身份

新增：

```csharp
public struct PlayerId : IEquatable<PlayerId>
{
    public ulong Value;
}

public struct PlayerIdentity : IComponentData
{
    public PlayerId Value;
}

public struct PlayerInventory : IComponentData
{
    public ushort SlotCount;
    public uint Revision;
}
```

本地 `PlayerContext : MonoBehaviour` 只保存/解析当前 `PlayerId`，不保存背包内容。
将来本地多人或联网玩家各自拥有独立 Player Entity。

### 7.2 通用容器槽

推荐把玩家背包和仓库统一为：

```csharp
public struct InventorySlot : IBufferElementData
{
    public ItemId ItemType;
    public ushort Count;
}
```

Buffer 长度固定等于 `SlotCount`，空槽为 `ItemId.Invalid + Count = 0`。禁止删除空元素，
从而保证拖放时 `SlotIndex` 稳定。

仓库增加 `StorageInventory` 或复用带类型标记的 `InventoryContainer`。当前
`storage_capacity` 表示总物品容量，建议在 `storage_level_stats.csv` 新增 `slot_count`：

- `storage_capacity`：容器允许的总物品数，保持现有语义；
- `slot_count`：UI 和堆叠使用的槽位数；
- 单槽上限仍取物品 `max_stack`。

这样不会静默改变现有数据库字段含义。迁移完成后删除或只读兼容
`StoredItemCount`，不要长期维护聚合计数与槽位两套真相。

### 7.3 加工槽

加工建筑使用专用 Buffer，因为输入过滤和输出权限与普通容器不同：

```csharp
public enum ProcessorSlotKind : byte
{
    Input,
    Output
}

public struct ProcessorItemSlot : IBufferElementData
{
    public ItemId AcceptedItemType;
    public ushort Count;
    public ushort Capacity;
    public ushort RequiredOrProducedCount;
    public byte RecipeSlotIndex;
    public ProcessorSlotKind Kind;
}
```

选中配方后，由命令系统按配方顺序一次性重建空槽。加工系统的行为改为：

1. Idle 时检查全部输入槽数量；
2. 同时检查全部输出槽能否容纳本次产物；
3. 条件全部满足后，原子扣除全部输入并进入 Processing；
4. 完成时原子写入全部输出槽；
5. 自动运输或玩家拿走输出后更新槽位数量；
6. 每次物品数量或配方变化推进 `ProcessorInventoryRevision`。

严禁逐输入扣除后才发现后续输入不足，否则会丢物品。

### 7.4 自动端口兼容

多原料配方要求输入端口按物品分别表达剩余容量。推荐将当前单一
`AcceptedItemType + FreeCapacity` 扩展成 Buffer 项：

```csharp
public struct ItemInputPortCapacity
{
    public ItemId ItemType;
    public int FreeCapacity;
    public byte PortIndex;
    public byte Enabled;
}
```

同一个物理 `PortIndex` 可以发布多条不同 `ItemId` 的容量记录。运输仲裁仍以 ItemId 查匹配项，
无需在 Fixed Tick 中建立托管集合。该变化需要补充多输入运输回归测试。

手动拖放与自动运输必须共享同一个 reservation 边界。当前 `ItemPortAdapterSystem` 同时负责
应用上一 Tick 的 Receipt 和发布下一代端口快照，实施时应拆成明确的两个阶段：

```text
ItemPortReceiptApplySystem              应用上一 Tick 的自动运输结果
  -> RecipeSelectionCommandSystem       处理配方命令
  -> ManualItemTransferSystem           原子执行玩家物品命令
  -> ItemProcessSystem                  推进加工
  -> ItemPortSnapshotPublishSystem      从最新槽位发布端口容量/可用量
  -> BeltProgressSystem
  -> BeltTransferSystem                 自动运输仲裁并产生下一批 Receipt
  -> ItemPortBufferSwapSystem
```

`ItemPortSnapshotPublishSystem` 必须保证 `BeltTransferSystem` 本 Tick 读取的有效快照已经包含
手动命令造成的数量变化，同时保留现有双缓冲无全量复制的设计。不得让手动系统绕过 reservation
单独扣减输出或占用输入容量。若保留现有 Buffer 世代含义，则命令提交后必须刷新本 Tick 的有效
读快照及下一代快照；更推荐在这次拆分中把“本 Tick 发布哪一代、运输读取哪一代”定义成单一明确规则。

## 8. 统一数据获取

### 8.1 查询与快照接口

窗口通过 `UiDataHub` 订阅，而不是查找 GameObject 或直接持有 `EntityManager`：

```csharp
public interface IUiDataSource<TKey, TSnapshot>
{
    IDisposable Subscribe(TKey key, Action<TSnapshot> onChanged);
    bool TryGetLatest(TKey key, out TSnapshot snapshot);
}
```

首批 Provider：

- `PlayerInventoryDataSource : IUiDataSource<PlayerId, InventorySnapshot>`
- `BuildingDataSource : IUiDataSource<BuildingRuntimeId, BuildingSnapshot>`
- `BuildCatalogDataSource : IUiDataSource<Unit, BuildCatalogSnapshot>`
- `CommandResultDataSource : IUiDataSource<PlayerId, CommandResultSnapshot>`

新增窗口时只需声明需要的 Snapshot 并订阅 Provider；窗口不需要复制 World/Query 缓存代码。

### 8.2 Snapshot 内容

Snapshot 使用 UI 友好的稳定 ID，不把原始 `Entity` 暴露给 View：

```text
InventorySnapshot
  PlayerId / Revision / SlotCount / IReadOnlyList<ItemSlotSnapshot>

BuildingSnapshot
  BuildingRuntimeId / BuildingLevelId / BuildingKind / NameKey / IconKey
  ProcessorSnapshot? / StorageSnapshot?

ProcessorSnapshot
  SelectedRecipeId / Status / Progress01 / InventoryRevision
  Inputs[] / Outputs[] / AvailableRecipes[]

ItemSlotSnapshot
  SlotIndex / ItemId / Count / Capacity / AcceptedItemId / SlotAccess
```

`BuildingRuntimeId` 是运行期稳定身份。UI 与未来网络命令不使用 `Entity.Index/Version` 作为协议 ID。

### 8.3 导出策略

`UiSnapshotExportSystem` 放在 `PresentationSystemGroup`：

- 从 `UiObservationRegistry` 获取当前订阅键；
- 只为订阅键解析 Entity；
- Revision 未变化时复用上一次槽位快照；
- 进度仅更新一个 float 和状态枚举；
- 把新快照发布到 `UiDataHub`；
- World 销毁、Entity 删除或建筑拆除时发布 `Unavailable`，窗口自动关闭。

不建议给整个游戏状态做每帧通用 runtime binding。Unity 6 runtime binding 可用于 Window 内少量
标量 ViewModel，但集合仍由受控的槽位池刷新。若使用 runtime binding，ViewModel 属性使用
`[CreateProperty]` 和变化通知，避免首次反射和无条件每帧刷新。

## 9. 统一玩家命令

### 9.1 命令头

所有会改变权威玩法状态的命令必须携带：

```csharp
public struct PlayerCommandHeader
{
    public PlayerId PlayerId;
    public uint RequestId;
    public uint ClientSequence;
}
```

- `PlayerId`：命令归属；
- `RequestId`：UI pending 状态与结果关联；
- `ClientSequence`：同玩家命令稳定排序及未来网络去重。

不同玩家可以在同一帧提交同种或不同种命令，执行系统按
`PlayerId + ClientSequence` 使用稳定顺序处理。不可依赖 DynamicBuffer、Chunk 或 HashMap 枚举顺序决定冲突结果。

### 9.2 命令类型

首批命令：

```text
SelectRecipePlayerCommand
MoveItemPlayerCommand
PlaceBuildingPlayerCommand
PlaceBeltPathPlayerCommand
RemoveBuildingPlayerCommand
```

`EnterBuildModeCommand` 和 `CancelBuildModeCommand` 只改变本地工具状态，仍通过同一 Bus 路由，
但由 `PlayerBuildController` 的本地 Handler 消费，不进入 Fixed Step。

关键载荷：

```csharp
public struct ItemEndpoint
{
    public ItemOwnerKind OwnerKind;       // Player / Storage / Processor
    public ulong OwnerRuntimeId;
    public ItemSlotDomain Domain;         // Inventory / ProcessorInput / ProcessorOutput
    public ushort SlotIndex;
}

public struct MoveItemPlayerCommand
{
    public PlayerCommandHeader Header;
    public ItemEndpoint Source;
    public ItemEndpoint Destination;
    public ItemId ExpectedItemType;
    public ushort Amount;
}
```

`ExpectedItemType` 用于检测 UI 快照过期，不能替代 ECS 对源槽实际内容的读取。

### 9.3 Bus、Mailbox 与 ECS 入口

`PlayerCommandBus.Submit<TCommand>()` 是 UI 和控制器唯一提交入口：

1. 分配 RequestId；
2. 根据命令类型找到注册 Handler；
3. 本地工具命令立即处理；
4. 权威命令写入 `PlayerCommandMailbox`；
5. `PlayerCommandIngressSystem` 在安全的主线程阶段把 Mailbox 批量写入对应 ECS Buffer；
6. Fixed Step 系统验证并提交；
7. 结果经统一 `PlayerCommandResult` 返回对应玩家。

采用“统一 API + 各命令独立 ECS Buffer/系统”，不采用一个不断膨胀的巨大 union struct。
新增命令时增加命令 struct、Handler、Buffer/System 和错误码，不必修改所有已有系统。

### 9.4 物品移动验证顺序

`ManualItemTransferSystem` 必须一次性验证后原子提交：

1. PlayerId 存在且命令序列有效；
2. Source/Destination Owner 存在；
3. 玩家有权访问目标建筑且仍在允许交互范围；
4. 源、目标不是同一槽；
5. Source 槽位索引有效，ItemId 与数量匹配；
6. Destination 槽位索引有效；
7. 目标槽过滤允许该 ItemId；
8. 目标堆叠兼容且容量足够；
9. 输出槽不可放入，输入槽不可放入错误物品；
10. 全部通过后同时扣源、加目标并推进双方 Revision；
11. 返回成功数量或具体失败原因。

第 8 至 10 步使用“扣除未完成自动运输 reservation 后”的有效数量和容量。命令提交与端口快照
发布位于同一固定 Tick 的受控阶段，确保后续 `BeltTransferSystem` 不会再次消费已被玩家拿走的产物，
也不会向刚被玩家填满的输入槽继续注入。这里不能通过调整 UI pending 状态来解决，必须由 ECS
系统顺序和 reservation 数据保证。

错误码至少包括：`PlayerNotFound`、`OwnerNotFound`、`OutOfRange`、`StaleSnapshot`、
`InvalidSlot`、`EmptySource`、`WrongItem`、`DestinationRejected`、`CapacityExceeded`、
`RecipeBusy`、`SequenceDuplicate`。

### 9.5 建造命令接入

保留现有 `GridBuildCommandSystem` 作为最终建造执行器，在它前面增加玩家命令适配：

- 建筑列表只选择 `BuildingLevelId`；
- 点击格子时提交 `PlaceBuildingPlayerCommand`；
- 适配器验证玩家/建造权限/资源后生成现有 `GridBuildCommand`；
- `GridBuildResult` 映射回带 PlayerId 的统一结果；
- 现有性能场景可以继续直接写 `GridBuildCommand`，不被 UI 依赖污染。

## 10. 拖放交互

### 10.1 生命周期

`ItemDragManipulator` 使用 UI Toolkit Pointer 事件：

1. `PointerDownEvent`：记录源端点、快照版本、物品和数量；
2. 移动超过阈值后 `CapturePointer(pointerId)`；
3. 在 `drag-layer` 显示跟随指针的图标，图标 `PickingMode.Ignore`；
4. `PointerMoveEvent`：用 Panel Pick 找目标 `ItemSlotView`，只做预测高亮；
5. `PointerUpEvent`：释放 Pointer，若目标有效则提交 `MoveItemPlayerCommand`；
6. 收到命令结果前源和目标显示 pending，但不提前改数量；
7. 成功后由新 Snapshot 更新；失败则取消 pending 并显示原因；
8. `PointerCancelEvent`、窗口关闭、目标 Entity 消失时无条件清理 Drag State。

### 10.2 目标预测与权威校验

UI 可以根据 `AcceptedItemId`、`SlotAccess` 和容量做绿色/红色预览，改善手感；
ECS 必须重新校验全部规则。预测结果绝不能直接修改 Snapshot。

第一版操作约定：

- 左键拖拽：移动整个堆叠；
- 放到同类非满槽：合并；
- 放到空槽：移动；
- 放到错误加工输入槽或任意加工输出槽：红色拒绝；
- 放到窗口外：取消，不自动丢弃物品。

后续可在不改命令类型的情况下用 `Amount` 支持右键拆半、Ctrl 移动一个、Shift 快速转移。

## 11. 玩家控制器与输入模式

### 11.1 MonoBehaviour 拆分

保留 OOP 控制器，按单一职责拆分：

| 组件 | 职责 |
|---|---|
| `TopDownPlayerController` | 移动、升降、镜头旋转；不访问背包/建筑内容 |
| `PlayerInteractionController` | 光标格子、建筑命中、交互距离、打开建筑窗口 |
| `PlayerBuildController` | 当前建造工具、旋转、路径预览、提交建造命令 |
| `PlayerInputModeController` | Gameplay/UI/Build Action Map 开关和输入优先级 |
| `PlayerContext` | 当前 PlayerId、Camera 和控制器引用 |

`EcsGridInteractionController` 的格子换算、occupancy 查询、路径预览逻辑迁移到后两者；
`OnGUI` 和数字键菜单在正式 UI 上线后删除。

### 11.2 Input System

项目已有 `Assets/InputSystem_Actions.inputactions`，新增或整理 Action Map：

```text
Gameplay: Move, Look, Interact, ToggleBackpack, ToggleBuildCatalog
Build:    Place, Rotate, Cancel, Remove, TogglePathOrder
UI:       Point, Click, ScrollWheel, Navigate, Submit, Cancel
```

同时在 `Factory.Runtime.asmdef` 增加 `Unity.InputSystem` 引用，并为 Action Asset 启用生成 C# 类
或在控制器上序列化 `InputActionReference`。正式控制器不再混用静态 `UnityEngine.Input` 查询；
项目当前 `Active Input Handling = Both` 可在迁移期间保留，所有控制器迁移完成后再切为 Input System。

建议默认键位：

- 左键：与光标建筑交互/UI 点击/拖放或世界放置；
- Tab：物品栏打开和关闭；
- Q：普通模式进入建筑模式并打开建筑列表；建筑模式中重新打开建筑列表以更换待建建筑；
- F/Ctrl+F：建筑模式下拆除/批量拆除
- 右键拖动：镜头旋转；
- R：建筑模式下旋转；
- Escape：退出建造模式/退出物品栏和建筑交互界面

窗口与模式约束：

- 建筑列表内选择建筑后关闭列表并进入连续建造；一次放置成功后不退出建筑模式；
- 建筑列表打开期间屏蔽全部世界放置、拆除和建筑交互，避免 UI 点击穿透；
- 建筑模式下忽略 Tab 和世界建筑交互；Escape 退出整个建筑模式；
- 普通模式左键点击可交互建筑打开建筑窗口，Tab 独立切换物品栏；二者可同时打开；
- 普通模式按 Escape 同时关闭建筑窗口与物品栏。

输入优先级：

```text
正在拖拽 > 模态窗口 > 指针位于可交互 UI > 建造模式 > 普通 Gameplay
```

当指针位于可交互 UI 时，禁止世界放置/拆除/建筑交互；不能只依赖各控制器碰巧没有收到点击。

## 12. 图标数据方案

### 12.1 选择

在现有表中增加图标 key，并在生成的托管 `FactoryDatabaseAsset` 中保存 `Sprite` 引用：

```text
items.csv:           增加 icon_key
building_levels.csv: 增加 icon_key
```

对应行结构增加：

```csharp
public string iconKey;
public Sprite icon;
```

目录约定：

```text
Assets/Art/Icons/Items/<icon_key>.png
Assets/Art/Icons/Buildings/<icon_key>.png
```

Importer 按现有 Prefab key 的方式校验：缺图标、重名或错误类型直接阻止数据库重建。

### 12.2 为什么不把 Sprite 放进 Blob

`FactoryDatabaseBlob` 保持 Burst 可读的纯数据，只存稳定 ID、Key、数量和规则；`Sprite` 是托管表现资源，
由 `FactoryPresentationCatalog` 在 UI 初始化时按 ItemId/BuildingLevelId 建一次数组索引。
槽位刷新只做数组查找，不使用字符串查找或 `Resources.Load`。

图标纹理统一 Sprite 导入设置并打入 Sprite Atlas，减少 UI 批次和运行时纹理切换。

## 13. 建议目录与文件

```text
Assets/Scripts/ECS/Data/
  EcsPlayerComponents.cs
  EcsInventoryComponents.cs
  EcsPlayerCommandComponents.cs
  EcsUiRevisionComponents.cs

Assets/Scripts/ECS/Systems/
  PlayerCommandIngressSystem.cs
  RecipeSelectionCommandSystem.cs
  ManualItemTransferSystem.cs
  PlayerGridCommandAdapterSystem.cs
  UiSnapshotExportSystem.cs

Assets/Scripts/ECS/Presentation/Player/
  PlayerContext.cs
  TopDownPlayerController.cs
  PlayerInteractionController.cs
  PlayerBuildController.cs
  PlayerInputModeController.cs

Assets/Scripts/ECS/Presentation/UI/
  GameUiController.cs
  UiWindowManager.cs
  UiDataHub.cs
  PlayerCommandBus.cs
  PlayerCommandMailbox.cs
  DataSources/
  ViewModels/
  Views/
  DragDrop/

Assets/UI/Runtime/
  GameUiRoot.uxml
  GameUiTheme.uss
  Windows/BuildingWindow.uxml
  Windows/BackpackWindow.uxml
  Windows/BuildCatalogWindow.uxml
  Controls/ItemSlot.uxml
  Controls/RecipeCard.uxml

Assets/Art/Icons/Items/
Assets/Art/Icons/Buildings/
```

这些文件仍位于 `Assets/Scripts` 根 asmdef 下，继续编译进 `Factory.Runtime`。编辑器导入逻辑留在
`Factory.Editor`，测试留在 `Factory.Tests`。

## 14. 性能设计

### 14.1 ECS 侧

- Fixed Tick 不调用 UI，不产生字符串，不创建托管集合；
- UI 命令属于低频事件，批量进入 typed DynamicBuffer；
- 物品移动一次最多修改两个槽位和两个 Revision；
- 加工系统继续使用 Burst Job，并以 Buffer 线性扫描少量配方槽；
- 不为窗口、槽位、图标或拖拽对象创建 Entity；
- 不因 UI 打开而增加对全部建筑的查询；
- 关闭所有窗口时，快照导出系统仅保留静态目录和命令结果的最小工作。

### 14.2 UI 侧

- 常用窗口和槽位复用 VisualElement；
- `display: none` 隐藏关闭窗口，不使用 opacity 0 保持绘制；
- 只在 Revision 变化时改图标、数量和槽位 class；
- 进度箭头只修改一个 transform/width，不重建树；
- 拖拽图标只有一个；
- 图标用 Atlas；
- 不在 `Update` 中执行 `Q()`、字符串路径查找或 Clone UXML；
- View 在初始化时缓存所有命名元素引用；
- 不使用每帧全树 runtime binding。

### 14.3 性能验收

在现有 `Perf_4096_Mk4_FullLoop` 等场景验证：

- UI 关闭时 Fixed Tick 基线无可测退化；
- 同时打开建筑和背包时不出现全实体扫描；
- 稳态 Fixed Tick 托管 GC 为 0；
- 静止 UI 的托管 GC 为 0；
- 连续拖放只在操作和快照更新时产生有限 UI 工作；
- `ItemPortBufferSwapSystem`、运输仲裁和物品表现时间不因 UI 随建筑数量线性增长。

## 15. 实施阶段

### Phase 1：数据模型前置

1. 新增 PlayerId、Player Entity 和固定槽玩家背包；
2. 仓库迁移到稳定槽位模型；
3. 加工建筑初始配方改为 `-1`；
4. 加工状态升级为多输入/多输出槽；
5. 扩展输入端口的多 ItemId 容量表达；
6. 为容器和加工槽添加 Revision；
7. 同步修改 `FactoryPerformanceScenarioLayout` 和 `FactoryPerformanceScenarioBootstrap`，让测试建筑
   显式选择配方，不能继续依赖第 0 条默认配方；
8. 更新 `PerformanceScenarioLayoutTests` 及相关 Fixture；
9. 完成 ECS 单元/系统回归测试，并重新验证 `Perf_ProducerConsumer` 确实产生持续吞吐。

完成定义：在没有 UI 的测试中，玩家、仓库、生产建筑之间的物品命令可以正确执行，
多输入/多输出配方可以正确加工。

### Phase 2：UI 基础设施

1. 建立单 `UIDocument` 根树和 Dock；
2. 实现 `UiWindowManager`、`UiDataHub` 和订阅生命周期；
3. 实现 `UiSnapshotExportSystem`；
4. 实现图标导入和 `FactoryPresentationCatalog`；
5. 接入 Input System 和输入优先级。

完成定义：空窗口可独立/同时打开关闭；关闭窗口不会继续读取容器 Buffer。

### Phase 3：窗口内容

1. 背包网格；
2. 仓库网格；
3. 配方选择页；
4. 三栏加工页与进度；
5. 建筑列表和建造模式入口。

完成定义：所有窗口只消费 Snapshot，不直接访问 ECS。

### Phase 4：统一命令与拖放

1. `PlayerCommandBus`、Mailbox、Ingress；
2. 配方选择命令；
3. 原子物品移动命令；
4. 建造命令适配；
5. Pointer Capture 拖放、pending 和错误反馈；
6. 不同 PlayerId 的隔离和稳定排序测试。

完成定义：任何玩法状态变化都能追溯到带 PlayerId/RequestId 的命令及结果。

### Phase 5：替换原型入口与验收

1. 从正式场景移除 `Stage3PrototypeHud.OnGUI`；
2. 拆分并替换 `EcsGridInteractionController.OnGUI/输入选择`；
3. 保留性能场景的无 UI 路径；
4. 运行完整 `Factory.Tests`；
5. 执行 UI PlayMode 用例和性能场景；
6. 更新 Gameplay 开发指南和操作说明。

完成状态（2026-08-09）：正式 `Ecs` 场景已移除 `Stage3PrototypeHud`；建造
选择完全由 UI Toolkit 目录负责，放置、旋转、路径顺序与拆除统一读取 Input
System Build Action Map；性能 Bootstrap 会停用 `GameUiRoot`；Phase5 PlayMode
用例覆盖原型入口移除及 Gameplay/Build/Modal Action Map 切换。回归修复后，
Q 同时存在于 Gameplay/Build Map，目录快照不再重置待确认建筑，R 在传送带
起点选定后切换横竖优先。配方卡和建筑卡均改为池化复用，连续快照不再在
PointerDown/PointerUp 之间替换按钮；“更换配方”按钮强制位于加工流程层
之上，配方页状态按建筑隔离。最终验收通过 81/81 EditMode、6/6
PlayMode；`Perf_4096_Mk4_FullLoop`（scale 64、4096 节点、100% 装载）
完成 583 帧短采样，均值 3.407 ms、P95 5.609 ms。

## 16. 测试矩阵

### 16.1 ECS EditMode/System 测试

- 新生产建筑 `SelectedRecipeIndex == -1`；
- 只能选择与 MachineType 匹配的配方；
- Busy 或槽内有物品时更换配方失败；
- 多输入全部满足才原子扣除；
- 多输出全部写入且无部分产出；
- 玩家到仓库、仓库到玩家移动成功；
- 加工输入槽拒绝错误 ItemId；
- 加工输出槽拒绝放入；
- 满堆叠、满容器、过期快照和失效 Owner 返回正确错误；
- 同一玩家重复 ClientSequence 不重复执行；
- 不同 PlayerId 的命令和结果不会串线；
- 同 Tick 冲突遵循稳定排序；
- 自动运输与手动移动同时操作时不丢失、不复制物品；
- 每次成功变化只推进相关 Revision。
- 测试/性能场景创建 Processor 时必须显式指定配方，未指定时保持 `SelectedRecipeIndex == -1`。

### 16.2 UI/PlayMode 测试

- 建筑窗口单开位于左侧；
- 背包单开位于右侧；
- 两者同时打开位置不跳变；
- 无配方建筑显示配方页；
- 选配方成功后切到加工页；
- 输入/输出每种物品各一槽且虚影正确；
- 进度箭头从 0 到 1；
- 仓库与背包网格正确显示空槽和堆叠；
- 拖错槽显示拒绝且不提交或由 ECS 拒绝；
- 拖放成功前不乐观改数量；
- PointerCancel、关闭窗口和建筑被拆除时拖拽清理；
- 点击 UI 不触发世界建造；
- 建筑列表选择后进入建造模式并能取消；
- Escape 优先级符合设计。

### 16.3 数据导入测试

- Item/BuildingLevel 缺 icon_key 报错；
- icon_key 找不到 Sprite 报错；
- 同 key 图标歧义报错；
- 图标数组按运行时 ID 正确索引；
- 配方多输入/多输出导入与槽位顺序一致。

### 16.4 测试场景 Bootstrap 回归

- `FactoryPerformancePlacement` 能为 Processor 保存 RecipeKey；
- `CreateProducerConsumer()` 为 Miner 配置 `mine_iron`、为 Furnace 配置 `smelt_iron`；
- `FactoryPerformanceScenarioBootstrap` 在配方未应用完成前不会报告 READY；
- RecipeKey 不存在、MachineType 不匹配或命令失败时 Setup 明确失败；
- `Perf_ProducerConsumer` 在 Warmup 后 `acceptedTransferCount > 0`，不是只有建筑数量正确；
- 纯传送带场景和 `FactoryBuildStressDriver` 不需要伪造配方命令。

## 17. 最终验收标准

功能全部满足以下条件才算完成：

1. 生产建筑未选配方时显示其全部可用配方；
2. 选中后显示输入、进度、输出三栏，所有物品种类均有独立槽位和虚影；
3. 输入槽只接受指定物品，输出槽只能拿出；
4. 仓库和玩家背包均为网格，并可在两窗口间拖放；
5. 建筑窗口可单开、背包可单开，两者可左/右同时打开；
6. 建筑列表来自数据库，选择后进入现有 ECS 建造流程；
7. 物品、配方和建造操作全部由统一命令入口提交并返回结果；
8. 每条权威命令包含 PlayerId，多个玩家的顺序、权限和结果相互隔离；
9. 新增窗口无需自己创建 EntityQuery；
10. 新增命令无需修改一个中央巨型 union；
11. 所有物品与建筑等级都有 UI 图标；
12. UI 关闭时不会扫描全部 ECS 实体，Fixed Tick 稳态无托管 GC 回归；
13. 原有运输、建造和性能测试继续通过。

## 18. Unity 官方资料

- [Unity 6 UI Toolkit 运行时 UI](https://docs.unity3d.com/cn/6000.0/Manual/UIE-support-for-runtime-ui.html)
- [Unity 6 运行时 UI 事件系统与 Input System](https://docs.unity3d.com/cn/6000.0/Manual/UIE-Runtime-Event-System.html)
- [Unity 6 运行时数据绑定](https://docs.unity3d.com/cn/6000.0/Manual/UIE-runtime-binding.html)
- [UI Toolkit Pointer 捕获](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-capture-the-pointer.html)
- [UI Toolkit 性能优化](https://docs.unity3d.com/6000.0/Documentation/Manual/best-practice-guides/ui-toolkit-for-advanced-unity-developers/optimizing-performance.html)
