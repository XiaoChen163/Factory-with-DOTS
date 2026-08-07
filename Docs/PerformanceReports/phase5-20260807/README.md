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
