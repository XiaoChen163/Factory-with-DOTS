# Phase 3 三场景独立性能测试（含补充场景）

采集时间：2026-08-06（Asia/Shanghai）

Unity：6000.3.19f1，Windows Editor Batch Mode，`-nographics`

硬件：Intel Core i5-9500F（6 逻辑核）、16 GB RAM（本机自动检测）

每个场景使用独立 Unity 进程，READY 后预热 5 秒并采样 10 秒。FixedStep、
BeltTransfer 和 GC 均按采样期间实际 Fixed Tick 数折算
（`marker sum ÷ fixedTickCount`）。

## 核心三场景结果

| 场景 | 实体规模 | Tick/s | 平均帧 / P95 | FixedStep ms/Tick | BeltTransfer ms/Tick | GC KiB/Tick | 成功传输/Tick |
|:---|:---|---:|---:|---:|---:|---:|---:|
| 4096 半载蛇形 | 4096 Belt、2048 Item | 62.10 | 0.874 / 2.081 | 0.630 | 0.046 | 47.7 | 244.39 |
| F16 × 256 分叉 | 5165 Belt、16 Splitter、1024 Item | 61.90 | 0.873 / 2.118 | 0.688 | 0.034 | 47.2 | 127.65 |
| 4096 满载 + 一级尾带 | 4097 Belt、1 Storage、4081 Item | 62.10 | 0.838 / 1.853 | 0.646 | 0.040 | 48.1 | 72.39 |

## 硬件不敏感指标对照（Phase 2 → Phase 3）

以下指标不依赖 CPU 频率，可直接对照两个 Phase。Phase 2 数据来自 i9-12900HX，
Phase 3 来自 i5-9500F，均为 Batch Mode、5 秒预热、10 秒采样。

### 每 Tick 模拟统计（确定性）

| 场景 | Ready 请求/Tick | 成功传输/Tick | 接受率 |
|:---|---:|---:|---:|
| 4096 半载蛇形 | 331.8 → 329.4 | 247.3 → 244.4 | 74.5% → 74.2% |
| F16 × 256 分叉 | 128.7 → 127.7 | 128.7 → 127.7 | 100% → 100% |
| 4096 满载 + 尾带 | 3633 → 3579 | 60.8 → 72.4 | 1.7% → 2.0% |

- HalfLoaded / F16 每 Tick 传输量几乎不变（±1% 内），确认仲裁行为未变。
- Blocking 的 Ready 请求量稳定在 ~3600/Tick；成功传输的差异来自采样相位：
  仓储从空开始填充，两次采样的 Tick 窗口不同。整个运行期的物品消耗量
  （4096 → 4082 vs 4096 → 4081）基本一致，行为等价。

### 确定性算法工作量

- Phase 2 Resolver 报告：候选输入检查严格为 `N - 1`（128→127、512→511、
  1024→1023、4096→4095），单轮路由 pass。
- Phase 3 EditMode 测试 `CandidateSelection_WorkScalesLinearlyTo4096Nodes`
  仍断言同一数值（`LinearCandidateInspectionCount == N - 1`、
  `LinearRoutingPassCount == 1`）并通过，O(N) 工作量与 Phase 2 完全一致。
- 拓扑只在 Grid Revision 变化时重建：两个 Phase 的测试均断言 Revision 不变
  时重建 1 次、变化后重建 2 次。

### GC 字节分配（本质不随 CPU 速度变化）

| 场景 | Phase 2 KiB/Tick | Phase 3 KiB/Tick | 变化 |
|:---|---:|---:|---:|
| 4096 半载蛇形 | 248.0 | 47.7 | -80.8% |
| F16 × 256 分叉 | 299.9 | 47.2 | -84.3% |
| 4096 满载 + 尾带 | 247.1 | 48.1 | -80.5% |

分配字节数是确定性工作，不受硬件影响；折算公式包含每帧基线分配，高帧率
会略微放大数值，但两个 Phase 的口径相同。

### 其他

- `wait_for_job_group`：两个 Phase 采样均为 0。
- 60 Tick/s 达标属性：Phase 2 在 i9 上为 60.5～60.6；Phase 3 在更弱的
  i5 上为 61.8～62.1，达标余量反而更大（Tick/s 绝对值本身硬件敏感）。
## 与同机 Phase 1 对照

Phase 1 基线取自同一台 i5-9500F 上的
`phase1-current-hardware/`，两者同为 Batch Mode、5 秒预热、10 秒采样，
可作同口径优化幅度结论。

| 场景 | FixedStep ms/Tick | BeltTransfer ms/Tick | GC KiB/Tick | Tick/s |
|:---|---:|---:|---:|---:|
| 4096 半载蛇形 | 20.190 → 0.630 | 20.147 → 0.046 | 717.8 → 47.7 | 49.27 → 62.10 |
| F16 × 256 分叉 | 58.067 → 0.688 | 58.020 → 0.034 | 1366.0 → 47.2 | 17.19 → 61.90 |
| 4096 满载 + 一级尾带 | 28.405 → 0.646 | 28.334 → 0.040 | 726.0 → 48.1 | 35.07 → 62.10 |

- BeltTransfer 主路径：-99.8% / -99.9% / -99.9%；
- FixedStep 组总量：-96.9% / -98.8% / -97.7%；
- GC：-93.4% / -96.5% / -93.4%；
- 三个场景在 Phase 1 无法维持 60 Tick/s，Phase 3 全部稳定达到 60+ Tick/s。

## 补充场景（Phase 3 新增覆盖）

| 场景 | 实体规模 | Tick/s | FixedStep ms/Tick | BeltTransfer ms/Tick | GC KiB/Tick | 成功传输/Tick |
|:---|:---|---:|---:|---:|---:|---:|
| 512 混合 Junction | 462 Belt、25 Merger、25 Splitter、256 Item | 61.90 | 0.155 | 0.033 | 101.2 | 34.59 |
| 4096 满环 | 4096 Belt、4096 Item | 61.79 | 0.594 | 0.051 | 47.6 | 516.97 |

FullLoop 采样期物品按 Mk4 速度（8 格/s）持续环移，`acceptedTransfersPerTick
= 516.97`，与 4096 节点每格约 7.5 Tick 的流通量一致，未出现物品丢失。

## 退出条件核对

- Transfer 主路径 Burst 化：`BeltTransferSystem.OnUpdate` 主线程 marker
  降至 0.034～0.051 ms/Tick，不再由托管 OnUpdate 主导模拟时间。
- 非必要 `WaitForJobGroup`：三个采样中 `wait_for_job_group` 均为 0。
- GC：47～48 KiB/Tick（同机 Phase 1 下降 93%～96%），尚未达到 `0 B`；
  残余分配主要来自每 Tick 创建/回放 ECB 与约 1.3 KiB/帧的帧基线分配。
- FixedStep 组内 BeltTransfer 之外的剩余时间（约 0.58 ms/Tick）来自 Burst
  Job 执行和延迟 ECB Playback（`TransferCommandBufferSystem` 暂未列入采集
  指标），已不含 NativeArray → 托管数组快照与逐实体写回。
- `belt_progress` / `belt_item_position` 等子 marker 采样值低于 0.5 µs，
  `ReadDisplayValue` 四舍五入显示为 0.0000 ms。

## 正确性

各场景 READY 实体数与场景定义一致；同机 EditMode 回归 `Factory.Tests` 为
`39 passed / 0 failed`（2026-08-06）。

## 采集告警

- 与 Phase 2 记录同类别的 Entities Graphics 噪音：`-nographics` 下系统创建
  时 `SkinningDeformationSystem.OnCreate` 报告一次 NullReferenceException，
  发生在 READY 之前，不影响采样数据。
- MixedJunction 场景约 3700 FPS，帧基线分配按 Tick 折算后被放大，其
  GC/Tick（101 KiB）高于其余场景，属于高帧率折算口径效应。
- Phase 2 场景数据来自 i9-12900HX 基线机；本文 Phase 3 数据来自 i5-9500F
  本机。跨硬件绝对 ms 只作量级参考，优化幅度以同机 Phase 1 对照为准。

## 原始数据

- `Perf_4096_Mk4_HalfLoaded.json` / `.csv`
- `Perf_F16_Mk4_1024Items.json` / `.csv`
- `Perf_4096_Mk4_Blocking.json` / `.csv`
- `Perf_512_MixedJunction.json` / `.csv`
- `Perf_4096_Mk4_FullLoop.json` / `.csv`