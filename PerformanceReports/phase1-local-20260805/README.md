# Phase 1 本机同硬件复测

采集时间：2026-08-05（Asia/Shanghai）  
提交：`78fbd0a 性能优化Phase 1减少GC`  
Unity：6000.3.19f1，Windows Editor Play Mode  
CPU：Intel Core i9-12900HX（24 logical processors）  
GPU：NVIDIA GeForce RTX 4060 Laptop GPU  
RAM：32543 MB  
采集参数：场景 `READY` 后预热 5 秒，采样 10 秒。

本报告与 `4096-baseline` 使用同一台机器、相同 Editor 模式和相同采样器，
因此可用于 Phase 1 前后对照。首次运行强制重新导入了共享 SubScene，确保
`ItemPrefabBakingSystem` 的新烘焙结果已经生效。

## 正确性测试

Unity EditMode Test Runner：`15 passed / 0 failed / 0 skipped`，总耗时
0.397 秒。其中 Phase 1 新增的以下测试全部通过：

- 建筑输入消费就绪物品并发布 Receipt；
- 建筑输出实例化已经预置 `Item` 组件的 Prefab；
- 端口 Owner 顺序只在 Grid Revision 变化后重建。

## 同机基线对比

FixedStep、BeltTransfer 和 GC 均按采样期间的实际 Fixed Tick 数折算。
负数表示下降。

| 场景 | FixedStep ms/Tick | 变化 | BeltTransfer ms/Tick | 变化 | GC KiB/Tick | 变化 |
|:---|---:|---:|---:|---:|---:|---:|
| 4096 半载蛇形 | 14.085 → 14.137 | +0.4% | 13.942 → 13.993 | +0.4% | 848.9 → 717.1 | -15.5% |
| F16 × 256 分叉 | 33.888 → 35.327 | +4.2% | 33.742 → 35.166 | +4.2% | 1556.1 → 1395.7 | -10.3% |
| 4096 满载 + 一级尾带 | 20.155 → 19.678 | -2.4% | 19.797 → 19.390 | -2.1% | 854.9 → 719.9 | -15.8% |

| 场景 | 基线平均帧 | Phase 1 平均帧 | 基线 Tick/s | Phase 1 Tick/s |
|:---|---:|---:|---:|---:|
| 4096 半载蛇形 | 136.836 ms | 83.276 ms | 60.37 | 60.44 |
| F16 × 256 分叉 | 674.283 ms | 688.165 ms | 28.31 | 27.70 |
| 4096 满载 + 一级尾带 | 405.668 ms | 388.468 ms | 47.00 | 49.07 |

平均帧时间会受一个渲染帧内追赶多少个 Fixed Tick 影响。半载场景虽然平均帧
下降 39.1%，但 FixedStep 和 BeltTransfer 的每 Tick 成本均持平，因此不能
解释为模拟核心提速。F 型场景只有 15 个渲染帧样本，约 4% 的耗时上浮应
视为短采样波动，而不是已确认的性能回归。

## 结论

1. Phase 1 达成了“减少低风险托管分配”的目标。三个场景的 GC/Tick 均
   下降，幅度为 10.3%～15.8%，说明 Item Prefab Index、端口 Owner、
   Cell Dictionary 容量和目标保留集合的复用有效。
2. Phase 1 没有降低 Resolver 主路径成本。`BeltTransferSystem` 仍占
   FixedStep 的约 98%～99%，单 Tick 时间在测量误差范围内基本不变。
3. 稳态 GC 仍为 0.70～1.36 MiB/Tick，尚未满足 `0 B/Tick`。主要剩余来源：
   Native 快照转托管数组，以及每轮 Resolve 创建 Node、Dictionary、候选、
   accepted 和 loop/path 容器。
4. F 型场景仍只有 27.70 Tick/s，阻塞场景为 49.07 Tick/s；两者均无法
   达到 60 Hz。下一步必须进入 Phase 2 的持久化拓扑和 `O(N)` Resolver。
5. `GridPlacementTransformSystem` 的每帧 Marker 均值约从 0.023 ms 降到
   0.006 ms，Revision 门控生效，但其绝对占比很小。

## 当前测试矩阵的覆盖限制

- 半载和 F 型场景没有生产建筑输出；阻塞场景只有仓库输入。因此本轮只能
  通过回归测试验证“Prefab 已预置 Item”，无法量化高吞吐 Instantiate 路径
  的收益。
- 性能场景会禁用 `EcsGridInteractionController`，所以其 Query/Singleton
  缓存不在本轮 Profiler 数据内。
- 建议补充“多个生产建筑持续输出并由下游消费”的结构变化场景，单独衡量
  Item Prefab 与后续对象池优化。

## 原始数据

- `Perf_4096_Mk4_HalfLoaded.json` / `.csv`
- `Perf_F16_Mk4_1024Items.json` / `.csv`
- `Perf_4096_Mk4_Blocking.json` / `.csv`

JSON 保存硬件、实体数量、吞吐与 Profiler 汇总；CSV 保存逐渲染帧数据。
