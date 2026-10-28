# 多层稀疏网格阶段 0 基线

日期：2026-08-14

本目录冻结 `Multi-Level-Sparse-Grid-Migration-Plan.md` 阶段 0 的单层行为、
建造诊断和性能比较基线。采样均由 Unity 批处理性能管线完成，未开启 Deep Profile。

## 运行环境

- Unity：`6000.3.19f1`
- Entities：`1.4.8`
- OS：Windows 11 10.0.22631
- CPU：Intel Core i9-12900HX，24 logical processors
- 内存：32 GB
- GPU：NVIDIA GeForce RTX 4060 Laptop GPU
- 固定模拟频率：60 Tick/s
- 采样：预热 5 秒、采样 10 秒；连续建造场景由 Driver 全程采样

完整机器字段同时保存在每份 JSON 的 `unityVersion`、`operatingSystem`、
`processor`、`processorCount`、`systemMemoryMegabytes` 和 `graphicsDevice` 中。

## 自动化回归

- EditMode：109 passed / 0 failed / 0 skipped
- PlayMode：9 passed / 0 failed / 0 skipped
- 原始 PlayMode 结果：`TestResults/stage0-playmode.xml`
- 原始 EditMode 结果：`TestResults/stage0-editmode.xml`

阶段 0 新增契约覆盖：

- World/Cell 正负坐标、Origin 和 CellSize 往返；
- 单格与 `2x3` 多格 footprint 的四向整数旋转；
- 矩形边界；
- L 形 Belt 路径的 X 优先、Z 优先、转角及末端方向；
- 建造批次诊断计数；
- 拓扑重建次数、节点数和耗时。

现有运输测试继续覆盖 Belt、Merger、Splitter、端口、阻塞、满环原子移动、
round-robin 和 Revision 不变时不重建。补测过程中修复了旧实现中偶数×奇数 footprint
在 90/270 度旋转时因浮点舍入导致占格折叠的问题；当前整数映射结果作为后续迁移契约。

## 性能结果

| 场景 | Mean frame | P95 frame | FixedStep/Tick | TPS | GC Alloc/Tick net | Persistent Native estimate | Draw Call P50 |
|---|---:|---:|---:|---:|---:|---:|---:|
| 32×32 满载满环，1024 节点 | 1.570 ms | 2.274 ms | 0.664 ms | 60.90 | 0.43 B | 1092.09 MB | 0 |
| 32 条×32 格连续建造/拆除 | 1.009 ms | 1.775 ms | 0.433 ms | 62.41 | 4857.91 B | 1058.09 MB | 0 |
| 64×64 满载满环，4096 节点 | 3.832 ms | 6.099 ms | 2.337 ms | 60.99 | 1.19 B | 1118.09 MB | 0 |

`Persistent Native estimate` 是同一采样帧的
`Total Reserved Memory P50 - GC Reserved Memory P50`。Unity 6000.3 的当前可用
Profiler Recorder 没有暴露独立的 Persistent Allocator 字节计数，因此报告明确保存该
可复算代理值以及两项原始指标。稳态场景 GC 净分配为噪声级；连续建造场景包含预览、
命令和托管临时记录分配，不能当作稳态 GC 门槛。

批处理模式虽然创建了 D3D12 设备，但本机 `CPU Render Thread Frame Time` 和
`Draw Calls Count` Recorder 均返回 0；该限制已冻结在报告的 `batchMode=true`、原始
metric 和 `nonZeroSampleCount=0` 中。后续阶段必须用相同批处理模式做 CPU 对比；需要
验收实际渲染时，另做非 batch 同场景采样，不与本表混算。

## 非 Batch 渲染样本

使用与 32×32 满环相同的场景、规模、装载率、5 秒预热和 10 秒采样参数，另启动一次
非 batch Editor Play Mode。该报告记录 `batchMode=false`，并确认全部 1739 个采样帧的
Render Thread 和 Draw Call 均为非零：

| 指标 | 结果 |
|---|---:|
| Mean frame | 5.746 ms |
| P95 frame | 6.609 ms |
| FixedStep/Tick | 0.573 ms |
| Fixed Tick | 60.88 TPS |
| Render Thread mean | 0.851 ms |
| Render Thread P50 | 0.819 ms |
| Render Thread P95 | 1.045 ms |
| Draw Call mean | 68.97 |
| Draw Call P50 / P95 | 69 / 69 |

原始数据为 `nonbatch-full-loop-32x32.json` / `.csv`。它用于冻结实际 Editor 渲染口径，
不与上面的 batch CPU 数字直接计算回归百分比。

## 诊断基线

| 场景 | Build batches | Placement scans | Temporary records | Path cells validated | Topology rebuilds | Final nodes | Last rebuild |
|---|---:|---:|---:|---:|---:|---:|---:|
| 32×32 满环 | 1 | 0 | 1024 | 0 | 2 | 1024 | 3.166 ms |
| 连续建造/拆除 32 | 64 | 32768 | 33792 | 1024 | 66 | 0 | 0.006 ms |
| 4096 满环 | 1 | 0 | 4096 | 0 | 2 | 4096 | 3.727 ms |

连续建造场景清楚记录了阶段 2 要消除的当前线性扫描：64 个批次累计扫描 32768 个
既有 Placement。每条 32 格路径累计验证 1024 格，放置失败时仍由事务暂存列表整体回滚。

## 原始数据

- `full-loop-32x32.json` / `.csv`
- `continuous-build-32.json` / `.csv`
- `full-loop-4096.json` / `.csv`
- `nonbatch-full-loop-32x32.json` / `.csv`

每个 JSON 都包含场景规模、实体数量、采样参数、机器信息、逐 marker 统计、未解析 marker、
建造诊断和拓扑诊断。对应运行日志位于 `Logs/stage0-perf-*.log`。

## 可重复采集

统一通过 `.codex/skills/unity-launch` 指定的 `Run-UnityBatch.ps1` 调用
`FactoryPerformanceBatchRunner.Run`。固定参数如下：

```text
32x32 steady:
  scene=Perf_4096_Mk4_FullLoop scale=32 load=100 warmup=5 sample=10

build operations:
  scene=Perf_ContinuousBeltBuild scale=32 timeDelay=0.05 drag=0.05
  demolitionOrder=0 sample=10

existing pressure:
  scene=Perf_4096_Mk4_FullLoop scale=64 load=100 warmup=5 sample=10
```

验收每次运行必须同时满足：Unity exit code 0、日志依次出现 Opening/Entering
Play Mode/READY/CAPTURE COMPLETE、JSON 和 CSV 同时生成，且 `sampledFrames`、
frame statistics、metrics 与诊断字段非空。

## 存档审计

仓库当前没有正式 SaveGame/Persistence 系统、存档版本常量或迁移样本；只有规划文档中的
未来存档设计。因此阶段 0 不冻结旧存档版本，也没有迁移 fixture。引入正式存档前必须先
定义版本号和至少一个 Level 0 单层迁移样本。
