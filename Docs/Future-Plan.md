1. 物品、配方管理器（编辑器内界面/独立界面，方便操作不用手动配表）
2. UI内物品中文名显式（支持多语言）
3.  机器可视化编辑界面（机器的出入口仅看表很抽象，需要可视化界面）
4.  多出入口机器完善（指定每个机器的出入口的输入和输出）
5.  工厂建筑拥有碰撞体积
6.  多层建造&垂直传送带（场景内不止一个网格系统，可以存在多个网格区块，但网格区块的原点一定在整数坐标，方便不同网格系统间互相访问；并且区块不一定是长方形）
7.  支持非长方形建筑（建筑的占格可以是其他形状）
8.  放置预览独立shader（蓝色半透明，这样可以看到形状）
9. 第一人称玩家控制器代替现有topdown玩家控制器，但是摆放建筑时需要宽阔的topdown视角方便放置建筑，现有的控制器可以作为第一人称控制器的一种模式保留）
10. 加入地图场景，矿点等，矿机不再凭空产生矿

以上内容是项目未来的规划, 优先级：

建议的启动顺序是：

**6 → 7 → 4 → 10（矿点运行时）→ 5 → 2 → 1 → 3 → 8 → 10（地图内容）→ 9**

第 10 项应拆成“矿点底层规则”和“地图场景制作”两阶段，因此会较早开始、较晚完成。

| 优先级 | 原编号 | 规划 | 排序原因 |
|---:|---:|---|---|
| 1 | 6 | 多层建造、多网格区块、垂直运输 | 改动最底层。当前坐标、占用、建造、射线检测和传送拓扑都依赖“唯一二维矩形网格”。 |
| 2 | 7 | 非长方形建筑 | 建筑占格模型需要在空间系统稳定后确定，也会影响碰撞、预览和端口编辑。 |
| 3 | 4 | 完善多出入口机器 | 当前虽然有多端口 Buffer，但处理器实际上会遍历“每个端口 × 每个配方槽位”，缺少明确的端口—物料绑定。 |
| 4 | 10A | 矿点数据模型与矿机约束 | 先实现矿点类型、储量、可开采位置、消耗和矿机放置规则，停止矿机凭空生产。 |
| 5 | 5 | 建筑碰撞体积 | 必须在建筑形状稳定后实现，同时是地图探索和第一人称控制器的前置条件。 |
| 6 | 2 | 正式多语言系统 | 项目已有 `NameKey`，但当前只是把 Key 直接显示出来；应在内容编辑器之前确定字符串表和回退规则。 |
| 7 | 1 | 物品、配方管理器 | 等物品、配方、矿点和多端口的数据结构稳定后再做，避免编辑器反复重构。 |
| 8 | 3 | 机器可视化编辑界面 | 依赖异形占格和多端口最终模型，适合与第 1 项合并成统一的 Factory Content Editor。 |
| 9 | 8 | 独立放置预览 Shader | 属于表现层。应直接显示建筑真实模型/占格轮廓，而不仅是当前的方块格子。 |
| 10 | 9 | 第一人称与建造俯视模式 | 依赖地图、建筑碰撞、多层网格和最终输入逻辑，最后改最稳妥。 |

最重要的第一阶段应先建立统一空间地址，例如：

```text
GridAddress
├─ RegionId：稳定区块 ID
└─ GlobalCell：int3 整数世界格坐标
```

每个网格区块保存整数原点和“有效格子集合/分块位图”，而不是只保存矩形宽高。建筑、传送带、端口、矿点和占用索引都使用 `GridAddress`。垂直传送带则应当是运输拓扑中的一种三维连接，不要做成绕过拓扑系统的特殊机器。

这是当前最主要的返工风险：现有 [EcsGridComponents.cs](D:/UnityProject/Factory-with-DOTS/Assets/Scripts/ECS/Data/EcsGridComponents.cs:4) 使用二维格子，而 [GridOccupancyIndexSystem.cs](D:/UnityProject/Factory-with-DOTS/Assets/Scripts/ECS/Systems/GridOccupancyIndexSystem.cs:61) 明确要求世界中只能有一个 `GridDefinition`。

几个具体建议：

- 非长方形建筑使用显式 `OccupiedCellOffsets`；宽高只作为包围盒和旋转中心。项目已经有这个 Buffer，但 [GridBuildCommandSystem.cs](D:/UnityProject/Factory-with-DOTS/Assets/Scripts/ECS/Systems/GridBuildCommandSystem.cs:866) 仍自动填满整个矩形。
- 多端口应增加 `PortBinding`：指定端口对应哪个配方输入/输出槽、允许的物品或过滤规则，避免所有输入口都接受所有原料。
- 第 1、3 项最好做成同一个编辑器工具，包含“物品、配方、建筑、占格、端口、矿点、翻译”多个页面，共用稳定 ID、引用校验和 Undo/Redo。
- 编辑器应修改唯一的源数据并调用现有导入器，不能直接编辑生成的 `FactoryDatabase.asset`，否则会形成双数据源。
- 本地化字符串只留在表现层；ECS Blob 保存 `NameKey` 即可。当前 [FactoryPresentationCatalog.cs](D:/UnityProject/Factory-with-DOTS/Assets/Scripts/ECS/Presentation/UI/FactoryPresentationCatalog.cs:33) 尚未真正解析本地化表。
- 放置预览应使用建筑真实 Prefab 的 Ghost Renderer，并用蓝色半透明/红色无效状态覆盖材质。当前实现只是运行时创建透明方块材质。
- 第一人称功能应迁出 `Prototype`，建立 `FirstPerson / BuildOverview` 两种正式输入与相机状态，而不是继续扩展现有 [TopDownPlayerController.cs](D:/UnityProject/Factory-with-DOTS/Assets/Scripts/Prototype/Input/TopDownPlayerController.cs:3)。

简化成里程碑就是：**先重建空间坐标体系，再稳定建筑与物流数据模型，然后补矿点和碰撞，最后建设内容工具、表现与玩家控制。**