# 统一生命与伤害（A01 基础交付）

实现日期：2026-11-11，批处理修订：2026-11-14。对应 [A01 计划](../Plan/A01-DamageSystem-Plan.md)。伤害在当前固定 Tick 末批量结算，不逐次同步扣血，也不人为拖至下一 Tick。

## 当前交付范围

玩家、单位和建筑可以组合相同的 `Health`、`HealthPolicy`、`VitalIdentity`、`VitalStatus`、`PendingVitalTransition` 和 `CombatFaction`。`Death` 策略归零后进入 Dead，`Disable` 策略进入 Disabled。当前提供通用初始化、伤害、普通治疗、显式复活、碰撞子实体映射、快照和正式事件；没有给现有场景对象自动添加生命。

正式生命和状态只有战斗结算模块写入：`HealthResolveSystem` 管理生命周期、延期操作及批次，`DamageResolveJob` 在其受控边界内并行写入生命。初次生成使用 `InitializeTarget`；SubScene/Prefab 可使用根对象上的 `HealthAuthoring`，Baker 校验正整数最大生命和合法策略，运行时在首次固定 Tick 起始初始化。生命定义目前只有此最小创作入口；A03/B01 接入真实角色/建筑时再扩展既有领域定义和内容管理器，不另建生命内容库。

已经接入生产系统的可选状态门控：具有 VitalStatus 的加工设备在 Dead/Disabled 时不再启动、推进或发布加工结果，保留槽位和在制品。未接生命的既有设备继续工作。通用 `VitalActivity.CanAct` 只读取正式 VitalState，不读取当前血量。完整角色控制/AI/武器限制、掉落、碰撞退休、安全点重生由 A03 接入；射击和 ECS Physics 命中生成由 A04 接入；建筑维修物资事务、恢复资格、供电与重启由 B01 接入。当前不接受维修请求，也不允许治疗或复活解除 Disabled。

## 固定 Tick 阶段

`FixedStepSimulationSystemGroup` 的真实子组 `CombatTickStartGroup` 使用 OrderFirst，位于同父组的 `BeginFixedStepSimulationEntityCommandBufferSystem` 之后，普通生产、传输和物理组之前。没有移动现有领域系统，也没有通过跨父组排序属性推断顺序。

每次起始组更新：推进世界内 ulong Tick；初始化新目标；提交已到期转换；封口渲染意图；稳定排序并结算已到期治疗/复活。`HealthRequestFlushSystem` 位于固定组 OrderLast，明确在 EndFixedStepSimulation ECB 之后，将主线程及 Job inbox 的本 Tick 伤害合为一批。主线程排序和分组，Burst Job 在不同目标之间并行结算，同一目标内按稳定顺序处理；随后主线程按原稳定顺序导出结果并预约归零转换。归零转换最早到下一个 Tick 才正式提交。

| 时点 | 10 血目标收到 15 点伤害 |
|---|---|
| N 正常模拟调用伤害入口 | 只入队，仍为 10 血 Active，不同步等待 Job |
| N 末批次结算 | 变为 0 血 Active，并锁存 PendingVitalTransition；同目标批内后续伤害不能重复归零 |
| N+1 起始 | 正式进入 Dead/Disabled，发布一次生效事件 |

暂停不更新固定组，因此正常世界不会自动结算伤害、延期治疗/复活或归零转换。输入/攻击生产者仍应停止在暂停时发出新伤害，不能手动调用 FlushInbox 绕过模拟阶段。一帧多次固定更新分别结算各 Tick 的批次，表现出口在这些更新之后统一消费；屏幕在下一次可用的表现更新/呈现时显示结果。

## 请求入口

固定模拟主线程在 CombatTickStartGroup 之后、Tick 末结算之前调用 `Submit`。填写 Kind、Target、TargetIdentity、正整数 Amount、SourceStableId 和 RequestId。Damage 使用 GeneratedTick=ExecuteTick=当前 Tick，只做入口校验、去重、容量预约和入队，不访问目标组件、不等待 Job、不立即扣血；Heal/Respawn 仍使用当前 Tick/当前 Tick+1。`SubmitDamage` 接受发出时复制的 `DamageSource`，复制伤害、类型、责任主体、物品和阵营快照；基础伤害为 0 返回 NoEffect，不创建请求。普通零值请求返回 InvalidAmount。这些是主线程入口，并行生产者应使用 ECB inbox。

```csharp
var health = world.GetOrCreateSystemManaged<HealthResolveSystem>();
if (health.TryGetDamageTarget(colliderEntity, out Entity root, out VitalIdentity life))
{
    HealthFailure accepted = health.Submit(new HealthRequest
    {
        Kind = HealthRequestKind.Damage,
        Target = root,
        TargetIdentity = life,
        Amount = 15,
        SourceStableId = producerStableId,
        RequestId = attackSequence,
        InstigatorIdentity = attackerIdentity,
        SourceEntity = projectileEntity,
        SourceFactionSnapshot = sourceFactionAtEmission
    });
    // None 仅表示已接收；执行结果见 RequestResolved。
}
```

渲染帧桥接使用 `SubmitIntent`：所有意图在下一固定 Tick 起始封口，分配当 Tick 时间戳；Damage 在该 Tick 末结算，非伤害意图仍再等一 Tick。它不承诺渲染帧调用返回前血量更新。不要在固定 Tick 外使用 Submit 留下旧时间戳。原始鼠标输入仍应交给武器/命中流程，不能由 HUD 绕过命中判断生成正式伤害。

Job 生产者读取唯一 `CombatClock.Tick`，伤害使用 K/K 时间戳，治疗/复活使用 K/K+1，通过 ECB 将 `HealthRequestInbox` 追加到 `resolver.InboxEntity`。使用 EndFixedStepSimulation ECB 回放，并注册生产者依赖，由其后的 HealthRequestFlushSystem 在同一 Tick 消费；测试/自定义管线可在明确安全点由主线程调用 FlushInbox，但不要逐次命中调用它。不要留到渲染帧末或下一固定 Tick 才回放；主线程及 Job 伤害错过本 Tick 的批次都返回 InvalidRequest，不静默拖到下一 Tick。延期操作仍可在到期 Tick 起始消费。生产者需要限制本 Tick 的写入量；消费者对溢出请求返回 QueueFull。

每个合并批次按 ExecuteTick、SourceStableId、RequestId、目标身份和代次排序；Job inbox 在容量仲裁之前也按此键排序。按实际目标 Entity（含 Version）分组，不按请求声称的身份分组：同一实体即使被不同/错误身份引用，也只有一个写入者。`DamageResolveJob` 每组执行一次，组内顺序处理所有请求，组间独立并行，单个目标的生命最多写回一次。主线程按排序索引合并结果，Job 完成先后不决定事件及转换序号。

Tick 起始已到期治疗/复活先于当 Tick 末伤害；未来治疗不能取消本 Tick 已确定的归零。不同显式批次按其安全消费顺序生效；默认管线每 Tick 一批。每个生产者必须使用唯一、稳定的 SourceStableId，RequestId 使用非零且不复用的 ulong 序号，不以 Entity 遍历/ECB 偶然追加顺序决定玩法。同一范围攻击对同一生命根复用同一请求身份，跨目标合法独立结算。

入口返回值只表达接收情况，None 不等于目标一定成功受伤；Damage 的 RequestResolved 在批次结束时产生，延期操作的结果在执行 Tick 产生。目标存在性、身份、生命代次、合法血量、正式状态和阵营在实际执行时复核。阵营 0 为无队伍限制；非零源阵营与执行时目标阵营相同时，默认拒绝伤害。`AllowFriendlyFire` 为世界内统一规则；后续阵营变化不追溯已提交伤害。SourceEntity 和责任主体消失不取消已保存请求；结算不再读取它们。

批次只同步 Health、PendingVitalTransition、VitalIdentity、HealthPolicy、VitalStatus 和 CombatFaction 的相关依赖，不再逐次调用 CompleteAllTrackedJobs。每批统一等待其伤害 Job 完成后汇总结果。主线程排序/分组、去重、事件预约及汇总仍有串行成本；单目标集中受击仍需保持顺序。DamageBatchCount、LastDamageBatchRequestCount 和 LastDamageBatchTargetCount 可辅助诊断；这些不是性能基准，本次只运行正确性回归，没有量化 FPS 或进行大规模压力测试。

`Respawn` 使用 Amount=0，只允许无待生效转换的 Dead 目标；到期 Tick 恢复满血和 Active，并增加 LifeGeneration，没有额外隐式延迟。旧代次请求失败。此入口只负责新生命，不负责玩家控制关联、位置、库存或碰撞体流程，不能单独用来宣称完整玩家重生已交付。

## 数值、身份与有界生命周期

生命、请求数值、来源、实际变化量和快照全部使用 int，伤害减法/治疗加法使用 long 后钳制。弹药定义、内容管理器 IntegerField 和 AmmunitionDefinitionBlob 同步迁移为 int；零基础伤害合法，负基础伤害非法。当前库不存在需要迁移的小数弹药子资产；没有浮点生命/请求导入接口。

VitalIdentity.Value 是世界内单调分配的独立身份，不混用玩家、建筑或库存 ID；LifeGeneration 为 uint。身份不承诺跨世界/跨存档保持不变。Tick、请求序号和事件/转换序号采用 ulong；内部计数器耗尽时失败而不回绕，复活代次耗尽返回 GenerationExhausted。

主线程请求和意图合计最多 4096；去重最多 8192 条，按源/请求/目标/生命代次保存 120 Tick 后清理，满时返回 QueueFull。调用方不得把短期去重缓存当成永久重放保护，不得复用旧攻击序号。Job 的旧时间戳另行拒绝。每 Tick 已消费请求和已提交转换被移除，世界销毁清空所有队列、事件和上下文，不使用静态世界缓存。

碰撞子实体通过 DamageTargetOwner 查找生命根，最多解析 16 层，循环或无生命返回 false。该查找不负责射线遮挡；A04 必须保留不可损坏物体的物理命中，不能把 false 当作射线可穿透。

## 表现出口

`TryGetSnapshot` 返回已提交 Health、VitalState、身份、Tick 和独立的待转换标志。待转换标志不能用于提前播放死亡或禁止正式行动。

`HealthPresentationExportSystem` 位于 PresentationSystemGroup，单次排空世界内结果队列，通过 EventReceived 广播所有固定 Tick 的事件。正常表现层统一订阅该出口；底层 TryTakeEvent 仅供测试或替代出口，不能多个消费者竞争读取。RequestResolved 是正式成功/失败结果，含请求、实际变化、前后血量及产生/执行 Tick；死亡、瘫痪仅由转换生效时发布 Death/Disabled。TransitionDiscarded 用于目标销毁/换代诊断。Respawned 标记已经提交的新生命。

事件携带单调序号，最多保留 8192 条。每个已接收请求预留结果与后果所需容量；归零后保留跨 Tick 的转换事件预约。表现积压时，新请求在入口返回 QueueFull，已接收请求的正式结果与转换事件不会被挤掉。一帧内多个固定 Tick 的正式事件保留至表现出口统一排空。绕过主线程入口的大量原始 Job 拒绝诊断若无法入队，只增加 DroppedEventCount，绝不覆盖已接收请求的事件；生产者应限制写入量并监测此计数。正式状态和后果不依赖表现消费者。出口不保存全部历史，世界销毁解除订阅。

## 回归记录

专项测试位于 `Assets/Tests/Combat/HealthResolveTests.cs`；实际设备门控回归位于 `ProcessorInventoryTests.DisabledProcessorStopsAtEffectiveTickAndPreservesWorkInProgress`；整数定义、Blob 和内容管理器回归位于 `FactoryItemCapabilityTests`。

最终测试结果、日志与后续接入范围记录于 [A01 计划](../Plan/A01-DamageSystem-Plan.md) 的实施记录。
