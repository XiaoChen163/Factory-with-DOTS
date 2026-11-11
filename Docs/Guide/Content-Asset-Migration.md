# 内容数据源迁移记录

日期：2026-11-11。范围：将内容管理器从 CSV 保存层迁移到正式 ScriptableObject，不实现物品能力组件，不将 A01 标记为完成。

## 最终数据链路

```text
Factory > 内容管理器
    → 强类型内存草稿（Undo / Redo / 放弃）
    → 整组校验与过期草稿检查
    → Assets/Data/FactoryDatabase.asset
    → Baking
    → 只读 ECS Blob 与表现目录
```

`FactoryDatabaseAsset` 从生成缓存转为唯一正式编辑源，资产及 `.meta` 一起移至 `Assets/Data/FactoryDatabase.asset`，保留原 GUID 和既有引用。已有数组、字段和数值不变，不拆分每物品独立资产。`Generated` 仍保存其他生成资源；正式数据库不能视为可删除重建的缓存。

移除 `Assets/Data/FactoryTables`、`FactoryCsvTable`、`FactoryBuildingColliderCsv` 和旧 CSV 导入器。`FactoryDatabaseEditor` 负责资产加载、校验和保存，旧重建菜单替换为只读的 **Factory > Validate Static Database**；构建前同样校验正式资产。

模型导入、材质工具、美术批处理和图标烘焙全部切换到资产。模型与图标采用 Unity 对象引用；保留原路径/图标 Key 字段作为兼容元数据。图标烘焙更新原有生成绑定，保留显式选择的其他 Sprite。JSON 继续作为 Blender 的空间/碰撞数据交换格式，不承担正式数据源职责。

## 数据保留核对

删除 CSV 前，逐项对比旧源表与现有 ScriptableObject，核对结果为零差异：

| 内容 | 数量 | 核对范围 |
|---|---:|---|
| 物品 | 9 | ID、Key、名称、堆叠、分类、模型引用、图标 Key |
| 建筑 | 28 | ID 保留；Key、行为、名称、空间、模型/图标、行为属性、端口、碰撞体 |
| 配方 | 9 | 加工类型、加工时间、原料/产物类型与数量、顺序 |
| 加工类型 | 5 | Key 与已解析 ID 关联 |

现有正式数据资产无需重写，测试结束后与迁移前资产保持一致。新增内容按同类当前最大 ID + 1 分配，重排数组或修改 Key 不重新生成 ID。此规则不提供跨版本存档迁移；删除或手动修改 ID 仍需单独处理兼容性。

## 验证结果

Unity Editor：`6000.3.19f1`，使用项目 `unity-launch` 的受保护批处理入口，未执行性能压力测试。

| 验证 | 结果 | 本地记录 |
|---|---|---|
| 编译与资源导入 | 通过，退出码 0 | `Logs/ContentAsset-Compile.log` |
| 完整 Edit Mode `Factory.Tests` | 339 / 339 通过 | `Logs/ContentAsset-EditMode-2.log`、同名 `.xml` |
| 完整 Play Mode `Factory.PlayModeTests` | 37 / 37 通过 | `Logs/ContentAsset-PlayMode.log`、同名 `.xml` |
| CSV 与资产迁移前核对 | 零差异 | `Logs/ContentAsset-MigrationAudit.txt` |
| 数据库移出 Generated 后编译、导入与资产校验 | 通过，退出码 0；资产和 `.meta` 字节保持一致 | `Logs/DatabaseAsset-Move-Validation.log` |

覆盖保存重开、Unicode 字符串、资源引用/GUID、Undo/Redo、放弃、草稿序列化、无效内容拒绝保存、多窗口冲突、Inspector/磁盘修改冲突、锁文件失败与重试、数字 ID 保持、模型导入及 Baking，并回归现有建造、生产、运输、UI 和 Play Mode 行为。

旧测试中对 CSV 文件的断言已替换为资产断言；依赖已移除正式样例物品/机器的测试改为使用现有生产内容或独立测试夹具，没有把测试样例重新加入正式数据库。

操作说明见 [Factory 内容管理器](Factory-Content-Manager.md)，定义说明见 [建筑与生产内容定义](Building-Data-Tables.md)。
