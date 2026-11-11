# 建筑与生产内容定义

更新日期：2026-11-11。本文保留原文件名以维持文档链接。

## 数据来源与编辑

`Assets/Data/FactoryDatabase.asset` 是正式数据总入口，通过 **Factory > 内容管理器** 编辑其引用的三个领域内容库。CSV 保存层已移除，不再执行 CSV 重建。**Factory > Validate Static Database** 可校验整个定义集合。

内容库保留四组现有强类型数据：

| 内容库 / 数组 | 元素类型 | 内容 |
|---|---|---|
| `ItemDatabase.asset / items` | `FactoryItemTableRow` | ID、Key、名称、堆叠、分类、模型/图标及能力子资产引用 |
| `BuildingDatabase.asset / machineTypes` | `FactoryMachineTypeTableRow` | 加工类型 ID 和 Key |
| `BuildingDatabase.asset / buildings` | `FactoryBuildingDefinitionRow` | 建筑基础属性、空间、表现、行为参数、端口与碰撞体 |
| `RecipeDatabase.asset / recipes` | `FactoryRecipeTableRow` | 配方 ID、Key、加工类型、加工时间和内嵌原料/产物 |

类型名中的 TableRow 为现有代码名称，不代表仍需外部表格。物品能力以物品库内部的 ScriptableObject 子资产保存，不拆分每物品独立资产，见 [物品能力与领域内容库](Item-Capabilities.md)。端口、碰撞体、配方原料/产物保持内嵌，不重新拆成关联表。

所有既有数字 ID 原样保留。新增项分配当前同类最大 ID + 1；数组重排和 Key 修改不重算 ID。Key 用于内容查找，模拟使用数字 ID；配方同时保存关联 ID/Key，并校验两者一致。该迁移不建立跨版本存档兼容层。

模型、图标直接引用 Unity 资产。路径和 iconKey 字段仅保留作兼容元数据，编辑器以实际对象引用为准。FBX 仍要求仅包含表现网格，不维护包装 Prefab。图标可为空，也可选择任意项目 Sprite。名称使用现有 `displayName`，没有运行时语言切换系统。

## 建筑参数

建筑 `kind` 对应 `BuildingKind`：Processor、Storage、Belt、Merger、Splitter、Foundation、RampFoundation、VerticalBeltEntry、VerticalBeltExit、VerticalBeltSegment。

- Processor 配置 `machineTypeId`；矿机和熔炉的区别来自加工类型、配方、模型和端口。
- Storage 配置 `storageSlotCount`；每格按物品最大堆叠计算，没有额外总容量。
- 水平和垂直传送带配置 `beltTier / cellsPerSecond`。
- RampFoundation 配置 `rampRiseHeightUnits`。
- 非对应行为的参数必须清零。修改行为时，内容管理器自动清理旧行为字段并设置新行为默认值。

建筑没有通用等级或加工速度倍率。加工周期由配方 `durationSeconds` 决定，固定 Tick 下 `process.DurationTicks = recipe.DurationTicks`；并联机器增加理论产量，缺料和物流堵塞仍限制实际产量。

## 占格、端口与生产

占格字段为 `footprintWidth / footprintHeight / isRectangle / footprintBitmap`；端口直接保存在建筑的 `ports` 数组中。坐标、位图及边界规则见 [建筑占格与端口定义](Building-Footprints.md)。管理器只展示空间示意，通过 Blender 导出的 `.factory.json` 更新空间数据。

一个 Processor 可执行对应加工类型的所有配方，多种建筑可使用相同加工类型。所有输入口接受当前配方需要的任意原料，按物品类型汇入对应输入槽；不同输入口共享同一原料槽容量和在途预约。

Processor 输出口的 `number` 和 `outputSlot` 均为从 1 开始的正整数，在同一建筑内分别唯一。`index` 是内部端口身份，无须等于显示编号。`outputSlot` 对应配方 `outputs` 数组的相对顺序，不按物品 ID 排序。当前配方没有相应槽时出口停用；所有可用配方的每个产物槽都必须有出口。输入口和非 Processor 端口的这两个字段为零。

生产开始前检查所有原料齐备且所有产物槽放得下完整下一批，再统一扣料。任意产物槽不足则整机不开工，输出排空和未满输入槽收料仍继续。切换配方清理旧绑定、快照、预约和回执，沿用现有物资规则。

UI 在产物槽显示出口编号；10m 内瞄准出口时，世界空间 TextMesh 显示同一编号，文字朝向相机并受遮挡限制。端口命中区、旋转、楼层和 CellSize 适配规则保持不变。

## 传送带

同 Tier 的水平带和垂直部件必须配置相同的有限正数速度；`(kind, beltTier)` 唯一。垂直 Tier 必须同时具备入口、出口和段。垂直路径按 Tier 关联部件，不依赖命名后缀；斜坡传送带复用水平带定义。

## 建筑碰撞体

`FactoryBuildingDefinitionRow.colliders` 保存 `FactoryColliderProxy[]`，每个形状的 `name` 在本建筑内唯一。同一个 FBX 可以被多个建筑引用，各自拥有不同的碰撞定义。

位置和旋转相对建筑根节点，尺寸使用 Unity 单位，旋转保存单位四元数：

| 形状 | 参数约束 |
|---|---|
| Box | 三个尺寸均大于零 |
| Sphere | 半径大于零 |
| Capsule | 半径大于零，总高度至少为直径，局部 Y 轴 |
| Cylinder | 半径和高度大于零，局部 Y 轴；Unity Physics 采用 32 边圆柱 |

空数组表示不附加定义碰撞体；地基、坡道和垂直传送带的专用碰撞机制保持不变。建筑占格与碰撞形状独立定义。

## 校验与运行时

内容管理器保存前、构建前校验正式内容，Baking 校验模拟定义。检查唯一非零 ID、唯一有效 Key、加工类型与物品引用、原料/产物数量及重复项、最大堆叠、行为参数、占格、端口、产物覆盖和碰撞形状。编辑器额外检查 FBX 和建筑名称。

Baking 直接从 ScriptableObject 创建 `FactoryDatabaseBlob`。建筑模拟数据进入 `FactoryBuildingDefinitionBlob`，名称和图标留在表现目录。实体、建造命令及 UI 使用相同的 `BuildingDefinitionId`。运行时不解析源文本，也不读取编辑器草稿。

操作细节见 [Factory 内容管理器](Factory-Content-Manager.md)。回归入口为 `BuildingDefinitionTests`、`FactoryContentEditingTests`、`FactoryBuildingContentEditingTests` 及现有建造、加工、碰撞与运输测试。
