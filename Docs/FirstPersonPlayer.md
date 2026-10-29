# 第一人称玩家

打开 `Assets/Scenes/Ecs.unity` 后运行。场景使用 `Assets/Prefabs/Player/FirstPersonRobot.prefab`，原俯视控制器已移除。

| 操作 | 按键 |
| --- | --- |
| 移动 | WASD / 方向键 |
| 转动视角 | 鼠标 |
| 步行时跳跃 | 空格 |
| 切换飞行 / 重力 | 0.3 秒内双击空格 |
| 飞行上升 / 下降 | 空格 / Shift |
| 步行或飞行加速 | 左右 Ctrl |

默认速度：步行 4、跑步 7、飞行 12、加速飞行 21 单位/秒，可在预制体的 `FirstPersonPlayerController` 中调整。

飞行时按住左右任一 Shift 下降，接触地基等可站立表面后自动退出飞行并恢复重力；悬停或同时按住空格、Shift 抵消升降时保持飞行。

游戏操作时锁定并隐藏鼠标，普通建造、拆除、建筑选择和垂直传送带预览均使用相机视口中心射线。背包、建筑窗口、建造目录或拖拽需要鼠标时释放光标并停止视角、水平移动及飞行升降；步行模式下重力继续生效。关闭界面后重新锁定。原有建造、拆除和 Ctrl 整线拆除规则保留。

第一人称交互射线从相机位置起算，最大距离为 50 米，包含边界。地基／坡道碰撞、空地建造平面、锁定建造层和垂直传送带出口预览均受此限制；超出距离时不产生有效命中。

玩家使用官方 `com.unity.charactercontroller` 1.4.5 与 Unity Physics，不再挂载 `UnityEngine.CharacterController`。`EcsCharacterMotor` 负责创建、销毁角色实体及同步表现位置，不再自行求解胶囊碰撞。`FactoryCharacterPhysicsUpdateSystem` 在官方固定物理更新组中运行；`FactoryCharacterProcessor` 调用官方接地、台阶／坡道、移动碰撞和穿透恢复流程。角色实体拥有 `KinematicCharacterBody`、`KinematicCharacterProperties`、ECS 胶囊 `PhysicsCollider` 及官方命中缓冲区，并使用官方位置插值。输入和机器人 Animator／相机仍由 GameObject 提供。

场景初始化时，通过 `SurfaceRegistrySystem.AddFoundation` 在玩家出生点脚下生成 10×10 的普通 ECS 地基；等待地基碰撞就绪后才开始角色移动与重力。地基可正常建造、拆除，不会重新补回已拆除的格子，场景不再放置 `Factory Floor`。角色碰撞使用独立的 Character 类别，建造射线不会命中角色自身。

运行时修改出生／传送位置应调用 `EcsCharacterMotor.Teleport`，它同步 ECS 位置、重置速度和插值；直接修改表现对象的 Transform 不会改变角色实体位置。禁用玩家控制组件时暂停角色模拟；销毁角色后延迟到下一次物理世界重建结束再释放胶囊碰撞资源。

建筑／拆除模式下按 Esc 只退出该模式，继续隐藏、锁定鼠标；普通模式下按 Esc 才释放并显示鼠标。关闭已打开窗口的 Esc 仅关闭窗口。释放指针后，点击游戏空白区域恢复第一人称，该次点击不会同时操作建筑。

机器人及三个循环动画位于 `Assets/Art/Characters/Robot`。`RobotLocomotion.controller` 的 Locomotion 状态使用 Speed 一维混合树，阈值为 0 / 4 / 7，由角色实际水平速度驱动；关闭根运动。完整机器人可在 Scene 视图看到，玩家相机排除 Player 层以避免头部模型遮挡视野。

输入全部使用新 Input System（Active Input Handling = Input System Package）。移动操作使用独立的 FirstPerson action map，因此建造模式不会禁用移动。UI 与建造继续使用现有输入资产。

编辑器菜单 `Factory/Player/Install First Person Robot` 可重建预制体和场景接线。它会重置玩家出生点及视角配置，运行前请保存自定义配置。剩余 Starter Assets 目录已删除，保留资源的 GUID 和来源信息未更改。

Play Mode 回归：`FirstPersonPlayerPlayModeTests`、`EcsCharacterCollisionPlayModeTests`、`FirstPersonInteractionReachPlayModeTests`、`FoundationRenderingPlayModeTests` 和 `ConstructionUiPlayModeTests`，覆盖飞行、重力、加速、动画、UI 输入阻断、DOTS 地基落地、入口边缘零距离接触脱困、碰撞后反向移动、50 米交互范围及原有建筑交互。

控制处理器及固定步长系统改编自官方 Standard Characters 示例，许可见 `Docs/CharacterController-LICENSE.md`。
