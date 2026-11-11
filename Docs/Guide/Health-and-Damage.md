# 统一生命与伤害（A01 基础交付）

实现日期：2026-11-11。对应 [A01 计划](../Plan/A01-DamageSystem-Plan.md)。

## 当前交付范围

玩家、单位和建筑可以组合相同的 `Health`、`HealthPolicy`、`VitalIdentity`、`VitalStatus`、`PendingVitalTransition` 和 `CombatFaction`。`Death` 策略归零后进入 Dead，`Disable` 策略进入 Disabled。当前提供通用初始化、伤害、普通治疗、显式复活、碰撞子实体映射、快照和正式事件；没有给现有场景对象自动添加生命。

正式生命和状态只有 `HealthResolveSystem` 写入。初次生成使用 `InitializeTarget`；SubScene/Prefab 可使用根对象上的 `HealthAuthoring`，Baker 校验正整数最大生命和合法策略，运行时在首次固定 Tick 起始初始化。生命定义目前只有此最小创作入口；A03/B01 接入真实角色/建筑时再扩展既有领域定义和内容管理器，不另建生命内容库。

已经接入生产系统的可选状态门控：具有 VitalStatus 的加工设备在 Dead/Disabled 时不再启动、推进或发布加工结果，保留槽位和在制品。未接生命的既有设备继续工作。通用 `VitalActivity.CanAct` 只读取正式 VitalState，不读取当前血量。完整角色控制/AI/武器限制、掉落、碰撞退休、安全点重生由 A03 接入；射击和 ECS Physics 命中生成由 A04 接入；建筑维修物资事务、恢复资格、供电与重启由 B01 接入。当前不接受维修请求，也不允许治疗或复活解除 Disabled。

## 固定 Tick 阶段

`FixedStepSimulationSystemGroup` 的真实子组 `CombatTickStartGroup` 使用 OrderFirst，位于同父组的 `BeginFixedStepSimulationEntityCommandBufferSystem` 之后，普通生产、传输和物理组之前。没有移动现有领域系统，也没有通过跨父组排序属性推断顺序。

每次组更新：完成 ECS 依赖；推进世界内 ulong Tick；初始化新目标；提交已到期转换；接收已封口的 Job 请求及渲染意图；最后稳定排序并结算已到期生命请求。新归零转换在结算后追加，最早到下一个 Tick 才提交，正式状态直接写入 ECS，不等待帧末 ECB。

| 时点 | 10 血目标收到 15 点伤害 |
|---|---|
| N 正常模拟 | 只接收请求，仍为 10 血 Active |
| N+1 起始 | 结算为 0 血 Active，并锁存 PendingVitalTransition |
| N+1 正常模拟 | 仍允许行动/加工，追加普通伤害和治疗均失败 |
| N+2 起始 | 正式进入 Dead/Disabled，发布一次生效事件 |

暂停不更新固定组，因此不会推进请求或转换。一帧多次固定更新分别推进 Tick，表现导出在这些更新之后统一消费。

## 请求入口

固定模拟主线程从世界获取 `HealthResolveSystem`，调用 `Submit`。填写 Kind、Target、TargetIdentity、正整数 Amount、SourceStableId 和 RequestId；系统分配 GeneratedTick=当前 Tick、ExecuteTick=当前 Tick+1。`SubmitDamage` 另接受发出时复制的 `DamageSource`，复制伤害、类型、责任主体、物品和阵营快照；基础伤害为 0 返回 NoEffect，不创建请求。普通零值请求返回 InvalidAmount。

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

渲染帧输入只调用 `SubmitIntent`，不调用固定模拟的 Submit。意图在下一个固定 Tick 起始封口，该 Tick 作为 GeneratedTick，再到其下一 Tick 执行；MonoBehaviour 的调用早晚不会使它在封口 Tick 扣血。

Job 生产者读取唯一 `CombatClock.Tick`，生成 K/K+1 时间戳的 `HealthRequestInbox`，通过 ECB 追加到 `resolver.InboxEntity`。ECB 必须在下次 CombatTickStartGroup 前回放，推荐 EndFixedStepSimulation ECB；不要追加到渲染帧末 ECB。消费者只接受恰好上一 Tick 产生、本 Tick 到期的记录，过早/过迟均返回 InvalidRequest。生产者需要限制本 Tick 的写入量；消费者对溢出请求返回 QueueFull。

统一顺序为 ExecuteTick、SourceStableId、RequestId、目标身份、生命代次。Job inbox 在容量仲裁之前也按此键排序。每个生产者必须使用唯一、稳定的 SourceStableId，RequestId 使用非零且不复用的 ulong 序号；不要使用 Entity 遍历/ECB 追加顺序分配攻击优先级。同一攻击命中同一生命根目标复用同一 RequestId，跨目标合法独立结算。

所有入口只接收请求。目标存在性、身份、生命代次、合法血量、正式状态和阵营在执行时复核。阵营 0 为无队伍限制；非零源阵营与执行时目标阵营相同时，默认拒绝伤害。`AllowFriendlyFire` 为世界内统一规则。SourceEntity 和责任主体消失不取消已保存请求；结算不再读取它们。

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
