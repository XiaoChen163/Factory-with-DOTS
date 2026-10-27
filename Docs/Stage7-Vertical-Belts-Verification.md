# 阶段 7 垂直传送带验证

日期：2026-10-28。Unity：6000.3.19f1。

## 自动化结果

- EditMode：209/209 通过；其中新增 `MultiLevelStage7Tests` 12 项。
- PlayMode：12/12 通过；新增正式 `Ecs` 场景中的 Mk4 入口／中间节点／出口建造、Physics 射线拾取和中间节点独立拆除测试。
- 编译及资源导入无 C# / Burst 编译错误。
- 未运行压力测试或性能采样。

EditMode 覆盖上下行、混合批次穿过地基、无支撑拒绝、方向不一致、入口禁止分叉、出口禁止合流、
独立占用冲突、普通建筑与竖井冲突、带物品拒拆、空节点断链重建、平面—垂直—平面交接、
逐节点阻塞、双竖井满载闭环原子移动、输出格拾取解析，以及 12 个等级 prefab／图标／速度配置。

PlayMode 在正式场景通过建造目录和玩家命令管线创建三个 Mk4 节点，验证速度为 8、
运行时表现引用有效、节点进入 Physics World，并以水平射线命中细圆柱，随后单独拆除中间节点。

## 复现

使用项目 `unity-launch` 技能的 `Run-UnityBatch.ps1`，关闭已有 Unity Editor 后运行：

```powershell
& ./.agents/skills/unity-launch/scripts/Run-UnityBatch.ps1 `
  -ProjectPath 'D:/UnityProject/Factory-with-DOTS' -TimeoutSeconds 240 `
  -ExtraArgs @('-runTests','-testPlatform','EditMode','-testResults','Logs/stage7-editmode.xml')

& ./.agents/skills/unity-launch/scripts/Run-UnityBatch.ps1 `
  -ProjectPath 'D:/UnityProject/Factory-with-DOTS' -TimeoutSeconds 240 `
  -ExtraArgs @('-runTests','-testPlatform','PlayMode','-testResults','Logs/stage7-playmode.xml')
```

资源可通过 `Factory/Assets/Rebuild Vertical Belt Prefabs` 重建，沿用现有图标烘焙与 CSV 数据库导入管线。
