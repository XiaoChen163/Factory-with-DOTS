# Phase 5 Port、建造和拓扑尖峰优化报告

日期：2026-08-07

场景：`Perf_ContinuousBeltBuild`，默认 `scale=64`，每条 64 格，共 64 条，
满载峰值 4096 个 Belt 实体；放置/拆除间隔 `timeDelay=0.25s`，拖动
`drag=0.25s`，正序拆除。样本为完整放置和拆除过程。

| 指标 | Phase5 前 | Phase5 后 | 变化 |
|---|---:|---:|---:|
| 平均帧时间 | 1.315 ms | 1.133 ms | -13.9% |
| P95 帧时间 | 2.194 ms | 2.059 ms | -6.2% |
| P99 帧时间 | 4.121 ms | 3.688 ms | -10.5% |
| 最大帧时间 | 117.025 ms | 109.855 ms | -6.1% |
| FPS | 760.15 | 882.21 | +16.1% |
| Fixed Tick | 60.25 TPS | 60.23 TPS | 持平 |

## 本轮改动

- Port reservation 计数从 Resolver 的 Native Map 移入
  `ItemInputPortSnapshot.ReservedTransferCount` 和
  `ItemOutputPortSnapshot.ReservedTransferCount`。
- Port Current/Next 改为 `ItemPortBufferGeneration` 双缓冲索引，Swap 只翻转
  generation，不再复制整个缓冲区。
- occupancy 拆除时增量移除，新增建筑通过 `PendingOccupancyAdd` 增量加入，
  不再每次建造全量重建。
- Belt 视觉通过 `BeltVisualDirtyCell` 只刷新受影响单元格与邻居，并把
  `DisableRendering` 变更合并到单个 ECB 播放。

## 回归

`Factory.Tests`：`59 passed / 0 failed`，新增
`Phase5OptimizationTests` 4 个用例。

## 最终验收

`Perf_4096_Mk4_FullLoop` 稳态 4096 节点采样（预热 5s、采样 10s）：

| 指标 | 结果 |
|---|---:|
| Fixed Tick | 60.89 TPS |
| FixedStepSimulationSystemGroup | 2.05 ms/Tick |
| BeltTransferSystem | 0.04 ms/Tick |
| ItemPortBufferSwapSystem | 0.00 ms/Tick |
| GC Alloc 净分配 | 0.68 B/Tick（噪声级，帧基线 1124 B 已扣除） |
| P95 帧时间 | 5.11 ms |

完成定义逐项证据：

- 传送、合流、分流、环路和端口回归：`Factory.Tests` 59/59 通过；
- 稳态 Tick 无托管 GC：FullLoop `gc_allocated_in_frame.perTickNet = 0.68 B`；
- Resolver 近似 `O(N)` 且不每 Tick 重建：
  `Topology_RebuildsOnlyWhenRevisionChanges` 与
  `CandidateSelection_WorkScalesLinearlyTo4096Nodes` 通过；
- Transfer 主路径 Burst Native Job：`FactoryTransferArbitrationJob` 经
  `BeltTransferSystem.Schedule` 执行，Phase 3/4/5 传输测试通过；
- 稳态逻辑状态不逐 Entity 主线程写入：仲裁 Job 使用 ComponentLookup/Buffer
  Lookup 与 ECB，`TransferCommandBufferSystem` 回放；
- Grid 和建筑 Transform 只在 Revision 变化时更新：`GridPlacementTransformSystem`
  缓存 Revision，Belt 视觉只消费脏单元格；
- 物品视觉每渲染帧最多一次：Phase 4 快照与 Presentation 测试通过；
- 稳定吞吐不持续结构变化：Item Pool 复用测试通过，
  `ItemPoolInitializationSystem` 不在稳态 Tick 重建；
- 4096 节点目标帧预算：FixedStep 2.05 ms/Tick，远低于 5 ms 建议值；
- Profiler 基线、优化后数据和测试场景：
  `baseline-scale64.json`、`optimized-scale64.json`、
  `4096-full-loop-detailed.json` 与 `Perf_ContinuousBeltBuild` 一并保留。

## 原始数据

- `baseline-scale64.json`：Phase5 前 `Perf_ContinuousBeltBuild` 默认 64
- `optimized-scale64.json`：Phase5 后同一场景同一参数
- `4096-full-loop-detailed.json`：`Perf_4096_Mk4_FullLoop` 稳态详细采集
