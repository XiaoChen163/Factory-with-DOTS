# 物品能力与领域内容库

更新日期：2026-11-11。状态：定义、内容编辑、校验和 Baking 接入；战斗执行与动画播放留待后续任务。

## 保存结构

```text
Assets/Data/FactoryDatabase.asset         总入口，保留原 GUID
  ├─ ItemDatabase.asset                  所有物品记录
  │    └─ ItemCapabilityDefinition 子资产
  │         ├─ GunCapabilityDefinition
  │         ├─ WeaponPresentationCapabilityDefinition
  │         └─ AmmunitionCapabilityDefinition
  ├─ RecipeDatabase.asset                配方与内嵌原料/产物
  └─ BuildingDatabase.asset              建筑、端口、碰撞及加工类型
```

物品仍是 `FactoryItemTableRow` 记录，通过 `capabilities` 数组引用能力子资产。每个物品可以组合多种能力，每种类型最多一个；能力实例归该物品私有，禁止多个物品共享同一实例。管理器自动创建、保存和清理子资产，不为每个物品或能力创建独立 `.asset` 文件。

总入口不重复保存正式数据。隐藏的 inline 数组仅服务旧数据迁移和临时测试/编辑快照，正式总入口中为空。已有 `items / recipes / buildings / machineTypes` 访问器从对应内容库读取，因此现有模拟与表现入口继续共用同一套定义。

`FactoryDatabaseLibraryMigration.Run` 将旧总入口的内嵌数据迁入三个内容库；已迁移时仅校验，不覆盖现有库。检测到部分配置或已有目标文件时停止，避免覆盖手动编辑。迁移保持既有 ID、Key、顺序、资源引用和数值。

## 最小能力定义

| 能力 | 字段 | 约束 |
|---|---|---|
| 枪械 | `fireMode` | 半自动或自动；没有连发组模式 |
| 枪械 | `roundsPerMinute` | 有限正数，单位发/分钟 |
| 枪械 | `magazineCapacity` | 1～65535 |
| 枪械 | `reloadSeconds / rangeMeters` | 有限正数，分别为秒、米 |
| 枪械 | `ammunitionItem` | 一种物品的 `ItemId`，目标必须具有弹药能力 |
| 弹药 | `baseDamage` | 非负 int 整数；定义/内容管理器/Blob 使用相同单位，零基础伤害不产生伤害请求 |
| 武器表现 | `firstPersonPrefab / thirdPersonPrefab` | 第一/第三人称模型或 Prefab，允许留空 |
| 武器表现 | `firstPersonAnimations / thirdPersonAnimations` | RuntimeAnimatorController 引用，支持普通及 Override Controller；配置时要求相应模型存在 |

模型和控制器引用必须为已保存的项目资产。库存/物流使用的物品模型继续独立于手持模型：库存模型沿用静态 FBX 校验，手持表现允许动画模型与 Prefab。

Category 只用于分类，不决定物品能力。本次不自动给原有工业物品附加能力，也不引入配件系统、通用属性键值表或标签行为。

## 内容管理器与保存

物品详情提供能力列表、添加菜单、展开字段和移除按钮；枪械弹药下拉框只列出已拥有弹药组件的物品。添加后未配置兼容弹药可暂存于草稿，但无法保存为正式定义。

能力参数进入可序列化草稿，支持 Undo/Redo、域重载和放弃。预览或编辑不会修改正式子资产。保存前校验整个候选集合，并检查总入口、三个内容库和能力子资产是否被其他操作修改。

保存前检查全部文件可写，再提交内容库及子资产。普通参数修改复用已有子资产，保留 GUID/本地文件 ID；移除组件、删除物品会清理孤立能力。修改物品 ID 会同步枪械弹药引用；手动改变 ID 仍属于身份变更，可能重建该物品的能力子资产，不提供存档兼容保证。

被配方或枪械引用的物品不能直接删除。移除被引用的弹药组件会使相关枪械校验失败，必须调整关联配置后才能保存。

## Baking 与状态边界

Baker 显式依赖三个内容库及能力子资产。Baking 汇总为原有 `FactoryDatabaseBlob`，额外提供：

- `GunsByItemId`：按物品 ID 查询枪械定义。
- `AmmunitionByItemId`：按物品 ID 查询弹药定义。

空槽的 `Item.IsValid == false`。表现资源保留在托管目录，通过 `FactoryPresentationCatalog.GetWeaponPresentation(ItemId)` 查询，不进入 Burst Blob。

这些仅是只读规格。通用整数生命与伤害结算已由 [A01 生命系统](Health-and-Damage.md) 提供；当前弹数、开火冷却、换弹进度和射击命中仍待 A04 接入，没有新增射击输入、扣弹或 Animator 播放逻辑。待机/开火/换弹/移动等 Animator 动作契约也由后续表现接入任务确定。

修改工具或测试时，不能直接 `Instantiate` 总入口后编辑，因为引用的内容库仍会共享。使用 `FactoryDatabaseEditor.CreateSnapshot` 获取独立的扁平候选和能力克隆，结束后 `DestroyImmediate` 快照；`FactoryContentDraft` 已遵守此边界。

## 验证

迁移后逐项核对原有 9 种物品、28 种建筑、9 个配方、5 种加工类型，与迁移前数据一致。总入口 GUID 保持不变，正式库未自动附加组件，无测试子资产残留。新增回归位于 `Assets/Tests/Data/FactoryItemCapabilityTests.cs`，覆盖草稿、子资产生命周期、引用校验、资源持久化和 Blob/表现目录查询。

Unity `6000.3.19f1` 验证记录：

| 检查 | 结果 | 本地记录 |
|---|---|---|
| 编译与领域内容库迁移 | 通过，退出码 0 | `Logs/ItemCapabilities-Migration.log` |
| 完整 Edit Mode `Factory.Tests` | 353 / 353 通过 | `Logs/ItemCapabilities-EditMode-2.log`、同名 `.xml` |
| 完整 Play Mode `Factory.PlayModeTests` | 37 / 37 通过 | `Logs/ItemCapabilities-PlayMode.log`、同名 `.xml` |
| 数据与 GUID 核对 | 原有定义完全一致 | `Logs/ItemCapabilities-DataAudit.txt` |

没有运行压力测试或新增战斗执行流程。测试使用的临时枪械/弹药能力已清理，正式库存、配方和建筑内容保持原样。
