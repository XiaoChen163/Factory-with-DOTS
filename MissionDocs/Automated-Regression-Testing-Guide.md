# ECS 自动化回归测试指南

## 1. 文档目的

本文档规定 Factory ECS 项目在修改代码后必须运行的自动化测试，以及新增测试项目时应遵循的目录、夹具、命名和验证流程。

当前测试环境：

- Unity：`6000.3.19f1`
- Entities：`1.4.8`
- Unity Test Framework：`1.6.0`
- 测试程序集：`Factory.Tests`
- 测试模式：EditMode
- 测试目录：`Assets/Scripts/Testing`

测试代码只能依赖正式运行时程序集，不得把测试状态或测试专用组件放入正式运行路径。

## 2. 每次修改后的最低测试要求

### 2.1 通用规则

每次修改 `Assets/Scripts` 下的代码后，至少执行以下检查：

1. 等待 Unity 脚本编译和 Domain Reload 完成。
2. 确认 Console 中没有新的编译错误。
3. 运行完整的 `Factory.Tests` EditMode 测试程序集。
4. 确认结果中 `failed=0`，不能只运行单个测试后就提交。
5. 如果修改引入新的玩法规则或修复了缺陷，必须先增加对应回归测试，再认为修改完成。

开发过程中可以先运行与改动相关的测试类以快速反馈，但提交前必须运行整个 `Factory.Tests`。

### 2.2 按修改类型选择测试

| 修改范围 | 开发中优先运行 | 提交前必须运行 | 需要特别确认的结果 |
|---|---|---|---|
| `BeltTransferResolver` | `BeltTransferResolverTests` | 完整 `Factory.Tests` | 物品不丢失、不复制，仲裁结果确定 |
| `Belt`、`Merger`、`Splitter` 组件 | `BeltTransferResolverTests` | 完整 `Factory.Tests` | Progress、CurrentItem 和 cursor 语义不变 |
| Belt Progress 或 Fixed Tick 顺序 | 直线与阻塞测试 | 完整 `Factory.Tests` | 未 Ready 不移动，统一快照移动仍成立 |
| 合流器仲裁 | `CompetingSources_*` | 完整 `Factory.Tests` | round-robin 和创建顺序无关性 |
| 测试夹具或测试 asmdef | `FixtureSmokeTests` | 完整 `Factory.Tests` | 测试能发现、World 能创建和销毁 |
| Item Port、Buffer Swap、Processor 或 Storage | 对应端口集成测试；若不存在则必须补建 | 完整 `Factory.Tests` | Receipt 只应用一次，容量和预订正确 |
| 拓扑缓存或新 Resolver | 旧、新 Resolver 对照测试 | 完整 `Factory.Tests` | 相同输入产生相同标准化状态 |
| Presentation 或 Transform | 对应 PlayMode 测试 | EditMode 全套及相关 PlayMode 测试 | 多 Fixed Tick 时每渲染帧最多更新一次 |
| 纯 UI、文档或非运行时代码 | 相关专项测试 | 至少确认完整脚本编译；影响程序集时运行完整测试 | 不破坏程序集引用和测试发现 |

表格中尚未建立的端口、拓扑对照和 Presentation 测试属于后续必须补齐的测试范围，不能用现有 Resolver 测试代替。

## 3. 当前自动化测试项目

当前 `Factory.Tests` 一共包含 9 个测试。

### 3.1 夹具烟雾测试

文件：`Assets/Scripts/Testing/FixtureSmokeTests.cs`

- `TransportScenario_AdvancesOneStraightTransfer`
- `FactoryWorldFixture_CreatesIsolatedTransportEntities`

这些测试验证测试基础设施本身。修改 asmdef、World 夹具或 Resolver 场景夹具后，应优先运行它们。

### 3.2 直线运输

文件：`Assets/Scripts/Testing/BeltTransferResolverTests.cs`

- `StraightLine_DoesNotMoveItemBeforeItIsReady`
- `StraightLine_MovesReadyItemIntoEmptyDownstreamNode`
- `StraightLine_MovesReadyChainFromOneSnapshot`

保护的规则：

- `Progress < 1` 的物品不得移动；
- Ready 物品可以进入空下游；
- 连续 Ready 节点使用同一 Tick 快照原子推进。

### 3.3 下游阻塞

- `BlockedTarget_PreservesSourceAndTargetItems`
- `BlockedTarget_DoesNotMoveUpstreamWhenTargetIsNotReady`

保护的规则：

- 下游无法离开时不能覆盖已有物品；
- 下游未 Ready 时，上游不得提前进入；
- 阻塞前后物品集合必须保持一致。

### 3.4 两来源争抢目标

- `CompetingSources_MergerUsesRoundRobinCursor`
- `CompetingSources_WinnerDoesNotDependOnInsertionOrder`

保护的规则：

- Merger 按 `NextInputIndex` 选择来源；
- 成功接收后 cursor 正确推进；
- 赢家不依赖来源 Entity 的创建顺序或数组插入顺序；
- 同一目标在一个 Tick 内最多接受一个来源。

## 4. 如何运行测试

### 4.1 在 Unity Test Runner 中运行

1. 打开 `Window > General > Test Runner`。
2. 选择 `EditMode`。
3. 选择 `Factory.Tests`。
4. 点击 `Run All`。
5. 确认 9 个测试全部通过，Console 中没有异常。

### 4.2 使用项目内的测试请求入口

项目提供了 `FactoryTestRunRequest`。当 Unity 已经打开时：

1. 在项目根目录创建 `Temp/Factory.Tests.run` 文件，内容可以是任意文本。
2. 在 Unity 中执行资源刷新，例如按 `Ctrl+R`。
3. 请求文件会在测试启动时自动删除。
4. 测试结束后读取 `Temp/Factory.Tests.result`。

成功结果示例：

```text
result=Passed
passed=9
failed=0
skipped=0
duration=0.091849
```

`Temp` 中的请求和结果文件不是项目资产，不应提交到版本控制。

### 4.3 命令行运行

执行命令前必须关闭同一项目的 Unity Editor，否则项目锁会阻止批处理实例启动。

```powershell
& $unityExe `
  -batchmode `
  -nographics `
  -projectPath "D:\Qxc\unityProject\Factory-with-DOTS" `
  -runTests `
  -testPlatform EditMode `
  -assemblyNames Factory.Tests `
  -testResults "D:\Qxc\unityProject\Factory-with-DOTS\Temp\Factory.Tests.xml" `
  -logFile "D:\Qxc\unityProject\Factory-with-DOTS\Temp\Factory.Tests.log" `
  -quit
```

CI 不仅要检查 Unity 进程退出状态，还应解析测试 XML，确认失败数量为零。

## 5. 如何添加新测试项目

### 5.1 选择测试层级

先根据被测对象选择夹具：

- 纯运输解析规则：使用 `TransportScenario`，不创建完整游戏场景。
- ECS System、EntityQuery、DynamicBuffer 或 ECB 行为：测试类继承 `FactoryWorldFixture`。
- 依赖 PlayerLoop、渲染帧或 Presentation Group：建立 PlayMode 测试程序集，不要伪装成纯 EditMode 测试。
- 性能和 GC：使用 Performance Test Framework，并与正确性测试分开执行和报告。

### 5.2 创建测试文件

在 `Assets/Scripts/Testing` 下按功能创建 `*Tests.cs` 文件，例如：

```text
Assets/Scripts/Testing/ItemPortIntegrationTests.cs
Assets/Scripts/Testing/MergerRoundRobinTests.cs
Assets/Scripts/Testing/LoopTransferTests.cs
```

文件应使用 `Factory.Tests` 命名空间。Unity 生成 `.meta` 后，测试脚本和 `.meta` 必须一起提交。

只有测试需要新的包程序集时才修改 `Factory.Tests.asmdef`。增加引用后必须重新运行 `FixtureSmokeTests` 和完整测试程序集。

### 5.3 测试命名

测试名使用以下格式：

```text
被测场景_条件或动作_预期结果
```

示例：

```text
Splitter_WhenPreferredOutputBlocked_UsesNextAvailableOutput
FullLoop_WhenEveryItemIsReady_RotatesAtomically
InputPort_WhenCapacityIsZero_DoesNotConsumeItem
```

名称应表达玩法语义，不要使用 `Test1`、`Works` 或 `ResolverTest` 等无法说明失败原因的名称。

### 5.4 使用 TransportScenario 添加 Resolver 测试

推荐结构：

```csharp
[Test]
public void StraightLine_WhenItemIsReady_MovesToEmptyTarget()
{
    using TransportScenario scenario = new TransportScenario();
    Entity item = scenario.CreateItem();
    scenario.AddBelt(new int2(0, 0), new int2(1, 0), item, 1f);
    scenario.AddBelt(new int2(1, 0), new int2(1, 0));
    TransportStateSnapshot before = scenario.CaptureState();

    TransportTickResult result = scenario.ResolveTick();
    TransportStateSnapshot after = scenario.CaptureState();

    Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
    Assert.That(
        TransportAssertions.NodeAt(after, new int2(1, 0)).CurrentItem,
        Is.EqualTo(item));
    TransportAssertions.AssertItemSetPreserved(before, after);
}
```

注意事项：

- 使用 `ResolveTick()` 模拟完整 Fixed Tick。它包含当前 `BeltTransferSystem` 使用的多轮 Resolver 调用。
- 除非专门测试单轮内部行为，否则不要直接调用 `FactoryTransferResolver.Resolve()`。
- 不要依赖数组下标表示格子，使用 `TransportAssertions.NodeAt()` 按 Cell 查询。
- 对所有会移动物品的场景，保存 before/after，并调用 `AssertItemSetPreserved()`。
- 测试确定性时，至少再构造一次不同 Entity 创建顺序或数组插入顺序的等价场景。

### 5.5 使用 FactoryWorldFixture 添加 System 测试

System 测试类继承 `FactoryWorldFixture`：

```csharp
public sealed class ItemPortIntegrationTests : FactoryWorldFixture
{
    [Test]
    public void InputPort_WhenCapacityExists_ConsumesOneItem()
    {
        Entity item = CreateItem();
        Entity belt = CreateBelt(
            new int2(0, 0),
            new int2(1, 0),
            item,
            1f);

        // 创建端口 Owner、填写 Buffer、更新被测 System，然后断言状态。
    }
}
```

注意事项：

- 每个测试使用独立 World，不能复用游戏的 `DefaultWorld`。
- 通过系统公开更新入口测试行为，不要用反射调用私有方法。
- 更新 Job 系统后调用夹具的 `UpdateSystem()`，确保依赖完成后再读取组件。
- 测试结束由夹具自动完成 Job 并销毁 World。
- 端口测试必须同时断言运输节点、Item Entity、Receipt 和 AppliedTransferCount，不能只检查其中一个状态。

## 6. 新测试的完成标准

新增测试只有同时满足以下条件才算完成：

- 测试名称能描述场景、条件和预期；
- 单独运行时通过；
- 与整个 `Factory.Tests` 一起运行时通过；
- 重复运行结果一致；
- 测试不依赖当前打开场景、Default World、渲染帧率或测试执行顺序；
- 测试失败时，断言信息能够指出违反的规则；
- 修复缺陷所添加的测试，在没有修复的代码上应能复现原问题；
- 测试脚本和 `.meta` 已加入版本控制；
- Unity Console 无新增编译错误、异常或未释放 Native 容器警告。

## 7. 测试维护规则

- 玩法规则变化时，先更新测试所表达的规则，再修改实现。
- 性能重构不得为了通过测试而降低现有确定性或物品守恒断言。
- 不得通过忽略测试、删除断言或放宽为“非空即可”来处理失败。
- 如果组件布局变化，优先更新夹具的构造方法，避免每个测试重复适配。
- 每增加一种网络结构，至少包含成功路径、阻塞路径和确定性路径。
- 测试数量变化后，文档中的当前测试清单和示例通过数量应同步更新。
