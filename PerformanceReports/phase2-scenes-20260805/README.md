# Phase 2 三场景独立性能测试

采集时间：2026-08-05（Asia/Shanghai）

Unity：6000.3.19f1，Windows Editor Batch Mode，`-nographics`

硬件：Intel Core i9-12900HX，24 logical processors，32 GB RAM

每个场景使用独立 Unity 进程，READY 后预热 5 秒并采样 10 秒。

## 结果

FixedStep、BeltTransfer 和 GC 均按采样期间的实际 Fixed Tick 数折算。

| 场景 | 实体规模 | Tick/s | 平均帧 / P95 | FixedStep ms/Tick | BeltTransfer ms/Tick | GC KiB/Tick | 成功传输/Tick |
|:---|:---|---:|---:|---:|---:|---:|---:|
| 4096 半载蛇形 | 4096 Belt、2048 Item | 60.60 | 1.343 / 2.527 ms | 2.016 | 1.945 | 248.0 | 247.25 |
| F16 × 256 分叉 | 5165 Belt、16 Splitter、1024 Item | 60.59 | 1.591 / 3.126 ms | 2.625 | 2.551 | 299.9 | 128.67 |
| 4096 满载 + 一级尾带 | 4097 Belt、1 Storage、采样结束时 4082 Item | 60.50 | 1.388 / 2.672 ms | 2.204 | 2.096 | 247.1 | 60.80 |

三个场景均稳定达到 60 Fixed Tick/s。`WaitForJobGroupID` 在三个采样中均为
0，`Structural Changes` marker 未被当前采集器解析到。

## 与 Phase 1 本机数据的参考对照

下表使用同一台 i9-12900HX 上保存的 Phase 1 数据。Phase 1 是有图形界面的
Editor Play Mode，本次是 `Batch Mode + -nographics`，因此百分比只用于观察
模拟 marker 的量级变化，不能视为严格同口径结论。

| 场景 | BeltTransfer ms/Tick | 参考变化 | GC KiB/Tick | 参考变化 |
|:---|---:|---:|---:|---:|
| 4096 半载蛇形 | 13.993 → 1.945 | -86.1% | 717.1 → 248.0 | -65.4% |
| F16 × 256 分叉 | 35.166 → 2.551 | -92.7% | 1395.7 → 299.9 | -78.5% |
| 4096 满载 + 一级尾带 | 19.390 → 2.096 | -89.2% | 719.9 → 247.1 | -65.7% |

F16 场景从 Phase 1 的 27.70 Tick/s 提升到本次 60.59 Tick/s；阻塞场景从
49.07 Tick/s 提升到 60.50 Tick/s。半载场景在两个版本中都能维持 60 Tick/s。

## 采集告警

- 首个场景强制重导入 SubScene 时，`ItemPrefabBakingSystem` 报告一次
  `BufferTypeHandle<ItemPrefabEntry>` 被结构变化失效；场景之后仍以正确的
  4096 Belt / 2048 Item 状态进入 READY 并完成采集。
- 三个进程在 CAPTURE COMPLETE 之后退出时，Entities Graphics 在
  `-nographics` 模式下报告 `NullReferenceException`，并提示两个 Preview
  Scene 未关闭。异常发生在结果文件写入之后，不影响采样数据，但属于需要
  单独清理的测试基础设施问题。
- GC 仍约为 247～300 KiB/Tick，尚未达到 `0 B/Tick`。NativeArray 转托管
  数组和逐 Entity 写回仍属于 Phase 3 范围。

## 原始数据

- `Perf_4096_Mk4_HalfLoaded.json` / `.csv`
- `Perf_F16_Mk4_1024Items.json` / `.csv`
- `Perf_4096_Mk4_Blocking.json` / `.csv`
