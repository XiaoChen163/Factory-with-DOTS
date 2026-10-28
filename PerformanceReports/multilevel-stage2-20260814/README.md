# 多层稀疏网格阶段 2 验证

日期：2026-08-14

## 完成范围

- `GridBuildCommandSystem` 不再为每批命令扫描并复制全世界
  `GridPlacement`；批次状态只按占格和有限邻居从持久 Occupancy 索引懒加载。
- 批次内新增、删除和路径回滚通过可复用 Overlay 处理。
- `BuildingOccupancyRevision`、`TransportTopologyRevision`、
  `TransportVisualRevision` 和 `SurfaceTopologyRevision` 已拆分。
- `BeltTransferSystem` 只用 `TransportTopologyRevision` 重建 Resolver 拓扑；
  `BuildingOccupancyRevision` 仅刷新端口拥有者缓存。兼容没有新组件的旧 Fixture 时
  才回退读取旧 `GridDefinition.Revision`。
- 新建筑创建时直接取得最终 Transform；已有建筑只在带
  `GridTransformDirty` 时重新对齐。
- Occupancy 持久 HashMap 支持批量增量扩容、增量增删、只读 Job 视图和显式
  Occupancy Revision 触发的开发期全量修复。

## 自动化回归

- `Factory.Runtime.csproj`：0 warnings / 0 errors
- `Factory.Tests.csproj`：0 warnings / 0 errors
- Unity EditMode：118 passed / 0 failed / 0 skipped
- 原始结果：`TestResults/stage2-editmode.xml`

阶段 2 新增/强化测试覆盖：

- 64 个远处建筑存在时，失败路径只物化实际冲突的 1 个既有 Placement；
- Building Occupancy Revision 不使 Transport Topology 重建；
- Transport Topology Revision 改变时正常重建；
- Transform 只响应显式 `GridTransformDirty`；
- 显式编辑 Placement 后可通过 Occupancy Revision 修复持久索引。

## 同参数性能对照

场景：`Perf_ContinuousBeltBuild`，scale=32，预热 5 秒，采样 10 秒，
timeDelay=0.05，drag=0.05，demolitionOrder=0。

| 指标 | 阶段 0 | 阶段 2 | 变化 |
|---|---:|---:|---:|
| Build batches | 64 | 64 | 相同 |
| Path cells validated | 1024 | 1024 | 相同工作量 |
| Existing Placement scans | 32768 | 1024 | -96.875% |
| Temporary records | 33792 | 2048 | -93.94% |
| Mean frame | 1.009 ms | 0.844 ms | -16.4% |
| P95 frame | 1.775 ms | 1.426 ms | -19.7% |
| Fixed Tick | 62.41 TPS | 62.45 TPS | 稳定 |
| Managed memory delta | +5,926,912 B | -4,657,152 B | 无新增净增长 |
| Topology rebuilds | 66 | 66 | 运输变化语义不变 |

阶段 2 的 1024 次扫描来自 32 次删除各自沿实际相连的 32 格传送带遍历，
不再来自远处无关建筑。原始数据为 `continuous-build-32.json` / `.csv`。

另保留一份 scale=64 的扩大样本 `continuous-build.json` / `.csv`，128 个批次中
Placement scans 为 4096，仍严格等于 64 次删除 × 64 个实际相连节点。

Unity 批处理退出时仍报告 2 个 Preview Scene 未关闭；该信息发生在完成标记和报告写入
之后，且没有 Native Collection 或 weakptr 泄漏。本阶段没有修改 Preview Scene 管线。
