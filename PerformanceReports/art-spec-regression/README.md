# 建筑 3×3 规格回归验证

日期：2026-11-11。Unity 6000.3.19f1，Batch Mode，启用图形设备（RTX 4060 Laptop GPU）。
这是短时功能回归采样，不用于与历史性能基线比较；两个场景均预热 1 秒。
两个进程均 ExitCode=0、TimedOut=False，依次记录 Opening batch scene、Entering Play Mode、READY、CAPTURE COMPLETE。

| 场景 | 参数 | 采样秒 | 平均帧 ms | P95 ms | Tick/s | 接受传输/s | 功能结果 |
|---|---|---:|---:|---:|---:|---:|---|
| Perf_4096_Mk4_Blocking | scale=64，4096 初始物品（满载主带） | 3 | 1.817 | 2.663 | 65.66 | 4093.56 | 4097 条带、1 箱，箱内 4 件、运输中 4092 件 |
| Perf_ProducerConsumer | scale=64，初始物品为 0 | 5 | 0.986 | 1.472 | 63.40 | 755.17 | 1024 条带、128 加工器、64 箱，箱内合计 64 件 |

JSON 保存实体数量和 ECS Profiler 指标；同名 CSV 保存逐帧样本。

布局回归测试直接读取当前建筑 CSV，检查完整占地不重叠、不越界，且每个机器端口都连接朝向一致的传送带。
新增 6 个参数化用例覆盖阻塞场景 scale=2/8/64、生产消费场景 scale=1/4/64。
布局测试共 25 项全部通过。

图形模式完整 Factory.Tests：修改前 347 项（337 通过、10 失败），修改后 353 项（343 通过、10 失败）。
失败用例名称列表一致，主要涉及旧测试机器/物品、配方和数据数量断言；未在本次修复中修改这些测试。
测试结果分别位于 TestResults/performance-layout-baseline-graphics.xml 与 TestResults/performance-layout-regression-graphics.xml。
无图形设备的首次测试还触发了编辑器 UI 相关失败，最终采用上述图形模式结果。
