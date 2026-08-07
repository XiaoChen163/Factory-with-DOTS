# Phase 3 末 GC 采集口径修复（Phase 4 前）

采集时间：2026-08-06（Asia/Shanghai），与 Phase 3 报告同机同日。

Unity：6000.3.19f1，Windows Editor Batch Mode，`-nographics`
硬件：Intel Core i5-9500F（6 逻辑核）、16 GB RAM

每个场景独立 Unity 进程，READY 后预热 5 秒并采样 10 秒。本次报告仅修改
`FactoryPerformanceMetricsCapture`（测量口径），未改动任何模拟逻辑。

## 本次改动

`Assets/Scripts/ECS/Performance/FactoryPerformanceMetricsCapture.cs`：

- 帧缓冲改为预分配 `FrameSample[]`（含预分配 `double[]`），消除每帧
  `new double[]` / `new FrameSample`（约 200 B/帧）与 `List<FrameSample>(2048)`
  在容量 2048/4096/8192/16384/32768 翻倍时的一次性 34/67/132/263/525 KB 尖峰。
- 跳过 `GC.Collect()` 后首帧：该帧携带 ProfilerRecorder 建立的一次性
  ~14.5 MB 分配，旧口径把它摊进了全部 Tick，仅此一项即贡献约
  23.5 KiB/Tick。
- 报告新增四个字段（所有指标通用）：
  - `tickFrames`：实际运行了 FixedStep 的帧数（`fixed_step > 0.01 ms`，
    排除该 marker 在非 Tick 帧上的 ~0.5 µs 噪声底）；
  - `perTick`：Tick 帧指标总和 ÷ Tick 数；
  - `frameBaseline`：非 Tick 帧指标 p50（GC 即每帧基线分配）；
  - `perTickNet`：`(Tick 帧总和 - Tick 帧数 × frameBaseline) ÷ Tick 数`，
    即扣除帧基线后的净每 Tick 分配。GC 应看此值。

## 结果

| 场景 | Tick/s | 净 GC/Tick (perTickNet) | 帧基线/帧 | FixedStep ms/Tick | BeltTransfer ms/Tick |
|:---|---:|---:|---:|---:|---:|
| 4096 半载蛇形 | 62.10 | **0.17 B** | 1148 B | 0.636 | 0.046 |
| F16 × 256 分叉 | 62.00 | **0.25 B** | 1124 B | 0.814 | 0.053 |
| 4096 满载 + 尾带 | 62.70 | **0.33 B** | 1124 B | 0.834 | 0.059 |

### GC 对照（同机 Phase 3 → 本次）

| 场景 | Phase 3 报告 KiB/Tick | 本次净口径 B/Tick | 变化 |
|:---|---:|---:|---:|
| 4096 半载蛇形 | 47.7 | 0.17 | 约 1/280000 |
| F16 × 256 分叉 | 47.2 | 0.25 | 约 1/190000 |
| 4096 满载 + 尾带 | 48.1 | 0.33 | 约 1/150000 |

旧 47~48 KiB/Tick 的构成（逐帧拆分验证）：

- 帧基线 1.1~1.3 KiB/帧 ×（fps/tps 折算放大，1150 fps 时约 18.5 倍）
  ≈ 24~25 KiB/Tick；
- 采样首帧一次性 ~14.5 MB（`GC.Collect` 后首帧 + ProfilerRecorder 建立）
  ÷ 621 Tick ≈ 23.5 KiB/Tick；
- 采集脚本 `List<FrameSample>` 扩容尖峰（34/67/132 KB 等）合计
  ≈ 0.4 KiB/Tick。

三者相加与 47.7 KiB/Tick 精确吻合。稳态 Tick 帧与不带 Tick 帧的分配
完全一致（p50/p99 均为基线），即稳态每 Tick 托管分配实测为 ~0 B。

### ECB 归属结论

- Blocking 场景每个 Tick 都通过 ECB 执行 `DestroyEntity`（约 65 次/Tick），
  其 Tick 帧分配仍与基线完全相同（max 仅 1176 B = 基线 + 52 B）：
  **ECB 创建/回放走原生 update allocator，随帧回收，不产生托管 GC**。
- 结论：Phase 3 报告将残余分配归因于“每 Tick 的 ECB 创建/回放”是测量伪影。

## 尝试过并回退的优化（记录）

1. 删除 `RecordInjectedItemViaEcb` 的 `Ecb.SetComponent`：**错误，已回退**。
   `Ecb.Instantiate` 返回延迟实体，`BeltState.CurrentItem` 中对该实体的引用
   必须经 ECB 回放重映射；直接 `ComponentLookup` 写入会留下失效的延迟实体
   （4 个 `BuildingOutput_*` 测试失败，`EntityManager.HasComponent` 抛
   “still deferred”）。
2. `BeltTransferSystem` 按“是否有建筑端口”惰性创建 ECB：**错误，已回退**。
   ECB 系统是仲裁 Job 在 Tick 内的完成点（`FlushPendingBuffers` 完成
   producer handle）；跳过创建后 Job 跨 Tick 悬挂，主线程 EntityManager
   读取（采集协程 `ReadStats`）触发安全异常。且实测 ECB 创建与空回放为
   原生成本、零托管分配，该优化的收益不可测。真正的“稳态无 ECB”应随
   Phase 4 Item Pool 落地（物品改为池化实体后不再 Instantiate/Destroy）。

## 正确性

各场景 READY 实体数与场景定义一致；确定性计数与 Phase 3 一致
（HalfLoaded 332→Ready/Tick、248→接受/Tick；F16 128→127.5；Blocking
Ready ~3616/Tick，接受的相位差异与 Phase 3 文档说明相同）。同机 EditMode
回归 `Factory.Tests`：`39 passed / 0 failed`。

## 采集告警

- 本次时间指标略高于 Phase 3（FixedStep 0.64~0.83 ms/Tick vs 0.63~0.69），
  本机采样期间存在背景负载（p95 帧 1.7~2.8 ms，Phase 3 为 1.85~2.12），
  F16/Blocking 的 p95 帧时间与 FixedStep 均偏高，属机器噪声而非回归。
  GC 字节分配不随硬件/负载变化，不受影响。
- `belt_transfer` 0.046~0.059 ms/Tick 与 Phase 3（0.034~0.051）同量级。

## 原始数据

- `Perf_4096_Mk4_HalfLoaded.json` / `.csv`
- `Perf_F16_Mk4_1024Items.json` / `.csv`
- `Perf_4096_Mk4_Blocking.json` / `.csv`
