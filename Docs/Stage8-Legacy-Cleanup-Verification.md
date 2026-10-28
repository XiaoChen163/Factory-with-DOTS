# 阶段 8：旧系统清理验证

日期：2026-10-28。Unity：6000.3.19f1。

本次只实施改造方案 13.3 的旧系统退役，不包含连通缓存、区块流式、存档或性能采样。

## 变更范围

- 删除运行时 `GridDefinition` 及其 Size、Revision 和坐标换算重载；建造、占用、表现、运输、交互、性能场景和测试统一读取 `WorldGridConfig`。
- 删除矩形 Contains 回退；Surface Registry 未就绪时 Occupancy 不进入 Ready，建造和预览不再把矩形范围视为有效表面。
- 删除运输对全局 Grid Revision 的回退；运输拓扑和端口缓存分别消费 `TransportTopologyRevision`、`BuildingOccupancyRevision`。Transform 继续只响应 `GridTransformDirty`。
- 删除 `int2 -> GridCell` 隐式转换及交互接口的 `int2` 重载，调用方显式构建世界地址。建筑局部偏移、方向和单层性能布局的二维计算保留。
- 保留 `GridDefinitionAuthoring` 的脚本文件及 GUID，避免破坏场景引用；它不再生成旧运行时组件。矩形模式保留原枚举数值，在 Baking 时展开为 `InitialSurfaceCell`；Registry 消费后删除该 Buffer，运行时不保存矩形边界或旧初始化标记。
- 普通建筑的默认拾取只读取实际表面；地基、垂直入口的空世界工作平面、选层平面和矩形拖建锁层平面保留。
- 建筑容器继续使用已有独立单调 ID 分配器；未发现需要删除的坐标派生永久 ID 路径。

## 验证结果

- EditMode：242/242 通过。
- PlayMode：13/13 通过。
- 新增回归覆盖：配置存在但 Registry 缺失时 Occupancy 不就绪；负坐标、多层稀疏初始格不产生矩形填充，初始 Buffer 只消费一次且所有者身份保持稳定。
- 旧矩形边界专用测试退役，其余单层行为测试改为显式创建表面，保留运输、阻塞、事务回滚和局部失效断言。
- 首次 `-nographics` 全量运行有一项场景测试触发 Entities Graphics RootsHandler 异常；使用图形设备运行的完整 EditMode 和 PlayMode 均通过，未屏蔽异常或修改第三方包。
- 未运行压力测试，不据此声明阶段 8 的规模化性能验收完成。

原始结果保存在本地 `Temp/stage8-editmode-graphics.xml`、`Temp/stage8-playmode.xml`，同名日志可用于定位。

## 复现

关闭已有 Unity Editor 后，从项目根目录运行；场景测试需要图形设备，不加 `-nographics`：

```powershell
& ./.agents/skills/unity-launch/scripts/Run-UnityBatch.ps1 `
  -ProjectPath 'D:/UnityProject/Factory-with-DOTS' -TimeoutSeconds 240 `
  -LogFile 'Temp/stage8-editmode-graphics.log' `
  -ExtraArgs @('-runTests','-testPlatform','EditMode','-assemblyNames','Factory.Tests','-testResults','Temp/stage8-editmode-graphics.xml')

& ./.agents/skills/unity-launch/scripts/Run-UnityBatch.ps1 `
  -ProjectPath 'D:/UnityProject/Factory-with-DOTS' -TimeoutSeconds 300 `
  -LogFile 'Temp/stage8-playmode.log' `
  -ExtraArgs @('-runTests','-testPlatform','PlayMode','-assemblyNames','Factory.PlayModeTests','-testResults','Temp/stage8-playmode.xml')
```
