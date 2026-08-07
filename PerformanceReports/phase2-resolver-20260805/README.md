# Phase 2 Resolver 验证记录

采集时间：2026-08-05（Asia/Shanghai）

Unity：6000.3.19f1，Windows Editor EditMode

测试程序集：`Factory.Tests`

## 正确性结果

EditMode Test Runner：`23 passed / 0 failed / 0 skipped`，总耗时
0.256919 秒。

新增的新旧 Resolver 差分覆盖：

- 就绪直线链和下游阻塞；
- Merger round-robin；
- Splitter round-robin；
- Splitter 冲突后的输出回退；
- 满 Belt 环原子移动；
- Grid Revision 不变时拓扑不重建，Revision 变化后只重建一次。

Phase 1 的建筑输入、建筑输出和端口 Owner Revision 测试继续通过。

## 线性工作量验证

测试构造末端阻塞的满载直线网络。每档预热一次，随后采样 5 Tick。
候选检查次数是确定性的复杂度指标；墙钟时间只作为当前 Editor 会话的辅助
数据，不用于跨硬件比较。

| 节点数 | 候选输入检查/Tick | Resolver harness ms/Tick |
|---:|---:|---:|
| 128 | 127 | 0.0315 |
| 512 | 511 | 0.0913 |
| 1024 | 1023 | 0.2554 |
| 4096 | 4095 | 0.7313 |

候选检查次数严格为 `N - 1`，验证候选选择不再执行目标 × 来源的
`O(N²)` 扫描。4096 节点相对 128 节点扩大 32 倍，当前样本耗时扩大约
23.2 倍。

## 范围说明

本记录验证 Phase 2 Resolver 的正确性、缓存失效和算法增长趋势。完整场景中
仍保留 NativeArray 转托管数组和逐 Entity 写回；这些开销属于 Phase 3，
不能只用本次 Resolver harness 数据推断完整 `BeltTransferSystem` 的最终耗时
或 `GC.Alloc = 0 B`。三个完整场景的独立进程采集结果见
[`../phase2-scenes-20260805/README.md`](../phase2-scenes-20260805/README.md)。
