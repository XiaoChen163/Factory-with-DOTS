# Factory 静态数据表

这些 CSV 是建筑、物品和配方的编辑源。不要手工修改 `Assets/Data/Generated/FactoryDatabase.asset`。

## 模型导入

在 Unity 的 `Factory > 内容管理器` 点击“导入模型…”，选择建筑或物品模式。应用后进入可撤销草稿，“保存并重建”统一提交 CSV。FBX 和纹理复制会立即写入资产。

- 建筑模式：配对 `.factory.json` 提供占格、端口和碰撞，FBX 提供模型。一次导入同时更新四部分。
- 物品模式：只导入 FBX 并新增或更新物品表；不生成占格、端口、碰撞体或刚体。
- 两张主表的 `model_path` 都是完整的项目内 FBX 路径，不接受模型 Prefab。
- 多个物品可共用 FBX；烘焙按 ItemId 创建独立实体模板，对象池保持各自物品身份。

## 物品与配方

| 表 | 内容 |
|---|---|
| `items/items.csv` | `id,key,name_key,max_stack,category,model_path,icon_key` |
| `items/recipes/recipes.csv` | 配方 Key、加工类型、生产时间 |
| `items/recipes/recipe_inputs.csv` | 配方输入物品和数量 |
| `items/recipes/recipe_outputs.csv` | 按源行顺序定义产物槽与数量 |

物品 ID 0 为无效值；已发布 ID 不应修改或复用。配方单种物品数量不超过该物品的最大堆叠。修改物品或配方 Key 会同步条目引用。

## 建筑

| 表 | 内容 |
|---|---|
| `buildings/buildings.csv` | Key、行为、宽长、`model_path`、矩形标记和占格位图 |
| `buildings/building_ports.csv` | 每行一个端口：类型、索引、边界格坐标、方向及 Processor 产物槽 |
| `buildings/building_colliders.csv` | 每行一个碰撞体：`building_key,collider_key,type`、局部位置、单位四元数、尺寸、半径和高度 |
| `buildings/building_names.csv` | 建筑 Key、语言、名称 |
| `buildings/building_presentation.csv` | 建筑 Key、图标路径、菜单顺序 |
| `buildings/processor_stats.csv` | 加工类型 |
| `buildings/belt_stats.csv` | 传送带 Tier 和速度 |
| `buildings/storage_stats.csv` | 仓储槽数 |
| `buildings/ramp_stats.csv` | 坡道高度单位 |
| `buildings/machine_types.csv` | 加工类型字典 |

无通用建筑等级；加工耗时来自配方。关联表通过建筑 Key 连接。碰撞支持 Box、Sphere、Y 轴 Capsule 和 Cylinder；同一建筑的碰撞体 Key 必须唯一。占格与物理碰撞独立。详见 [建筑数据表指南](../../../Docs/Guide/Building-Data-Tables.md)。

## 图标与重建

图标管线读取建筑和物品表中的 FBX，输出到 `Assets/Art/Icons/Buildings` 和 `Assets/Art/Icons/Items`。物品 `icon_key` 留空时按物品 Key 查找默认图标，非空时按已有图标文件名匹配。建筑图标路径由表现表指定。

`Factory > Icons > Bake Changed` 增量生成图标，`Bake All` 强制重建；`Factory > Rebuild Static Database` 校验全部 CSV 并重建数据库。运行时身份解析为紧凑 ID，长期存档仍应保存稳定 Key。
