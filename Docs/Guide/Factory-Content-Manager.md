# Factory 内容管理器

Unity 菜单 **Factory > 内容管理器** 提供建筑、物品、配方三个页签，支持 `.factory.json`、FBX、材质适配、FBX 直接引用及 CSV 驱动的 DOTS Physics 建筑碰撞。

1. 选择页签，在左侧搜索并选择内容，右侧编辑详情。行序沿用源表，建筑数字 ID 不会因界面显示而重排。
2. 输入立即进入内存草稿，并显示未保存状态；撤销/重做仅修改草稿。切换页签或搜索不会丢失修改。
3. 点击“保存并重建”。先按现有导入规则解析全部草稿并校验整组候选数据，再写入有修改的 CSV，最后生成一次 `FactoryDatabase.asset`。
4. “放弃修改 / 重新读取”会在确认后丢弃草稿，重新读取磁盘。关闭窗口时，未保存修改使用 Unity 的保存、放弃、取消提示。

逗号、双引号、换行及中文可直接作为单元格内容。未修改记录、注释、额外列、其他语言名称和表内行序会保留。源表已被其他操作修改时，保存会报出文件路径；重新读取后再编辑，避免旧草稿覆盖新内容。

校验失败时保留草稿，不写入 CSV 或有效数据库。多表写入期间暂停资源导入；写入失败时恢复本次涉及文件的原始字节，并报告具体文件。如果磁盘本身不允许恢复，错误会同时列出恢复失败的路径。

Play Mode 及进入 Play Mode 期间禁用字段编辑、保存与数据库重建；退出后可继续处理草稿。现有 **Factory > Rebuild Static Database** 菜单保留。

## 建筑

- 左侧只显示图标、名称和只读 Key；搜索支持 Key 和名称。选择建筑后在右侧查看详情。
- 顶部 Key 输入框供“新增”使用，留空会自动分配。建筑创建后 Key 只读；JSON 导入按文件中的 `building.key` 查找或新增同一草稿。
- 名称默认编辑 `zh-CN`，保留其他语言。详情可选择 FBX 模型和图标，修改菜单顺序，并按行为配置加工类型、仓储容量/槽数、传送带 Tier/速度或坡道高度。
- “烘焙变化的图标并绑定”复用现有图标烘焙器；先保存草稿，再烘焙。烘焙后绑定进入草稿，确认后保存到 `building_presentation.csv`。
- 删除建筑只移除该 Key 的主表、名称、表现、端口、碰撞体及行为扩展行。FBX、材质、纹理和既有程序生成的 Prefab 会保留。

### 占格与端口

右侧“占格与端口（只读）”显示尺寸、占用格、物流箭头及端口说明，不提供尺寸、占格、端口或产物槽编辑控件。需要调整时，在 Blender 插件中修改后重新导出并导入 `.factory.json`，或修改对应 CSV 后点击“放弃 / 重新读取”。

蓝色表示占用，X 向右、Z 向上，位图第一行始终对应 Z=0。每格坐标位于左上角，放大的绿色箭头表示输入、橙色箭头表示输出，产物槽号位于右下角。坐标不裁剪、不重新居中，校验仍复用 `FactoryBuildingFootprintUtility`。

单格传送带、垂直传送带、地基和坡道地基隐藏整个占格区域。合流器、分流器及其他建筑继续展示，便于查看多端口关系。Processor 输出口保留从 1 开始的产物槽；其他行为不保存 `number/output_slot`。产物槽覆盖和配方兼容性仍在整组候选校验中检查。

### 导入 `.factory.json`

1. 点击建筑页“导入 .factory.json”，选择单个文件。当前支持 `schema_version = 1`。
2. 在独立的“导入 Factory 建筑”窗口中查看 Key、模型配对文件名、占格尺寸、端口数量和碰撞代理预览。新建筑默认勾选文件中提供的部分；已有建筑默认全部不覆盖，明确勾选本次需要更新的占格、端口或碰撞代理。
3. 点击“应用到草稿”后关闭导入窗口，回到内容管理器继续操作；取消或关闭导入窗口不修改草稿。缺失部分保持不变；存在的空 `ports` / `colliders` 数组会在勾选时清空对应部分。名称、图标、行为与游戏属性保持不变。
4. 为新建筑补齐行为、FBX 模型及行为所需字段；缺失的占格通过 JSON 导入或 CSV 提供，然后“保存并重建”。补齐前只保留未保存草稿，不会向 CSV 写入无效建筑。

导入和名称修改均支持草稿撤销/重做。再次导入时，未勾选覆盖的数据保持原样。

模型引用保存在 `buildings.csv` 的 `model_path`；碰撞体保存在 `building_colliders.csv`，不再创建逐建筑的 Editor 配置资产。JSON 与 FBX 的配对路径只用于当前编辑会话，重新读取草稿后可重新导入 JSON 或手动选择 FBX。JSON 碰撞数组缺失表示不更新，勾选导入空数组表示删除该建筑的全部 CSV 碰撞记录。

### FBX 与材质

1. 导入 JSON 后，建筑详情会在 JSON 所在目录寻找 `model_file`。点击“导入配对 FBX”，或手动选择外部 FBX；也可在“FBX 模型”栏选择项目内的 FBX。选择后直接更新模型引用，无需生成包装 Prefab。
2. 外部文件复制到 `Assets/Art/Buildings/<key>/`，同目录及子目录中的图片纹理一并保留相对路径导入。项目内 FBX 直接引用。再次导入外部模型会覆盖此建筑已选中的模型文件，保留 `.meta`、材质映射和原资产路径；不会删除旧纹理。
3. 打开“材质映射与 URP 适配”。导入时优先在 `Assets/Art/Materials/Buildings` 及其子目录按源材质名称精确匹配，多个同名候选需手动选择。可绑定已有 `.mat`，或创建 URP/Lit 材质。映射保存到 FBX 的 ModelImporter；已绑定材质不会在再次导入时被重建或覆盖。
4. 点击“调整此材质”，使用 Unity 原生 Inspector 设置材质。法线贴图需设置为 Normal map；金属度用 R 通道，光滑度用 A 通道。粗糙度需要反相及正确通道，工具不转换纹理或复杂 Blender 节点。
5. 在“碰撞体（building_colliders.csv）”中新增、删除或修改碰撞体，再点击“保存并重建”。也可以通过 JSON 批量更新碰撞体。

FBX、纹理导入和材质操作立即落盘；草稿撤销与放弃只恢复 CSV 引用和参数。FBX 更新保留资源 GUID；碰撞体 JSON 导入和手动编辑均直接进入 CSV 草稿，保存后自动更新烘焙数据。

已有程序生成的传送带、地基等仍允许在 `model_path` 引用 `.prefab`，直到提供 FBX 替换。矿机、熔炉、仓库和 testMachine 已改为 FBX 直接引用，旧包装资产和逐建筑导入配置已移除。

### 碰撞体与传送带子物体

碰撞表一行一个形状，以 `building_key` 关联主表，`collider_key` 在同一建筑内唯一。位置、尺寸和四元数均为 Unity 建筑局部坐标。界面旋转以角度编辑，CSV 保存四元数以保留 Blender 导出精度。

支持 Box、Sphere、Capsule 和 Cylinder。Cylinder 使用 Unity Physics 的 32 边 Y 轴圆柱。烘焙时由建筑目录根据 CSV 参数生成共享 compound blob，按建筑定义保存；同一个 FBX 可以被不同建筑引用并使用不同碰撞体。建造时挂到建筑逻辑根实体，随旋转、楼层和拆除生效。没有碰撞记录时不附加 CSV 碰撞体，地基、坡道和垂直传送带原有专用碰撞机制继续生效。

普通传送带模型需包含以下唯一命名的网格子物体（允许嵌套分组）：

- `Connection Edge East`
- `Connection Edge North`
- `Connection Edge West`
- `Connection Edge South`
- `Direction Triangle`

导入时检查名称缺失、重复及网格组件。烘焙按名称取得实体，运行时控制边缘显隐和中央三角旋转。沿用现有坐标约定：四边对应建筑局部东、北、西、南；三角网格默认朝 +X，绕其局部 Y 轴旋转，父级坐标轴需与建筑坐标轴对齐。

### Blender 4.1 导出约定

Blender 插件负责 JSON；FBX 可使用项目脚本 [export_factory_model.py](../../Tools/Blender/export_factory_model.py) 同时导出。示例：

```powershell
& 'D:/Application/Blender 4.1/blender.exe' --background --factory-startup `
  --python Tools/Blender/export_factory_model.py -- `
  'D:/UnityProject/Factory-with-DOTS-BuildingCreater/blender-test/testMachine.blend' `
  'D:/UnityProject/Factory-with-DOTS/Logs/stage4-export' `
  'D:/UnityProject/Factory-with-DOTS-BuildingCreater'
```

脚本不保存或修改原 `.blend`；只导出非碰撞代理的 Mesh，应用求值修改器，排除相机、灯光和动画，图片随 FBX 复制。固定预设为 `global_scale=1`、`apply_unit_scale=True`、`FBX_SCALE_UNITS`、Forward `-Z`、Up `Y`、`bake_space_transform=True`，忽略 Blender 单位显示缩放。Unity 固定为 Scale Factor 1、Use File Scale、Bake Axis Conversion，禁用动画/相机/灯光/自动碰撞，保留源材质供映射。原点必须已在 Blender 占格底面中心，工具不会根据模型包围盒重新居中。

`Tools/Blender/Fixtures/testMachine` 保存 Blender 4.1.1 导出的 FBX、插件 JSON 和求值几何记录。非对称柱体的 Unity 中心为 `(0.6, 1.939839, 0.75)`、高度 2，底座底面为 Y=0；验收测试以此确认轴向、单位及原点。

## 物品

- 搜索支持 Key 和名称（`name_key`），列表显示图标、Key、名称。详情包含 ID、Key、名称、最大堆叠、分类、Prefab、图标。
- “新增”分配当前空闲的非零 ID，并创建未保存草稿；选择有效 Prefab 后再保存。ID 可手动编辑，临时空值或重复值会触发校验提示，并阻止保存。
- Prefab 使用 `Assets/Prefabs/Items`，图标使用 `Assets/Art/Icons/Items`，沿用导入器按文件名规范化匹配的规则。选择图标时自动补充可选 `icon_key` 列；留空使用 Prefab Key 的默认图标。资产文件本身不会移动或删除。
- 改名 Key 会同时更新两张配方条目表的引用，一次撤销即可恢复。仍被引用的物品不能删除，错误会列出配方及原料/产物位置。
- 降低最大堆叠会列出超限配方，保留原数量；先调整配方再保存。名称继续沿用现有 `name_key` 语义。

## 配方

- 搜索支持 Key、加工类型。列表显示原料/产物图标及数量、Key、加工类型和生产时间。
- 可新增、删除、修改 Key、生产时间和加工类型。修改配方 Key 会同步两侧条目；删除配方只删除相关 CSV 行。详情内可新增并选择加工类型，仍写入 `machine_types.csv`。
- “添加原料 / 添加产物”或条目中的物品按钮打开搜索弹窗，按物品名称或 Key 筛选，点击或键盘选择条目。选择同侧已有物品时合并数量，超出堆叠上限则保留原草稿并显示错误。
- 数量必须为正且不超过最大堆叠。产物槽位从 1 开始，用上下箭头调整顺序，按该顺序写入 `recipe_outputs.csv`。
- 可保留零原料的采矿配方；至少需要一个产物，生产时间必须为有限正数（小数用 `.`）。详情复用整组候选校验，检查对应建筑是否有覆盖各产物槽位的输出口。

相关实现：`FactoryCsvTable`（CSV 文档）、`FactoryContentDraft` 各分部（草稿、编辑规则与保存）、`FactoryBuildingJson`（Editor JSON DTO）、`FactoryBuildingColliderCsv`（碰撞表读写）、`FactoryDatabaseCsvImporter.ReadCandidate/Generate`（候选读取、校验与生成）。
