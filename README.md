# Factory-with-DOTS

基于 **Unity DOTS**（Entities 1.4 / Burst / Baking）构建的 3D 工厂物流游戏技术验证项目。项目从纯 GameObject 原型（Stage 0）起步，逐步演进为完整的 ECS 实现（Stage 3）：固定 Tick 模拟、Burst 编译的传送仲裁、O(N) 传送网络解析，以及覆盖 4096 节点规模的自动化性能基准。

> 核心思路：**逻辑与表现分离**，ECS 组件是正式运行时状态的唯一来源，GameObject 只负责创作（Authoring/Baking）与交互表现。

---

## ✨ 技术亮点

### 1. 确定性的传送带物流模拟

物流规则以"一格一物品"为不变量：每个传送带格最多持有一个物品，仅保存 `CurrentItem + Progress`，不使用物品 List、槽位数组或 `DynamicBuffer<BeltSlot>`。

- **固定 Tick（60 Tick/s）**：所有逻辑状态只在固定模拟组中推进，不受渲染帧率影响；物品移动表现独立插值。
- **统一快照 + 两阶段提交**：跨节点移动先生成请求快照，再统一冲突仲裁，最后一次性提交，杜绝物品丢失、复制或覆盖。
- **确定性仲裁**：合流器 / 分流器使用与 Entity 创建顺序无关的 round-robin；同一目标每 Tick 最多接受一个来源；满环支持原子移动。
- **可验证性**：仲裁结果不依赖 Job 线程执行顺序、HashMap 枚举顺序或 Chunk 顺序；`Factory.Tests` 内置 **39 个 EditMode 回归测试**，包括新旧 Resolver 差分测试、环路原子移动、round-robin、阻塞反压与拓扑失效测试（见 `Docs/Automated-Regression-Testing-Guide.md`）。

### 2. 传送解析从 O(N²) 到 O(N) 的 Burst 性能工程

早期 `BeltTransferSystem` 每 Tick 扫描"目标 × 来源"（`O(N²)`），并反复重建托管 Dictionary / 数组，导致 4096 节点场景单 Tick 超过 20 ms、每 Tick 分配约 0.8~1.5 MB GC。项目通过四步改造将其变为稳态、可扩展的实现：

- **持久化 Native 拓扑**：`Cell → Node → 连接` 的邻接结构缓存在 `FactoryLinearTransferResolver` 中，**仅在 Grid Revision 变化时重建**（测试断言：Revision 不变重建 1 次、变化后重建 2 次）。
- **线性候选选择**：按目标索引一次分桶/归约，候选输入检查严格为 `N - 1`（4096 节点为 4095），单轮路由 pass，消除 `O(N²)` 全图扫描。
- **Burst 仲裁 Job + 延迟 ECB**：`FactoryTransferArbitrationJob`（`[BurstCompile]`）完成全部仲裁，结果经 `TransferCommandBufferSystem` 延迟 Playback，主线程不再成为串行汇合点。
- **组件布局拆分与 GC 清理**：静态拓扑（Cell/Direction）与高频状态（CurrentItem/Progress）分离；移除托管数组、Dictionary、List，稳态 Tick 分配从 ~700 KiB 降至 **47~48 KiB/Tick**。

**同机（i5-9500F）Phase 1 → Phase 3 对比：**

| 场景 | BeltTransfer ms/Tick | FixedStep ms/Tick | GC KiB/Tick | 60 Tick/s |
|:---|---:|---:|---:|:---:|
| 4096 半载蛇形 | 20.147 → **0.046**（-99.8%） | 20.190 → **0.630**（-96.9%） | 717.8 → **47.7**（-93.4%） | ✅ |
| F16 × 256 分叉 | 58.020 → **0.034**（-99.9%） | 58.067 → **0.688**（-98.8%） | 1366.0 → **47.2**（-96.5%） | ✅ |
| 4096 满载 + 尾带 | 28.334 → **0.040**（-99.9%） | 28.405 → **0.646**（-97.7%） | 726.0 → **48.1**（-93.4%） | ✅ |

Phase 1 三个场景均无法维持 60 Tick/s；Phase 3 在更弱的 i5-9500F 上全部稳定达到 **61.8~62.1 Tick/s**，且行为等价（每 Tick 传输量 ±1% 内）。

---

## 功能特性

- 网格建造：3D 场景中的 2D 网格（`GridBuildCommandSystem`、`GridOccupancyIndexSystem`），建筑支持多格占地与输入/输出端口。
- 完整物流链路：矿机 → 传送带 → 合流器/分流器 → 熔炉（配方加工）→ 储物箱，物品连续移动表现与 1 格 1 物品逻辑容量并存。
- 数据驱动：建筑 / 物品 / 配方由 CSV（`Assets/Data/FactoryTables`）经 `FactoryDatabaseCsvImporter` 导入为 ScriptableObject，再经 Baking 管线转为 ECS 组件。
- 性能场景矩阵：7 个可重复的性能场景（4096 蛇形、F16 分叉、满载阻塞、满环原子移动、混合 Junction、Producer-Consumer、可扩展直线），支持批处理采集 JSON/CSV 报告。
- 渐进式架构对照：`Assets/Scripts/Prototype`（MonoBehaviour 原型）与 `Assets/Scripts/ECS`（正式实现）并存，便于对比 DOTS 重构收益。

## 技术栈

| 组件 | 版本 |
|:---|:---|
| Unity Editor | 6000.3.19f1 |
| Entities | 1.4.8 |
| Entities Graphics | 1.4.21 |
| Universal RP | 17.3.0 |
| Input System | 1.19.0 |
| Unity Test Framework | 1.6.0 |

固定模拟频率 60 Tick/s；正式网格 32 × 32，性能场景网格 64 × 64 ~ 96 × 96（最多 5165 传送节点）。

## 项目结构

```text
Assets/
├─ Scripts/
│  ├─ Prototype/        # Stage 0~1：纯 GameObject 原型（逻辑/表现分离、固定 Tick）
│  ├─ ECS/
│  │  ├─ Authoring/     # Authoring 组件与 Baking 系统（Prefab Catalog、Item、Belt Visual）
│  │  ├─ Data/          # ECS 组件定义（Belt/BeltState/Merger/Splitter/Item/端口 Buffer）
│  │  ├─ Systems/       # 传送仲裁、拓扑、进度、加工、端口交换、网格建造等系统
│  │  ├─ Presentation/  # 网格交互控制器、HUD
│  │  └─ Performance/   # 性能场景引导与 Profiler 指标采集
│  ├─ Testing/          # Factory.Tests EditMode 测试与夹具
│  └─ Data/             # 工厂数据库 ScriptableObject 与 CSV 导入器
├─ Data/FactoryTables/  # 建筑 / 物品 / 配方 CSV 数据
├─ Scenes/
│  ├─ Stage0Prototype.unity
│  ├─ Stage3Ecs.unity / Stage3EntitiesSubscene.unity
│  └─ Performance/      # 7 个性能基准场景
Docs/                   # 设计文档、回归测试指南、多阶段性能报告
```

核心系统：

- `BeltTransferSystem` — 传送仲裁入口，按 Grid Revision 失效/重建拓扑，调度 Burst 仲裁 Job
- `FactoryLinearTransferResolver` + `FactoryTransferArbitrationJob` — O(N) 拓扑解析与确定性仲裁
- `TransferCommandBufferSystem` — 延迟 ECB Playback，避免主线程汇合
- `BeltProgressSystem` / `ItemProcessSystem` / `ItemPortAdapterSystem` / `ItemPortBufferSwapSystem` — 进度、加工与建筑端口交换
- `GridBuildCommandSystem` / `GridOccupancyIndexSystem` / `GridPlacementTransformSystem` — 建造与网格占用

## 快速开始

1. 使用 **Unity 6000.3.19f1** 打开项目根目录，等待 Entities 包与 SubScene 导入完成。
2. 打开 `Assets/Scenes/Stage3Ecs.unity` 进入 Play Mode 体验完整 ECS 物流模拟。
3. 打开 `Assets/Scenes/Stage0Prototype.unity` 可对照查看 GameObject 原型版本。
4. 性能测试：打开 `Assets/Scenes/Performance/` 下任意场景并进入 Play Mode，等待左上角显示 `READY` 后开始采集 Profiler（场景说明见 `Assets/Scenes/Performance/README.md`）。

## 自动化测试

物流模拟的正确性高度依赖固定 Tick 时序与确定性仲裁，因此项目把自动化回归测试作为第一道防线：测试程序集 `Factory.Tests`（EditMode，共 39 个用例）从纯逻辑 Resolver 一直覆盖到真实 ECS 系统层。

### 分层策略

- **Resolver 层（纯逻辑）**：`TransportScenario` 构造节点快照，用同一份输入分别驱动旧 Resolver、线性 Resolver 与 Burst 仲裁 Job，断言输出状态等价——保证性能重构不改变玩法语义。
- **系统层（真实 World）**：`FactoryWorldFixture` 为每个测试创建独立 ECS World 并推进固定 Tick，验证组件拆分、ECB 延迟 Playback、统计写入等集成行为。
- **场景布局层**：校验 7 个性能场景的实体规模与拓扑约束（节点数、Junction 比例、环闭合等），防止基准场景漂移后性能数据失真。

### 测试分布

| 测试类 | 数量 | 覆盖内容 |
|:---|---:|:---|
| `BeltTransferResolverTests` | 7 | 旧 Resolver：就绪判定、统一快照移动、阻塞反压、Merger round-robin、与插入顺序无关 |
| `BeltTransferResolverPhase2Tests` | 8 | 新 Resolver 差分与复杂度：严格 `N - 1` 候选检查、单轮路由、Grid Revision 失效 |
| `BeltTransferResolverPhase3Tests` | 3 | Burst 仲裁 Job：Ready 计数、满环原子旋转、Splitter cursor 推进 |
| `BeltTransferSystemPhase1Tests` | 5 | 建筑端口：输入消费、输出实例化、组件注入、Revision 重建 |
| `BeltTransferSystemPhase3Tests` | 5 | 组件拆分、多 Tick 链路不丢物、统计写入、ECB Playback 生命周期 |
| `PerformanceScenarioLayoutTests` | 9 | 性能场景的网格 / 节点 / Junction / 环闭合约束 |
| `FixtureSmokeTests` | 2 | World 夹具可创建、可销毁 |

### 测试基础设施

- `FactoryWorldFixture`：每个测试独立的 `World`，`TearDown` 时 `CompleteAllTrackedJobs` 并释放，不污染生产 `DefaultWorld`。
- `TransportScenario`：同一份快照可同时驱动旧 Resolver、线性 Resolver 与 Burst Job，并暴露 `TopologyRebuildCount`、`CandidateInspectionCount`、`RoutingPassCount` 等复杂度指标。
- `TransportAssertions`：物品集合守恒断言（无丢失、无复制）与新旧实现状态等价断言。
- `FactoryTestRunRequest`：命令行运行入口，支持整程序集 EditMode 测试，结果写入 `Temp/Factory.Tests.result`，便于 CI 接入。

### 如何运行

- **编辑器**：Unity Test Runner → `Factory.Tests`（EditMode）运行全部 39 个用例。
- **命令行 / CI**：通过 `-executeMethod` 调用 `FactoryTestRunRequest.RunFromCommandLine()`，或创建 `Temp/Factory.Tests.run` 触发文件，编译完成后自动执行并退出。
- **提交门槛**：修改 `Assets/Scripts` 后必须完整运行 `Factory.Tests` 且 `failed=0`；传送 / 仲裁改动还需保证旧 / 新 Resolver 差分用例通过。
- 完整规范（目录、命名、按修改类型选择测试）见 `Docs/Automated-Regression-Testing-Guide.md`。

## 文档

- `Docs/Factory-with-DOTS.md` — 渐进式五阶段实现方案（设计意图与不变量）
- `Docs/ECS-Performance-Optimization-Plan.md` — ECS 性能优化策略（瓶颈分析、目标架构）
- `Docs/PerformanceReports/` — Phase 1~3 性能基准与 Resolver 验证记录（含原始 JSON/CSV）
- `Docs/Automated-Regression-Testing-Guide.md` — 自动化回归测试规范
- `Docs/Stage0-Test-Checklist.md` — 原型阶段测试清单
