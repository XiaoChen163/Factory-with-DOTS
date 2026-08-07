# 自动化性能与压力测试指南

## 1. 目的

本文档描述 Factory-with-DOTS 的自动化性能测试和压力测试方法。性能测试用于
在固定规模下比较不同优化阶段的稳态指标；压力测试使用倍增法不断扩大场景规模，
直到 `tps` 或 `fps` 低于阈值，记录当时的规模、吞吐、帧时间、GC、内存和
Profiler 摘要。

默认情况下，自动化流程只运行 `4096` 参数的固定规模场景，例如
`Perf_4096_Mk4_FullLoop` 或 `Perf_Straight_Scalable` 的 4096 节点配置。
压力测试只有在用户明确要求时才执行，不作为默认性能回归步骤。

## 2. 测试范围与参数矩阵

除 `Perf_F16_Mk4_1024Items` 保持固定布局外，其余性能场景都接受
`-factoryPerformanceScale`。`scale` 的具体含义由场景决定：

| 场景 | scale 含义 | 默认 | 最小值 |
|---|---|---:|---:|
| `Perf_4096_Mk4_HalfLoaded` | 正方形网格边长 | 64 | 2 |
| `Perf_4096_Mk4_Blocking` | 主蛇形边长 | 64 | 2 |
| `Perf_Straight_Scalable` | 传送带节点数 | 128 | 2 |
| `Perf_512_MixedJunction` | 模块网格每边模块数 | 5 | 1 |
| `Perf_4096_Mk4_FullLoop` | Hamilton 环边长 | 64 | 2（须为偶数） |
| `Perf_ProducerConsumer` | 生产线数 | 64 | 1 |
| `Perf_ContinuousBeltBuild` | 传送带条数，每条长度同为该值 | 64 | 1 |

未传入 `-factoryPerformanceScale` 时，所有场景仍使用原有固定默认值，因此旧报告
和新报告可以继续对比。报告中的 `scale` 与 `scaleUnit` 字段会记录本次实际规模。
所有场景不再设置代码级 scale 上限，实际可运行规模只受内存、构建超时和 ECS
网格尺寸限制。

`Perf_ContinuousBeltBuild` 是 Phase 5 的建造尖峰场景。它模拟玩家连续放置
传送带：点击起点、从起点匀速拖动到终点让放置预览逐渐变长、点击终点提交
`PlaceBeltPath`；放置完 `scale` 条后，再按 `demolitionOrder` 逐条拆除
`RemoveBeltLine`。相关参数：

| 参数 | 默认 | 说明 |
|---|---|---:|
| `-factoryPerformanceTimeDelay` | `0.25` | 两次放置之间以及每次拆除之间的间隔秒数 |
| `-factoryPerformanceDragSeconds` | `0.25` | 起点到终点的匀速拖动秒数 |
| `-factoryPerformanceDemolitionOrder` | `0` | 拆除顺序，`0` 正序，`1` 倒序 |

## 3. 单场景性能采样

单次采样仍使用现有的 `FactoryPerformanceBatchRunner` 管线。以
`Perf_4096_Mk4_FullLoop` 为例：

```powershell
$unityArgs = @(
  '-batchmode', '-acceptSoftwareTermsForThisRunOnly',
  '-useHub', '-hubIPC', '-cloudEnvironment', 'production',
  '-licensingIpc', 'LicenseClient-<username>',
  '-projectPath', 'D:\UnityProject\Factory-with-DOTS',
  '-logFile', 'D:\UnityProject\Factory-with-DOTS\Logs\perf.log',
  '-executeMethod', 'FactoryPerformanceBatchRunner.Run',
  '-factoryPerformanceScene', 'Perf_4096_Mk4_FullLoop',
  '-factoryPerformanceScale', '128',
  '-factoryPerformanceLoadPercent', '100',
  '-factoryPerformanceCapture',
  '-factoryPerformanceWarmupSeconds', '5',
  '-factoryPerformanceSampleSeconds', '10',
  '-factoryPerformanceOutput', 'D:\UnityProject\Factory-with-DOTS\PerformanceReports\full-loop-128.json'
)
```

成功运行的日志应依次出现：

```text
[ECS Performance] Opening batch scene: ...
[ECS Performance] Entering Play Mode for capture.
[ECS Performance] READY: ...
[ECS Performance] CAPTURE COMPLETE: ...
```

## 4. 自动化压力测试

仓库提供 `Tools/FactoryStress/Run-FactoryStressTest.ps1`。它按倍增序列依次启动
独立 Unity batch 进程，每个规模从初始状态开始采样，读取 JSON 报告后判定是否
达到性能下限。该脚本只在用户明确要求进行压力测试时运行，默认性能流程不会调用
它。

### 4.1 常用参数

| 参数 | 默认 | 说明 |
|---|---|---|
| `-Scene` | `Perf_Straight_Scalable` | 性能场景名 |
| `-StartScale` | `2` | 起始 scale |
| `-MaxScale` | `int` 上限 | 压力测试安全停止点，不再是场景 scale 硬上限 |
| `-Multiplier` | `2` | 每轮倍增系数 |
| `-LoadPercent` | `50` | 初始装载率 |
| `-WarmupSeconds` | `5` | 每轮采样前预热秒数 |
| `-SampleSeconds` | `10` | 每轮采样秒数 |
| `-TimeDelaySeconds` | `0.25` | 传送带场景的放置/拆除间隔秒数 |
| `-DragSeconds` | `0.25` | 传送带场景的拖动秒数 |
| `-DemolitionOrder` | `0` | 拆除顺序，`0` 正序，`1` 倒序 |
| `-TpsThreshold` | `50` | tps 下限，`0` 表示不检查 |
| `-FpsThreshold` | `0` | fps 下限，`0` 表示不检查 |
| `-ThresholdMode` | `Any` | `Any` 任一低于阈值即失败，`All` 需要两者都低 |
| `-OutputDir` | `Docs/PerformanceReports/StressTest` | 输出根目录 |
| `-TimeoutSeconds` | `300` | 单个 Unity 进程超时 |
| `-Graphics` | 关闭 | 不带时使用 `-nographics` |
| `-ReimportSubScene` | 关闭 | 带时使用 `ReimportSubSceneAndRun` |

### 4.2 示例

用 FullLoop 从 2 到 128 倍增，主阈值检查 tps：

```powershell
& .\Tools\FactoryStress\Run-FactoryStressTest.ps1 `
  -UnityExe 'D:\Application\Unity\6000.3.19f1\Editor\Unity.exe' `
  -Scene Perf_4096_Mk4_FullLoop `
  -StartScale 2 `
  -MaxScale 128 `
  -Multiplier 2 `
  -LoadPercent 100 `
  -WarmupSeconds 3 `
  -SampleSeconds 5 `
  -TpsThreshold 50 `
  -FpsThreshold 0
```

脚本输出目录结构：

```text
Docs/PerformanceReports/StressTest/Perf_4096_Mk4_FullLoop/stress-<timestamp>/
  scale-2/report.json           # compact 摘要，无 CSV
  scale-2/unity.log
  scale-4/report.json           # compact 摘要
  ...
  scale-64/report-detailed.json   # 实体规模等于 4096 时保留完整数据
  scale-64/report-detailed.csv
  scale-128/report-detailed.json  # 触发性能上限时保留完整数据
  scale-128/report-detailed.csv
  stress-summary.json
  attempts.csv
```

## 5. 阈值判定与报告

压力测试使用采样窗口内的均值判定：

- `tps = fixedTicksPerSecond`
- `fps = framesPerSecond`
- `ThresholdMode=Any`：任一配置的指标低于阈值即失败；
- `ThresholdMode=All`：所有配置的指标都低于阈值才失败。

首次失败的 scale 记为 `firstFailingScale`，上一轮通过的 scale 记为
`lastPassingScale`。压力脚本只在以下两种情况保留完整 `report-detailed.json`
和逐帧 CSV，中间规模只写 compact `report.json`，避免产生大量无用数据：

- `scale == 4096`，或实际实体规模等于 4096；
- 建造场景的 `peakBeltEntities == 4096`（`Perf_ContinuousBeltBuild` 默认满载
  时等价于 `scale == 64`）；
- 触发 tps/fps 性能下限。

完整报告保留 FixedStep、BeltTransfer、GC、内存、实体数量和传输吞吐。

`stress-summary.json` 同时包含：

- 本次运行配置；
- 每个 attempt 的 scale、tps、fps、帧时间、实体数量；
- `foundLimit`、`lastPassingScale`、`firstFailingScale` 和阈值报告路径；
- Unity 崩溃、超时或报告缺失时状态为 `error`，不会误判为性能极限。

`-nographics` 下 fps 通常是未限帧的合成值，建议以 tps 作为主阈值；需要严格
测量 fps 时，使用 `-Graphics` 并配合 VSync 或固定目标帧率。

## 6. CI 集成建议

1. 在 CI 上锁定同一台硬件或同一规格机器，避免跨机比较绝对毫秒。
2. 压力测试结果默认写入 `Docs/PerformanceReports/StressTest`，按
   `scene/runId` 归档；只有用户明确要求时才执行压力测试。
3. CI 检查 `stress-summary.json` 中的 `result`：
   - 非 `error` 且达到预期的 `firstFailingScale` 视为通过；
   - 回归测试应比较同一场景的 `lastPassingScale` 或同 scale 的
     `fixedTicksPerSecond`、`BeltTransfer perTick`、GC per tick。
4. 修改模拟逻辑后，先跑完整 `Factory.Tests`，再跑固定规模性能测试；只有用户
   明确要求压力测试时才运行压力测试，不要让压力测试替代确定性回归测试。

## 7. 注意事项

- `Perf_F16_Mk4_1024Items` 固定布局，不参与参数化压力序列。
- `-factoryPerformanceLoadPercent` 仅对支持装载率的场景生效；
  HalfLoaded、Blocking 和 ProducerConsumer 会忽略该参数。
- 小 scale 的启动和采样噪声占比更高，正式结论建议从可代表真实负载的 scale
  起跳，或用多次运行取中位数。
- 阻塞场景需要更长预热；压力脚本的 `-WarmupSeconds` 应按场景单独配置。
- 布局生成器不再限制 scale 上限；压力脚本的 `-MaxScale` 只是安全停止点。
- `Perf_4096_Mk4_FullLoop` 要求边长为偶数，这是哈密顿环拓扑约束，不是规模上限。

## 8. 验证清单

1. 运行 `Factory.Tests`，`PerformanceScenarioLayoutTests` 应覆盖每个可参数化
   场景的默认值和 scale 参数。
2. 用最小规模跑一次压力冒烟：`StartScale=2, MaxScale=8, TpsThreshold=999`，
   应立刻停止在 `firstFailingScale=2`。
3. 用宽松阈值再跑一次：`TpsThreshold=1`，应跑完序列并报告
   `limitNotFound` 或达到上限。
4. 对比同 scale 的固定规模报告与压力测试报告，确认 tps/fps、帧时间和
   Profiler 指标口径一致。
