# Factory static data tables

这些 CSV 是物品与配方的唯一编辑源。不要手工编辑
`Assets/Data/Generated/FactoryDatabase.asset`。

- `items.csv`：稳定 `ItemId`、显示 Key、堆叠、分类和 `prefab_key`。
- `recipes.csv`：稳定 `RecipeId`、设备类型和生产时间。
- `recipe_inputs.csv`：配方输入。
- `recipe_outputs.csv`：配方输出。

保存 CSV 后，Unity 会自动重建生成资产。也可以执行菜单
`Factory > Rebuild Static Database` 手动重建。

物品 Prefab 放在 `Assets/Prefabs/Items` 下，并使用与 `prefab_key`
一致的文件名。匹配时忽略大小写、空格、下划线和连字符，例如
`iron_ore` 可以匹配 `IronOre.prefab`。物品 Prefab 只能包含表现组件，
不需要 `ItemAuthoring` 或其他 `MonoBehaviour`。

当前生产逻辑支持每条配方零或一个输入，并且必须恰好有一个输出。
ID `0` 保留为无效值；已经发布的 ID 不应修改或复用。
