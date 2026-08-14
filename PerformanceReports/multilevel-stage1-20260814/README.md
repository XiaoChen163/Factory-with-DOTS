# 多层稀疏网格阶段 1 验收

日期：2026-08-14

本目录记录 `Multi-Level-Sparse-Grid-Migration-Plan.md` 阶段 1 的统一三维地址迁移验收。
运行环境、Unity 版本、场景和采样参数均与
`multilevel-stage0-20260814/full-loop-32x32.json` 相同。

## 正确性回归

- EditMode：115 passed / 0 failed / 0 skipped
- PlayMode：9 passed / 0 failed / 0 skipped
- 原始结果：`TestResults/stage1-editmode.xml`、`TestResults/stage1-playmode.xml`

阶段 1 新增契约覆盖：

- `GridCell(X, Level, Z)` 的相等与哈希包含 Level；
- `WorldGridConfig` 的 X、Level、Z 世界坐标往返；
- 旧矩形 `GridDefinition` 只接受 `Level = 0`；
- 平面 Belt 路径拒绝跨层起终点；
- 相同 X/Z、不同 Level 的运输节点不会连接；
- Building Runtime ID 由独立单调分配器产生，不再从坐标派生。

## 32×32 稳态性能

场景：`Perf_4096_Mk4_FullLoop`，scale=32，load=100，预热 5 秒，采样 10 秒。

| 指标 | 阶段 0 | 阶段 1 | 变化 |
|---|---:|---:|---:|
| Mean frame | 1.570 ms | 1.440 ms | -8.2% |
| P95 frame | 2.274 ms | 2.121 ms | -6.7% |
| FixedStep/Tick net | 0.663 ms | 0.639 ms | -3.7% |
| Fixed Tick | 60.90 TPS | 60.90 TPS | +0.00% |
| GC Alloc/Tick net | 0.43 B | 0.43 B | 噪声级，无持续 GC |
| Persistent Native estimate | 1092.09 MB | 1088.09 MB | -4.00 MB |

阶段 1 没有触发 5% CPU 回归门槛；本次同机结果反而略有改善。GC/Tick 仍处于小于
1 byte 的采样噪声范围。拓扑保持 2 次重建、1024 个最终节点，与阶段 0 一致。

原始数据：`full-loop-32x32.json`、`full-loop-32x32.csv`。运行日志为
`Logs/stage1-perf-full-loop-32x32.log`，包含 Opening、Entering Play Mode、READY 和
CAPTURE COMPLETE 四个完成标记。
