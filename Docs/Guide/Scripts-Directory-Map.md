# Scripts 源码目录导航

更新日期：2026-11-11。

源码保留“技术层 / 业务领域”的两级组织方式：先区分 ECS 数据、系统、Authoring、表现和编辑器，再在各层按领域查找。尚未实现的 Combat、Bases、Power 等领域不提前创建空目录。运行时与编辑器程序集边界保持不变。

## 1. 快速定位

| 要找的逻辑 | 主要目录 / 入口 |
|---|---|
| 网格、地基、坡道、碰撞 | `ECS/Data/World`、`ECS/Systems/World`、`ECS/Authoring/World` |
| 建造、拆除、占用索引 | `ECS/Systems/Construction`；入口 `GridBuildCommandSystem` |
| 建造射线、预览、传送带铺设 | `ECS/Presentation/Construction`；入口 `EcsGridInteractionController`，partial 文件集中存放 |
| 传送带推进、连接、仲裁、端口 | `ECS/Data/Transport`、`ECS/Systems/Transport` |
| 配方加工和配方选择 | `ECS/Systems/Production` |
| 人工物资转移、物品池初始化 | `ECS/Systems/Inventory` |
| 玩家移动、胶囊碰撞、玩家初始化 | `ECS/Data/Player`、`ECS/Systems/Player` |
| 第一人称输入采集、相机、角色表现同步 | `ECS/Presentation/Player`；输入模式仲裁在 `ECS/Presentation/Input` |
| UI/玩家命令进入 ECS 与结果导出 | `ECS/Data/Commands`、`ECS/Systems/Commands`、`ECS/Presentation/Bridge` |
| UI 快照导出、物品插值、传送带外观刷新 | `ECS/Systems/Presentation` |
| 窗口、控件、拖拽 | `ECS/Presentation/UI` |
| 图标/名称等表现目录 | `ECS/Presentation/Content` |
| 固定模拟频率 | `ECS/Systems/Core/Stage3FixedRateSystem.cs` |
| 运行时静态数据库及 Baking | `ECS/Data/Content`、`ECS/Authoring/Content` |
| 数据库定义、校验 | `DataBase/Definitions`、`DataBase/Validation` |
| 内容管理器及其草稿/partial 文件 | `DataBase/Editor/ContentManager` |
| 数据资产保存/构建校验、建筑模型导入、图标烘焙 | `DataBase/Editor/Import`、`Buildings`、`Icons` |
| UI/建筑资源生成、玩家预制体安装 | `DataBase/Editor/Setup`、`ECS/Editor/Player` |
| 性能场景、指标采集及批处理入口 | `ECS/Performance`、`DataBase/Editor/Performance` |
| 原型诊断 HUD | `ECS/Presentation/Diagnostics`，不作为正式 UI 入口 |

路径均相对 `Assets/Scripts`。测试仍位于 `Assets/Tests`，不随运行时代码迁入 Scripts。

## 2. 当前结构

```text
Assets/Scripts/
  Factory.Runtime.asmdef
  DataBase/
    Definitions/
    Validation/
    Editor/
      Factory.Editor.asmdef
      ContentManager/
      Import/
      Buildings/
      Icons/
      Setup/
      Performance/
  ECS/
    Data/
      World/ Construction/ Transport/ Player/ Commands/ Content/
    Systems/
      Core/ World/ Construction/ Transport/ Production/ Inventory/
      Player/ Commands/ Presentation/
    Authoring/
      World/ Construction/ Transport/ Content/
    Presentation/
      Player/ Input/ Construction/ Bridge/ Content/ UI/ Diagnostics/
    Editor/
      Factory.Player.Editor.asmdef
      Player/
    Performance/
```

同一行的多个目录是同级目录，不是嵌套路径。系统属于 `Systems/Presentation` 不代表它是 MonoBehaviour：这里存放的是执行表现采集、导出或刷新工作的 ECS 系统；`ECS/Presentation` 存放 GameObject/UI 接入与托管桥接。

## 3. 保留的跨领域文件

此次整理仅移动文件，不拆类型、不修改命名空间或玩法规则。部分历史组件文件同时承载多个领域，按主要职责归档：

- `Data/Construction/EcsBuildingComponents.cs`：建筑目录、建造命令，同时保留加工、仓储与库存槽组件。查找 `ItemProcessState`、`StorageState`、`InventorySlot` 时仍进入此文件。
- `Data/Transport/EcsFactoryComponents.cs`：传送带组件，同时保留物品、物品池、视觉状态和模拟统计。
- `Data/Player/EcsPlayerComponents.cs`：玩家身份与库存，同时包含容器身份等公共数据。
- `Systems/Transport/RampTopologyConnectionSystem.cs`：坡道与运输连接的桥接；坡道空间注册和碰撞位于 `Systems/World`。
- `Systems/World/SurfaceChunkMeshBuildSystem.cs`：地基表面的分块网格生成，和表面注册放在一起。

后续增加相关玩法时，可以结合实际依赖将混合组件拆分到独立文件；不要仅为目录整洁提前扩大本次改动。

## 4. 程序集与 Meta 规则

- 根 `Factory.Runtime.asmdef` 覆盖运行时领域子目录。
- `DataBase/Editor/Factory.Editor.asmdef` 和 `ECS/Editor/Factory.Player.Editor.asmdef` 保留原位置，编辑器源码只在各自边界内部移动。
- 原脚本及原有 `.meta` 成对移动，保留 GUID 和导入设置。新目录的 `.meta` 由 Unity 导入生成。
- 不通过删除全部脚本 `.meta` 来刷新目录；重新生成脚本 GUID 会破坏预制体、场景等已有引用。
- 新增领域优先在对应技术层增加子目录，避免把输入、正式状态、编辑器工具混在一个目录；尚未实现的领域不建立占位代码。

完整系统建议与实施顺序见 [未来计划 2](../Plan/Future-Plan-2.md)。
