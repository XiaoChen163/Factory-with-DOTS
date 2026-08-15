# Factory static data tables

这些 CSV 是物品与配方的唯一编辑源。不要手工编辑
`Assets/Data/Generated/FactoryDatabase.asset`。

- `items.csv`：稳定 `ItemId`、显示 Key、堆叠、分类、`prefab_key` 和可选的 `icon_key`。
- `machine_types.csv`：配方要求的加工能力 key。
- `buildings.csv`：建筑类型、能力、占地和共享端口布局。
- `building_levels.csv`：所有建筑共有的等级身份、表现 Prefab 和菜单顺序；斜面项另用
  `ramp_rise_height_units` 保存坡高（每层 8 单位，当前允许 8、4、2）。
- `belt_level_stats.csv`：传送带等级的绝对空间速度（格/秒）。
- `processor_level_stats.csv`：加工建筑等级相对于配方基础耗时的工作倍率（千分比）。
- `storage_level_stats.csv`：仓库等级可存放的物品总数。
- `building_ports.csv`：按布局 key 定义的逻辑输入/输出端口。
- `recipes.csv`：配方 key、加工能力 key 和基础生产时间。
- `recipe_inputs.csv`：配方输入。
- `recipe_outputs.csv`：配方输出。

保存 CSV 后，Unity 会自动重建生成资产。也可以执行菜单
`Factory > Rebuild Static Database` 手动重建。

物品和建筑图标由 Editor 自动从表现 Prefab 烘焙。默认图标 Key 与对应
`prefab_key` / `visual_prefab_key` 相同，因此 CSV 可以省略 `icon_key`；只有
需要让多个数据行共享另一张图标时才需要显式填写。输出目录分别是
`Assets/Art/Icons/Items` 和 `Assets/Art/Icons/Buildings`。可以通过菜单
`Factory > Icons > Bake All` 强制重建，或在
`Factory > Icons > Create or Select Settings` 中调整视角、灯光和构图边距。

CSV 之间使用稳定 key 关联；导入器会验证引用并在生成资产中解析为紧凑 ID。
运行时 ID 和 Blob 数组下标不应写入长期存档，存档应保存 key。

物品 Prefab 放在 `Assets/Prefabs/Items` 下，并使用与 `prefab_key`
一致的文件名。匹配时忽略大小写、空格、下划线和连字符，例如
`iron_ore` 可以匹配 `IronOre.prefab`。物品 Prefab 只能包含表现组件，
不需要 `ItemAuthoring` 或其他 `MonoBehaviour`。

建筑表现 Prefab 放在 `Assets/Prefabs/Buildings` 下，并通过
`building_levels.csv` 的 `visual_prefab_key` 绑定。每个等级可以使用完全
不同的 Mesh 和材质，但 Prefab 只能包含表现组件，不能包含建筑逻辑
Authoring。建筑逻辑、占地和端口由 CSV 生成；同一建筑类型的所有等级
共享 `port_layout_key`。每一条等级记录都是独立建造选项，行为专属属性
不放在这张通用等级表中。每个传送带等级必须在 `belt_level_stats.csv`
中恰好出现一次，每个加工建筑等级必须在 `processor_level_stats.csv`
中恰好出现一次，每个仓库等级必须在 `storage_level_stats.csv` 中恰好
出现一次；无关建筑等级不得出现在这些行为专属表中。
斜面地基表现 Prefab 还允许包含用于坡面拾取/碰撞的 `MeshCollider`。
`work_rate_permille` 只影响实际加工速度，不修改配方基础时间。

加工建筑不配置固定容量。每个配方输入物品种类占用一个输入槽，输出物品
种类占用一个输出槽，因此槽位数为输入种类数 `n` 加输出种类数 `m`；每个
输入槽按对应物品的 `max_stack` 限制数量。仓库总容量则由
`storage_level_stats.csv` 按建筑等级独立配置。

当前生产逻辑支持每条配方零或一个输入，并且必须恰好有一个输出，所以
Miner 实际为 `0+1` 个槽，Furnace 实际为 `1+1` 个槽。配方中单种物品的
数量不能超过该物品的 `max_stack`。
ID `0` 保留为无效值；已经发布的 ID 不应修改或复用。
