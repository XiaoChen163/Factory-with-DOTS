# Phase 4 视觉解耦与 Item 池化性能采集

采集时间：2026-08-07（Asia/Shanghai）

Unity：6000.3.19f1，Windows Editor Batch Mode，`-nographics`

硬件：Intel Core i9-12900HX（24 逻辑核）、32 GB RAM（本机自动检测）

每个场景独立 Unity 进程，READY 后预热 2 秒并采样 5 秒。FixedStep 与各系统
marker 按采样期间实际 Fixed Tick 数折算。

## 4096 半载蛇形

原始数据：`4096-half-loaded-nographics.json` / `.csv`

| 指标 | 数值 |
|:---|---:|
| Tick/s | 61.59 |
| FixedStep ms/Tick | 3.21 |
| TransferCommandBuffer ms/Tick | 3.09 |
| BeltTransfer ms/Tick | 0.06 |
| ItemVisualStateCapture ms/Tick | 0.02 |
| ItemTransformPresentation ms/Tick | 0.02 |
| 净 GC B/Tick | 0.17 |
| 成功传输/Tick | 248.30 |
| 活跃 Item 实体 | 2048 |

## 8 条生产-消费链

原始数据：`producer-consumer-full.json` / `.csv`

| 指标 | 数值 |
|:---|---:|
| Tick/s | 62.32 |
| FixedStep ms/Tick | 0.28 |
| BeltTransfer ms/Tick | 0.04 |
| ItemVisualStateCapture ms/Tick | 0.02 |
| ItemTransformPresentation ms/Tick | 0.01 |
| 净 GC B/Tick | 0.00 |
| 成功传输/Tick | 1.20 |
| 活跃 Item 实体 | 8 |

## 说明

- `TransferCommandBufferSystem` 是本机 4096 场景 FixedStep 的主要剩余耗时。
  同机、同口径的 Phase 3 基线短采也约为 3 ms/Tick，因此该数值属于本机
  背景波动范围，不是 Phase 4 引入的回归；两个 Phase 的 `belt_transfer`
  都在 0.04～0.06 ms/Tick。
- `ItemVisualStateCaptureSystem` 与 `ItemTransformPresentationSystem` 合计
  约 0.03～0.04 ms/Tick，视觉解耦没有把成本放回 Fixed Step。
- ProducerConsumer 场景无初始 Item，矿机持续产出、熔炉/仓库持续消费，
  `itemEntities` 在采样期稳定为 8，说明池化后的活跃实体数量不再随吞吐
  持续增长；无池时仍保留 Instantiate/Destroy 调试后备。
- Phase 4 未实现可选视野裁剪；可见性裁剪保持为后续可扩展项。

## 正确性

同机 EditMode 回归 `Factory.Tests`：`53 passed / 0 failed`
（2026-08-07），覆盖 Phase 1/2/3 既有规则以及 Phase 4 的池归还、池复用、
无池回退、视觉快照采样、Presentation 进度插值和 PostTransformMatrix
非均匀缩放保持。
