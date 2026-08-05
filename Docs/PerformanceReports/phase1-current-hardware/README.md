# Phase 1 当前硬件性能复测

采集时间：2026-08-05（Asia/Shanghai）  
Unity：6000.3.19f1，Windows Editor Batch Mode  
预热：5 秒；采样：10 秒

任务说明中给出的当前硬件是 i5-9400F、GTX 1650、16 GB RAM；本次 Unity
`SystemInfo` 实际检测到的是：

- CPU：Intel Core i5-9500F @ 3.00 GHz（6 logical processors）
- GPU：NVIDIA GeForce GTX 1660 SUPER
- RAM：16326 MB

下表和原始 JSON 均以自动检测结果为准。该环境与原始基准的 i9-12900HX、
RTX 4060 Laptop、32 GB RAM 不同，而且本次为 Batch Mode，因此数据只用于
保留当前执行机器上的 Phase 1 运行记录，不得直接计算优化幅度或回归结论。

## 测试结果

EditMode 回归测试：15 passed，0 failed，0 skipped。

| 场景 | 平均帧 / P95 | FPS | Fixed Tick/s | FixedStep ms/Tick | BeltTransfer ms/Tick | GC MiB/Tick | 成功传输/Tick |
|:---|---:|---:|---:|---:|---:|---:|---:|
| 4096 半载蛇形 | 387.489 / 399.751 ms | 2.59 | 49.27 | 20.190 | 20.147 | 0.701 | 248.94 |
| F16 × 256 分叉 | 1110.104 / 1156.958 ms | 0.90 | 17.19 | 58.067 | 58.020 | 1.334 | 124.32 |
| 4096 满载 + 一级尾带 | 544.271 / 584.029 ms | 1.85 | 35.07 | 28.405 | 28.334 | 0.709 | 67.99 |

FixedStep、BeltTransfer 和 GC 的每 Tick 数值使用采样期总 Fixed Tick 数折算。
Phase 1 仍未消除 Resolver 和 NativeArray → 托管数组路径的分配；这些属于
Phase 2/3 的拓扑缓存、线性 Resolver 与 Native Job 重构范围。

## 原始数据

- `Perf_4096_Mk4_HalfLoaded.json` / `.csv`
- `Perf_F16_Mk4_1024Items.json` / `.csv`
- `Perf_4096_Mk4_Blocking.json` / `.csv`

真实优化幅度应在原基准设备更新本次代码后，使用相同 Editor 模式、场景、
预热时间和采样时间重新采集。
