# ECS 4096 规模性能基线

采集时间：2026-08-05（Asia/Shanghai）  
Unity：6000.3.19f1，Windows Editor  
CPU：Intel Core i9-12900HX（24 logical processors）  
GPU：NVIDIA GeForce RTX 4060 Laptop GPU  
采集方式：场景进入 `READY` 后预热 5 秒，再使用 `ProfilerRecorder` 采样 10 秒。

> 当前许可证不包含 `com.unity.editor.headless`，因此数据来自普通 Editor Play Mode，
> 不是无开发开销的 Player 构建。帧时间包含 Editor、渲染和 Fixed Tick 追帧；
> `BeltTransferSystem`、FixedStep 和每 Tick GC 折算值更适合判断模拟瓶颈。

## 汇总

| 场景 | 平均帧 / P95 | FPS | Fixed Tick/s | FixedStep ms/Tick | BeltTransfer ms/Tick | GC MB/Tick | 成功传输/Tick |
|:---|---:|---:|---:|---:|---:|---:|---:|
| 4096 半载蛇形 | 136.836 / 195.741 ms | 7.34 | 60.37 | 14.085 | 13.942 | 0.829 | 245.91 |
| F16 × 256 分叉 | 674.283 / 711.849 ms | 1.49 | 28.31 | 33.888 | 33.742 | 1.520 | 129.60 |
| 4096 满载 + 一级尾带 | 405.668 / 439.380 ms | 2.47 | 47.00 | 20.155 | 19.797 | 0.835 | 68.86 |

60 Hz 的单 Tick 预算是 16.667 ms。半载蛇形勉强低于逻辑预算，但
渲染帧需要连续追赶约 8 个 Fixed Tick；F 型和阻塞场景的单 Tick 已分别
超出预算约 103% 和 21%。

## 关键发现

1. `BeltTransferSystem` 占 FixedStep 时间的 98%～99%，是决定性瓶颈。
   `BeltProgressSystem` 和 `BeltItemPositionSystem` 都远低于 1 ms/渲染帧，
   不是当前优先优化对象。
2. Resolver 的候选选择对每个目标再次扫描全部来源，形成 `O(N²)`；
   F 型场景还有 16 个 Splitter，使 `BeltTransferSystem` 最多执行 17 轮
   Resolve，因此达到 33.742 ms/Tick。
3. 每次 Resolve 都创建 Node 数组、Cell Dictionary、候选数组、Loop 路径
   和临时 List；外层系统又把 Native 快照复制为托管数组。实测每 Tick
   产生约 0.83～1.52 MB GC 分配，离稳态 `0 B` 目标很远。
4. 阻塞场景平均每 Tick 发出 3657.20 个请求，仅接受 68.86 个，接受率
   只有 1.88%。当前实现仍为绝大多数必然失败的请求执行完整扫描和分配。
5. F 型场景每 Tick 请求接受率为 100%，但仍是最慢场景，说明成本主要
   来自节点规模、Junction 多轮解析和全图算法，而不是失败分支本身。

## 优化优先级

1. 将目标候选选择改为按 `TargetIndex` 一次分桶/归约的 `O(N)` 算法。
2. 将 Cell → Node、连接、输出和环路信息缓存为由 Grid Revision 驱动的
   持久化 Native 拓扑，禁止在每 Tick、每 Resolve pass 重建。
3. 消除 NativeArray → 托管 Array 复制及 Resolver 内的托管数组、
   Dictionary、List；使用复用的 Native 容器并 Burst 编译。
4. 将 Junction 传播改为单次拓扑序/队列求解，移除
   `mergers + splitters + 1` 次全图 Resolve。
5. 只写回状态变化的节点，替代每 Tick 对全部 Belt/Merger/Splitter 调用
   `EntityManager.SetComponentData`。
6. 第一轮优化后使用同一采集器复测；最低验收为稳态 GC `0 B/Tick`，
   三场景 `BeltTransferSystem < 5 ms/Tick`。

## 原始数据

- `Perf_4096_Mk4_HalfLoaded.json` / `.csv`
- `Perf_F16_Mk4_1024Items.json` / `.csv`
- `Perf_4096_Mk4_Blocking.json` / `.csv`

JSON 保存环境、实体数量、吞吐和 Profiler 汇总；CSV 保存逐渲染帧样本。
