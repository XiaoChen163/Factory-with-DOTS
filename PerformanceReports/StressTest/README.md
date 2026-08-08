# Factory ECS 压力测试对比（Phase 3 → Phase 4）

对比时间：2026-08-07（Asia/Shanghai）

场景：`Perf_4096_Mk4_FullLoop`

配置：`StartScale=2`、`MaxScale=1024`、倍率 2、负载 100%、预热 3 秒、
采样 5 秒、TPS 阈值 50、`-nographics`。

硬件：Intel Core i9-12900HX、32 GB RAM。

## 对照运行

- Phase 3 基线：`stress-20260807-023047`
- Phase 4 最终：`stress-20260807-213025`

两个运行使用相同参数。极限点没有改变：`128` 通过、`256` 失败。

## 各档位对比

| Scale | 节点数 | Phase3 TPS | Phase4 TPS | Phase3 P95 ms | Phase4 P95 ms |
|---:|---:|---:|---:|---:|---:|
| 2 | 4 | 61.20 | 61.40 | 0.39 | 0.47 |
| 4 | 16 | 61.60 | 61.40 | 0.46 | 0.43 |
| 8 | 64 | 61.40 | 61.40 | 0.54 | 0.56 |
| 16 | 256 | 61.60 | 61.40 | 0.60 | 0.60 |
| 32 | 1024 | 61.60 | 61.60 | 1.37 | 1.11 |
| 64 | 4096 | 61.38 | 61.58 | 3.83 | 3.68 |
| 128 | 16384 | 61.94 | 61.67 | 17.38 | 17.72 |
| 256 | 65536 | 28.16 | 31.78 | 733.19 | 618.40 |

## 极限档（65536 节点）明细

| 指标 | Phase3 | Phase4 | 变化 |
|:---|---:|---:|---:|
| FixedStep ms/Tick | 31.37 | 27.97 | -10.8% |
| Main Thread ms/Tick | 31.47 | 28.05 | -10.9% |
| BeltTransfer ms/Tick | 0.24 | 0.16 | -33.3% |
| 帧 P95 ms | 733.19 | 618.40 | -15.7% |
| TPS | 28.16 | 31.78 | +12.9% |

Phase 4 新增的 `ItemVisualStateCaptureSystem` 和
`ItemTransformPresentationSystem` 在 65536 档合计约 0.02 ms/Tick，不是
极限瓶颈。`TransferCommandBufferSystem` 仍是该档主要剩余耗时。

## 4096 节点复核

依赖解耦后 4096 FullLoop 单独复测：

| 指标 | Phase3 | Phase4 |
|:---|---:|---:|
| TPS | 61.38 | 61.58 |
| 帧 P95 ms | 3.83 | 3.68 |
| FixedStep ms/Tick | 2.67 | 2.81 |
| BeltTransfer ms/Tick | 0.05 | 0.05 |

视觉与池化系统合计约 0.04 ms/Tick，4096 档与 Phase 3 基本同量级。

原始报告：

- `stress-20260807-213025/scale-64/report-detailed.json`
- `stress-20260807-213025/scale-256/report-detailed.json`
- `stress-20260807-213025/stress-summary.json`
