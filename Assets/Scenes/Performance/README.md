# ECS 性能测试场景

打开任意场景并进入 Play Mode。场景会以 Additive 方式加载共享的
`Stage3Ecs` 场景，复用正式的 SubScene、数据库、建造系统、视觉 Prefab
和运行时 ECS 系统。

初始化完成后左上角显示 `READY - start Profiler capture now`，Console
同时输出一条 `[ECS Performance] READY` 日志。应从此时开始采集
Profiler，场景加载和批量建造尖峰不属于稳态模拟样本。

## 运行策略

默认只运行 `4096` 参数的固定规模场景，例如 `Perf_4096_Mk4_FullLoop` 或
`Perf_Straight_Scalable` 的 4096 节点配置。压力测试只有在用户明确要求时才执行，
结果保存在 `Docs/PerformanceReports/StressTest/` 下。

## 场景清单

### Perf_4096_Mk4_HalfLoaded

- 默认网格：`64 × 64`，可通过 `-factoryPerformanceScale` 设置边长
- 默认 4096 格四级传送带，按蛇形填满整个网格；边长为 `scale` 时共 `scale²` 格
- 默认 2048 个铁矿石按奇偶格交错放置，形成 50% 初始占用
- 无分叉、无接收建筑

交错放置用于在测试开始时制造尽可能多的并发移动请求；尾端无输出，
因此长时间运行后网络会逐渐完全阻塞。

### Perf_F16_Mk4_1024Items

- 网格：`96 × 96`
- 左下角 `32 × 32` 蛇形输入主干，初始满载 1024 个铁矿石
- 16 个 Splitter 组成竖直主脊
- 每条分支恰好 256 格四级传送带，并折叠为 `64 × 4`
- 四级传送带总数 5165，Splitter 总数 16

数据库目前只有一级 Splitter，因此“都是四级”应用于所有普通传送带；
Splitter 使用数据库中唯一的 `splitter_mk1`。

该场景保持固定布局，`-factoryPerformanceScale` 会被忽略。

### Perf_4096_Mk4_Blocking

- 默认网格：`67 × 64`
- 默认 4096 格四级传送带折叠为 `64 × 64`，初始全部满载
- 可通过 `-factoryPerformanceScale` 设置主蛇形边长，默认 `64`，从 `2` 起无硬上限
- 尾部连接 1 格一级传送带
- 一级传送带进入旋转 180 度的一级储物箱

四级主带速度为 8 格/秒，一级尾带速度为 1 格/秒。储物箱容量为 100，
用于持续制造快速主干、慢速出口之间的移动与反压，最终在仓库装满后
形成完全阻塞。

### Perf_Straight_Scalable

- 默认 128 格四级直线传送带，50% 均匀装载
- 使用 `-factoryPerformanceScale` 或兼容参数 `-factoryPerformanceNodeCount`
  设置节点数，从 `2` 起无硬上限
- 使用 `-factoryPerformanceLoadPercent` 设置初始装载率，范围 `0～100`
- 推荐矩阵：`128 / 512 / 1024 / 4096` × `0 / 50 / 100%`

该场景用于隔离节点规模、装载率、Job 调度固定成本和线性增长趋势。

### Perf_512_MixedJunction

- 默认 462 格四级传送带、25 个 Merger、25 个 Splitter，共 512 个运输节点
- 可通过 `-factoryPerformanceScale` 设置模块网格每边模块数，默认 `5`，
  从 `1` 起无硬上限
- 25 个相互隔离的闭合三路分流/三路合流模块，另有 1 个 62 格闭环，
  Junction 比例约 9.8%
- 默认 50% 确定性交错装载，使 Splitter 输出和 Merger 输入竞争同时活跃
- 可使用 `-factoryPerformanceLoadPercent 100` 测量完全阻塞的 Junction 网络

所有模块均为闭环，长时间预热后仍会持续经过 Splitter 和 Merger，避免开放
链路排空后退化为静态阻塞样本。

### Perf_4096_Mk4_FullLoop

- 默认 `64 × 64` Hamilton 环，4096 格四级传送带
- 可通过 `-factoryPerformanceScale` 设置边长，默认 `64`，从 `2` 起无硬上限，
  且必须为偶数
- 默认 100% 满载，末端重新连接起点
- 同一 Ready Tick 可原子提交全部 4096 个物品，用于测量高密度状态写回
- 可使用 `-factoryPerformanceLoadPercent` 创建同拓扑的部分装载对照

### Perf_ProducerConsumer

- 默认 64 条相互独立的持续生产线
- 可通过 `-factoryPerformanceScale` 设置生产线数，默认 `64`，从 `1` 起无硬上限
- 每条为 `Miner → 8 Mk4 Belt → Furnace → 8 Mk4 Belt → Storage`
- 合计 128 个 Processor、64 个 Storage 和 1024 格四级传送带
- Miner 持续产生铁矿石，Furnace 冶炼铁锭，Storage 持续消费运输实体

该场景用于覆盖建筑端口、Receipt Buffer、Item Instantiate/Destroy 和 ECB
Playback，不与纯 Resolver 场景的结果混合解释。

### Perf_ContinuousBeltBuild

- 默认 `scale=64`，网格 `64 × 127`
- 依次模拟 `scale` 次完整传送带输入：点击起点、在
  `-factoryPerformanceDragSeconds` 内从起点匀速拖动到终点（放置预览随拖动
  逐渐变长）、点击终点提交整条 `scale` 格长的 `PlaceBeltPath`
- 两次放置之间和每次拆除之间间隔 `-factoryPerformanceTimeDelay` 秒，默认 `0.25`
- 放置完成后按 `-factoryPerformanceDemolitionOrder` 逐条拆除：
  `0` 按放置顺序正序，`1` 倒序，默认 `0`
- 总传送带节点数在满载时为 `scale²`，默认 4096

该场景专门覆盖 Phase 5 的连续拖动建造和连续拆除 Belt Line 尖峰；报告会记录
`timeDelaySeconds`、`dragSeconds`、`demolitionOrder`、已完成放置/拆除数量，
`peakBeltEntities`（满载时 `scale²`），并新增 `grid_build`、
`grid_occupancy_index`、`belt_topology_visual` Profiler 摘要。

## 参数化运行

除 F16 外的场景都支持 `-factoryPerformanceScale`；未传入时使用上述默认值。
`scale` 的含义由场景决定，报告中的 `scale` / `scaleUnit` 会记录实际使用的
主规模参数。例如：

```text
-factoryPerformanceScene Perf_4096_Mk4_FullLoop
-factoryPerformanceScale 128
-factoryPerformanceLoadPercent 100
```

`-factoryPerformanceScale` 的场景含义：

| 场景 | scale 含义 | 默认 | 最小值 |
|---|---|---:|---:|
| `Perf_4096_Mk4_HalfLoaded` | 正方形网格边长 | 64 | 2 |
| `Perf_4096_Mk4_Blocking` | 主蛇形边长 | 64 | 2 |
| `Perf_Straight_Scalable` | 传送带节点数 | 128 | 2 |
| `Perf_512_MixedJunction` | 模块网格每边模块数 | 5 | 1 |
| `Perf_4096_Mk4_FullLoop` | Hamilton 环边长 | 64 | 2（须为偶数） |
| `Perf_ProducerConsumer` | 生产线数 | 64 | 1 |
| `Perf_ContinuousBeltBuild` | 传送带条数，每条长度同为该值 | 64 | 1 |

以上场景不再设置 scale 上限，实际可运行规模只受内存、构建超时和 ECS 网格
尺寸限制。

`Perf_512_MixedJunction` 和 `Perf_4096_Mk4_FullLoop` 支持装载率参数；
`Perf_Straight_Scalable` 也支持装载率。`Perf_4096_Mk4_HalfLoaded` 和
`Perf_4096_Mk4_Blocking` 使用场景自身定义的交错/满载装载方式，
`Perf_ProducerConsumer` 使用持续生产，均不读取装载率。

## 采样建议

1. 关闭 Deep Profile。
2. 等待 READY 后再开始采样。
3. 同时记录 Main Thread、Job Worker、GC Alloc、Structural Changes 和
   FixedStepSimulationSystemGroup。
4. 每个场景至少采集 10 秒。
5. 阻塞场景如需观察仓库装满后的稳态，应运行至少 110 秒。
