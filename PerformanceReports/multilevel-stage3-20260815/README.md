# 多层稀疏网格阶段 3 验收

日期：2026-08-15（Asia/Shanghai）  
Unity：6000.3.19f1  
硬件：i9-12900HX / RTX 4060 Laptop / 32 GB

## 正确性回归

- EditMode：132/132 通过，包含 14 个阶段 3 专项用例；
- PlayMode：9/9 通过；
- 覆盖负坐标区块换算、256 位占用图、旧矩形迁移、跨区块面剔除、材质分组、
  确定性 Greedy Mesh/Box、统一地基命令事务、承载建筑拒拆、静态 Compound Collider、
  顶面/侧面命中身份恢复，以及 16 轮连续建造/拆除后的 Collider Blob 有界性；
- 完整回归结果见 `TestResults/multilevel-stage3-editmode.xml` 和
  `TestResults/multilevel-stage3-playmode.xml`。

## 真实场景冒烟

使用 `Perf_ProducerConsumer`、8 条生产消费线、1 秒预热和 1 秒采样，验证正式 SubScene
的旧矩形 Surface 初始化、区块表现和 Unity Physics 静态体均能在 Default World 中运行。

| 指标 | 结果 |
| --- | ---: |
| READY Belt / Processor | 128 / 16 |
| Fixed Tick | 67.97 /s |
| 平均帧时间 | 0.830 ms |
| P95 帧时间 | 1.250 ms |
| 采样帧数 | 1201 |
| 接受传输 | 79.97 /s |

原始数据为 `performance-report.json` 和 `performance-report.csv`。该短采样用于阶段 3
运行时冒烟，不替代长期硬件压力基准；Collider Blob 的重复替换/释放由确定性 EditMode
循环覆盖，最终活动 Blob 数保持为 1，没有随 16 轮修改增长。
