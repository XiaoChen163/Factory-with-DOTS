# ECS 性能测试场景

打开任意场景并进入 Play Mode。场景会以 Additive 方式加载共享的
`Stage3Ecs` 场景，复用正式的 SubScene、数据库、建造系统、视觉 Prefab
和运行时 ECS 系统。

初始化完成后左上角显示 `READY - start Profiler capture now`，Console
同时输出一条 `[ECS Performance] READY` 日志。应从此时开始采集
Profiler，场景加载和批量建造尖峰不属于稳态模拟样本。

## 场景清单

### Perf_4096_Mk4_HalfLoaded

- 网格：`64 × 64`
- 4096 格四级传送带，按蛇形填满整个网格
- 2048 个铁矿石按奇偶格交错放置，形成 50% 初始占用
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

### Perf_4096_Mk4_Blocking

- 网格：`67 × 64`
- 4096 格四级传送带折叠为 `64 × 64`，初始全部满载
- 尾部连接 1 格一级传送带
- 一级传送带进入旋转 180 度的一级储物箱

四级主带速度为 8 格/秒，一级尾带速度为 1 格/秒。储物箱容量为 100，
用于持续制造快速主干、慢速出口之间的移动与反压，最终在仓库装满后
形成完全阻塞。

## 采样建议

1. 关闭 Deep Profile。
2. 等待 READY 后再开始采样。
3. 同时记录 Main Thread、Job Worker、GC Alloc、Structural Changes 和
   FixedStepSimulationSystemGroup。
4. 每个场景至少采集 10 秒。
5. 阻塞场景如需观察仓库装满后的稳态，应运行至少 110 秒。
