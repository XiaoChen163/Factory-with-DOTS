# Factory 内容管理器

更新日期：2026-11-11。

Unity 菜单 **Factory > 内容管理器** 提供建筑、物品、配方三个页签，支持 `.factory.json`、FBX、材质适配和定义数据驱动的 DOTS Physics 建筑碰撞。

**唯一正式数据源是 `Assets/Data/FactoryDatabase.asset`（ScriptableObject）。** 资产及 `.meta` 已移出 Generated 目录，GUID 和场景引用保持不变。该资产不是可覆盖的生成缓存；CSV 源表、解析器和重建入口已移除。

```text
内容管理器 → 强类型内存草稿 → 整组校验 → FactoryDatabaseAsset → Baking → ECS Blob
```

1. 选择页签，在左侧搜索并选择内容，右侧编辑详情。数组顺序只影响展示，不重新分配数字 ID。
2. 名称等文本立即进入草稿并显示未保存状态；ID、堆叠、生产时间及建筑数值在提交输入后更新，避免输入中途被校验回退。撤销/重做仅修改草稿。切换页签或搜索不会丢失修改。
3. 点击“保存”。校验全部候选数据后写入同一个数据资产，保留原有 GUID；失败时保留草稿，不提交无效内容。
4. “放弃 / 重新读取”重新读取当前资产。关闭窗口时，未保存修改使用 Unity 的保存、放弃、取消提示。资源文件如被外部工具修改，需要先让 Unity 完成导入再重新读取。

中文、逗号、引号和换行直接保存在字符串字段中。草稿独立于正式资产；域重载后保留序列化草稿。保存检查加载时的内存资产快照和磁盘字节，其他窗口保存、Inspector 修改或外部改写都会拒绝过期草稿，避免覆盖新数据。

Play Mode 及进入 Play Mode 期间禁止修改和保存；退出后可继续处理草稿。**Factory > Validate Static Database** 只校验现有资产，不重新生成或覆盖内容。构建前也执行资产校验；Baking 继续执行运行时数据校验。

本轮只迁移保存来源，保留现有物品、配方、建筑及加工类型结构，不引入能力组件或每物品独立资产。

## 建筑

- 左侧只显示图标、名称和只读 Key；搜索支持 Key 和名称。选择建筑后在右侧查看详情。
- 顶部 Key 输入框供“新增”使用，留空会自动分配。建筑创建后 Key 只读；JSON 导入按文件中的 `building.key` 查找或新增同一草稿。
- 名称直接编辑建筑定义的 `displayName`；现有名称已保留，尚无运行时多语言系统。详情可选择 FBX 模型和图标，修改菜单顺序，并按行为配置加工类型、仓储容量/槽数、传送带 Tier/速度或坡道高度。
- “烘焙变化的图标并绑定”复用现有图标烘焙器；先保存草稿，再烘焙。烘焙后自动将生成图标绑定到数据资产并重新加载草稿；显式选择的其他 Sprite 保留。
- 删除建筑只移除其定义，包括内嵌端口、碰撞体和行为参数。FBX、材质和纹理资产会保留。

### 占格与端口

右侧“占格与端口（只读）”显示尺寸、占用格、物流箭头及端口说明，不提供尺寸、占格、端口或产物槽编辑控件。需要调整时，在 Blender 插件中修改后重新导出并导入 `.factory.json`。

蓝色表示占用，X 向右、Z 向上，位图第一行始终对应 Z=0。每格坐标位于左上角，放大的绿色箭头表示输入、橙色箭头表示输出，产物槽号位于右下角。坐标不裁剪、不重新居中，校验仍复用 `FactoryBuildingFootprintUtility`。

单格传送带、垂直传送带、地基和坡道地基隐藏整个占格区域。合流器、分流器及其他建筑继续展示，便于查看多端口关系。Processor 输出口保留从 1 开始的产物槽；其他行为不保存 `number/output_slot`。产物槽覆盖和配方兼容性仍在整组候选校验中检查。

### 统一模型导入器

建筑页和物品页顶部的“导入模型…”打开同一个窗口，可切换“建筑 / 物品”模式。

**建筑模式**：

1. 选择 Blender 导出的 `.factory.json`（`schema_version = 1`）。窗口读取 `building.key`，并在 JSON 同目录按 `model_file` 寻找配对 FBX；也可手动指定外部或项目内 FBX。
2. JSON 必须同时提供占格、`ports` 和 `colliders`。空数组明确表示没有端口或碰撞；省略部分会阻止应用。
3. 新建筑选择行为；Processor 同时选择加工类型。已有建筑保留名称、行为、图标和游戏属性。
4. 点击“应用到内容草稿”，同时写入模型引用、占格、端口和碰撞体。一键撤销恢复整个导入。已有 Key 会覆盖对应的空间数据和模型。
5. 回到管理器检查并点击“保存”，统一提交建筑及其内嵌空间、表现和行为数据。

**物品模式**：

1. 输入物品 Key，选择 FBX；首次选择文件时会根据文件名填入空 Key。
2. 点击“应用到内容草稿”，只新增或更新物品定义，不修改建筑、端口或碰撞体。
3. 新物品自动在当前最大 ID 后分配非零 ID，默认最大堆叠 64、Component 分类；已有物品保留 ID、名称、堆叠、分类和图标。
4. 保存草稿后可烘焙默认图标。物品不生成碰撞体或刚体。

模型文件与纹理导入立即落盘，取消或撤销内容草稿不会删除已导入的资产。建筑和物品均不生成包装 Prefab，也不维护逐模型配置资产。

### FBX 与材质

1. 导入 JSON 后，建筑详情会在 JSON 所在目录寻找 `model_file`。点击“导入配对 FBX”，或手动选择外部 FBX；也可在“FBX 模型”栏选择项目内的 FBX。选择后直接更新模型引用，无需生成包装 Prefab。
2. 外部文件复制到 `Assets/Art/Buildings/<key>/`，同目录及子目录中的图片纹理一并保留相对路径导入。项目内 FBX 直接引用。再次导入外部模型会覆盖此建筑已选中的模型文件，保留 `.meta`、材质映射和原资产路径；不会删除旧纹理。
3. 打开“材质映射与 URP 适配”。导入时优先在 `Assets/Art/Materials` 及其子目录按源材质名称精确匹配，多个同名候选需手动选择。可绑定已有 `.mat`，或创建 URP/Lit 材质。映射保存到 FBX 的 ModelImporter；已绑定材质不会在再次导入时被重建或覆盖。
4. 点击“调整此材质”，使用 Unity 原生 Inspector 设置材质。法线贴图需设置为 Normal map；金属度用 R 通道，光滑度用 A 通道。粗糙度需要反相及正确通道，工具不转换纹理或复杂 Blender 节点。
5. 在“碰撞体”中新增、删除或修改碰撞体，再点击“保存”。也可以通过 JSON 批量更新碰撞体。

FBX、纹理导入和材质操作立即落盘；草稿撤销与放弃只恢复内容引用和参数。FBX 更新保留资源 GUID；碰撞体 JSON 导入和手动编辑均直接进入强类型草稿，保存后自动更新烘焙数据。

建筑与物品表的 `model_path` 只允许 FBX。原有 23 个几何体建筑模型、6 个物品模型及 4 个组合图标模型已通过本机 Blender 4.1 转为 FBX；原模型 Prefab 已移除，尺寸与材质映射保持一致。端口提示和玩家等其他用途的 Prefab 不属于模型导入管线。

### 碰撞体与传送带子物体

碰撞体保存在建筑定义的 `colliders` 数组中，每个形状的 `name` 在同一建筑内唯一。位置、尺寸和四元数均为 Unity 建筑局部坐标。界面旋转以角度编辑，资产保存四元数以保留 Blender 导出精度。

支持 Box、Sphere、Capsule 和 Cylinder。Cylinder 使用 Unity Physics 的 32 边 Y 轴圆柱。烘焙时由建筑目录根据定义参数生成共享 compound blob，按建筑定义保存；同一个 FBX 可以被不同建筑引用并使用不同碰撞体。建造时挂到建筑逻辑根实体，随旋转、楼层和拆除生效。碰撞数组为空时不附加定义碰撞体，地基、坡道和垂直传送带原有专用碰撞机制继续生效。

普通传送带模型需包含以下唯一命名的网格子物体（允许嵌套分组）：

- `Connection Edge East`
- `Connection Edge North`
- `Connection Edge West`
- `Connection Edge South`
- `Direction Triangle`

导入时检查名称缺失、重复及网格组件。烘焙按名称取得实体，运行时控制边缘显隐和中央三角旋转。沿用现有坐标约定：四边对应建筑局部东、北、西、南；三角网格默认朝建筑局部 +X。烘焙记录三角的初始旋转及父级坐标轴，运行时和预览均绕建筑局部 Y 轴旋转，兼容 FBX 导入的嵌套分组与轴向变换。

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

- 列表搜索 Key 和名称（`name_key`），详情包含 ID、Key、名称、最大堆叠、分类、FBX、材质映射和图标。
- 模型字段直接引用项目内 FBX；外部文件导入到 `Assets/Art/Items/<key>/`。模型只允许 Transform、MeshFilter、MeshRenderer，不允许碰撞体、刚体和脚本。
- 图标字段直接选择项目 Sprite。烘焙器从物品定义读取模型，默认生成到 `Assets/Art/Icons/Items`，按物品 Key 区分图像，独立于模型文件名。图标为空时可通过烘焙自动绑定。
- 多个物品允许共享同一个 FBX；烘焙按物品 ID 创建独立实体模板，网格与材质资源共享，对象池按物品身份生成和回收。反复烘焙会替换旧模板。
- 改名 Key 会同时更新配方条目引用，一次撤销即可恢复。被引用的物品不能删除；降低最大堆叠会列出超限配方。

### 重建占位 FBX

`Tools/Blender/Fixtures/PlaceholderModels.json` 保存原几何体网格、层级、变换和材质槽。运行：

```powershell
& 'D:/Application/Blender 4.1/blender.exe' --background --factory-startup --python Tools/Blender/generate_placeholder_fbx.py
```

之后在 Unity 执行 `Factory > Models > Import Generated Placeholder FBX`，应用现有材质映射并核对几何边界。导出保留 Empty 分组，使用 `bake_space_transform=False`，避免带层级模型发生重复轴转换。所有模型文件已提交，无需在正常导入时运行 Blender。

## 配方

- 搜索支持 Key、加工类型。列表显示原料/产物图标及数量、Key、加工类型和生产时间。
- 可新增、删除、修改 Key、生产时间和加工类型。修改配方 Key 会同步两侧条目；删除配方同时删除内嵌的原料和产物条目。详情内可新增并选择加工类型，直接写入资产的 `machineTypes`。
- “添加原料 / 添加产物”或条目中的物品按钮打开搜索弹窗，按物品名称或 Key 筛选，点击或键盘选择条目。选择同侧已有物品时合并数量，超出堆叠上限则保留原草稿并显示错误。
- 数量必须为正且不超过最大堆叠。产物槽位从 1 开始，用上下箭头调整顺序，按该顺序保存到配方的 `outputs` 数组。
- 可保留零原料的采矿配方；至少需要一个产物，生产时间必须为有限正数（小数用 `.`）。详情复用整组候选校验，检查对应建筑是否有覆盖各产物槽位的输出口。

相关实现：`FactoryContentDraft` 各分部（强类型草稿、编辑规则、Undo 和保存）、`FactoryBuildingJson`（Editor JSON DTO）、`FactoryDatabaseEditor`（资产加载、候选校验和保存）、`FactoryDatabaseValidation`（定义与引用校验）。

## ID 与资源引用

现有数字 ID 保留。新增物品、建筑、加工类型和配方使用当前同类最大 ID + 1；修改 Key 或重排列表不重算 ID。物品 Key 或 ID 修改会同步配方原料和产物的关联，仍被配方引用的物品不能删除。该规则不等同于跨版本存档迁移，删除内容和手动修改 ID 仍需谨慎处理。

模型和图标直接引用 Unity 对象，资产移动后引用由 GUID 维持。已有 `modelPath`、`visualPrefabPath`、`iconKey` 保留为兼容元数据，不作为第二个资源来源。界面从对象引用获取当前路径。图标可选择任意项目 Sprite，不再要求通过目录中的文件名解析。
