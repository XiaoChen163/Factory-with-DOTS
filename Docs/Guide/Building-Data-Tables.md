# 建筑数据表

2026-10-30 起，建筑内容取消通用等级。加工建筑没有速度倍率，加工周期完全取决于
`items/recipes/recipes.csv` 的 `duration_seconds`。传送带仍有专属 Tier。

源表位于 `Assets/Data/FactoryTables/buildings`。建筑 Key 是唯一的源数据身份，
扩展表直接通过同一个 Key 关联，不再维护类型 ID、等级 ID 或端口布局 Key。

## 表的职责

| 表 | 字段 | 必填范围 |
|---|---|---|
| `buildings.csv` | `key,behavior,width,length,prefab_path,is_rectangle,bitmap` | 所有可建造对象各一行 |
| `processor_stats.csv` | `key,machine_type_key` | 每个 Processor 各一行 |
| `belt_stats.csv` | `key,tier,cells_per_second` | 水平带、垂直入口/出口/段各一行 |
| `storage_stats.csv` | `key,capacity,slot_count` | 每个 Storage 各一行 |
| `ramp_stats.csv` | `key,rise_height_units` | 每个 RampFoundation 各一行 |
| `building_ports.csv` | `key,type,index,cell_x,cell_z,direction_x,direction_z` | 每个物理端口一行 |
| `building_names.csv` | `key,locale,name` | 每个建筑至少一种语言 |
| `building_presentation.csv` | `key,icon_path,menu_order` | 每个建筑各一行 |
| `machine_types.csv` | `key` | 加工种类字典，供加工建筑和配方共同引用 |

`behavior` 直接对应 `BuildingKind`：`Processor、Storage、Belt、Merger、Splitter、
Foundation、RampFoundation、VerticalBeltEntry、VerticalBeltExit、VerticalBeltSegment`。
矿机、熔炉都属于 Processor，其区别由加工种类、配方、模型和端口定义。

Prefab 和图标使用完整的 `Assets/...` 工程相对路径，Prefab 必须是 `.prefab` 资产。
`icon_path` 可以留空；填写时必须能加载为 Sprite。游戏图标由表现表指定；图标烘焙
设置仍负责图像生成，但不在导入时覆盖表现表的路径。
名称导入当前优先取 `zh-CN`，缺少时回退为该建筑第一条名称；这不是运行时语言切换系统。

## 示例

```csv
key,behavior,width,length,prefab_path,is_rectangle,bitmap
furnace,Processor,4,3,Assets/Prefabs/Buildings/Furnace.prefab,false,1111/1111/1100
```

```csv
key,machine_type_key
furnace,smelting
```

```csv
key,type,index,cell_x,cell_z,direction_x,direction_z,number,output_slot
furnace,Input,0,-1,0,1,0,,
furnace,Output,0,4,0,1,0,1,1
```

一个 Processor 可以执行对应加工种类的配方，多种建筑可以绑定相同加工种类。
所有输入口接受当前配方需要的任意原料，按物品类型汇入对应输入槽；不同输入口共享
同一原料槽的容量和在途预约。一个槽满不会阻止其他口输入其他未满原料。

Processor 输出口必须填写 `number` 和 `output_slot`，两者均为从 1 开始的正整数，
在同一建筑内分别唯一：一个出口绑定一个产物槽，一个产物槽只有一个出口。
`index` 仍是内部端口身份，不要求与玩家可见的 `number` 相同。
`output_slot` 对应 `recipe_outputs.csv` 中同一配方条目的相对顺序，不按物品 ID 排序。
当前配方没有对应槽时出口停用；每条可用配方的所有产物槽都必须有出口。
输入口及非 Processor 端口的这两列留空。

UI 在产物槽上方显示配置的出口编号；玩家在 10m 内瞄准出口时，在出口处通过世界空间
TextMesh 显示相同编号，文字朝向相机，移开视线或被遮挡后隐藏，复用一个文字对象。
该提示由相机交互控制器在 LateUpdate 更新，不依赖 UIDocument、HUD 层或屏幕坐标转换。
端口命中区覆盖 OutputPortView 的完整可见区域（高 0.75 格、宽 1 格并留少量容差），
随建筑旋转、楼层及 CellSize 变化。遮挡检测忽略玩家自身及触发器，文字材质使用深度测试。
建筑尚无物理碰撞时，用实际占格组成的逻辑体积做遮挡（加工/仓储高度暂为 1.2 格，
其他建筑暂为 0.5 格）；已有 ECS/场景物理碰撞也会阻挡提示。

生产开始前要求所有输入齐备、所有输出槽放得下完整下一批，再统一扣除输入。
任意产物槽放不下下一批，整机暂停开工；输出排空及未满输入槽的收料仍然继续。
切换配方时返还/清理原有物料，并清除旧端口快照、预约和回执，随后重新发布绑定。

旧熔炉样例 `test_multi_io` 已由测试机器 M 的两条专用配方替代，避免单出口对应多个产物槽。
源数据不再包含 `work_rate_permille` 或加工等级属性。
固定 Tick 下，`process.DurationTicks = recipe.DurationTicks`；增加并联机器增加理论产量，
物流阻塞和缺料仍会限制实际产量。

占格坐标、位图和端口边界规则见 [建筑占格与端口定义](Building-Footprints.md)。

## 传送带

同一个 Tier 的水平带和所有垂直部件必须填写相同的正数速度。
`(behavior,tier)` 唯一，垂直 Tier 必须同时提供入口、出口和段。
垂直路径按 Tier 查找对应部件，不依赖命名后缀或通用建筑等级。
斜坡上的传送带复用水平带定义及其速度。

这里采用每个建筑 Key 直接配置 `tier,cells_per_second` 的方案，因此同等级速度在
各部件行中重复，由导入校验保证一致。

## 导入、运行时与迁移

修改源 CSV 后执行 **Factory > Rebuild Static Database**。生成的
`Assets/Data/Generated/FactoryDatabase.asset` 是缓存，不是另一份编辑源。

导入器检查重复/未知 Key、必填扩展行、行为匹配、Prefab/图标路径、加工种类、
仓储属性、传送带 Tier/速度、斜坡坡度、位图和端口边界。
无效内容不会保存为新的生成数据库。

生成缓存将同一建筑的扩展属性合并为 `FactoryBuildingDefinitionRow`；Blob 用
`FactoryBuildingDefinitionBlob` 保存模拟所需数据。ECS 实体、建造命令、图标目录和
快捷栏统一使用 `BuildingDefinitionId`，`BuildingIdentity` 仅保存这个 ID。
UI 名称和图标留在表现层，不放入模拟 Blob。

数字 ID 按建筑主表行序生成，仅在当前生成内容中有效，不能作为跨版本存档身份。
按 Key 引用的内容应在加载/初始化边界解析 ID，模拟循环使用数字 ID。
性能场景已改为按建筑 Key 选择内容，不再依赖主表行序。
`GridCell.Level` 仍表示世界楼层，与本次移除的建筑等级无关。

旧 `levels/building_levels.csv`、加工等级、仓储等级和传送带等级属性表已删除。
普通建筑 Key 去掉 `_mk1`，传送带保留 `belt_mk1` 等具体建筑 Key。
本次迁移没有新增存档格式兼容层。

回归测试见 `BuildingDefinitionSimplificationTests`、`IrregularBuildingFootprintTests`
以及现有加工、建造和垂直运输测试。
