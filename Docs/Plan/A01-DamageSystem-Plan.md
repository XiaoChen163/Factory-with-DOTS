# A01：统一生命、伤害与归零处置实施计划

本版本按用户确认采用当前 Tick 批量结算，替代先前的调用内同步扣血方案。

修订日期：2026-11-14。状态：基础结算已实施，本次改为当前 Tick 内按目标并行结算；完整角色、射击和维修仍由 A03/A04/B01 接入。

本次修订取代 2026-11-11 版本的全部请求延迟一 Tick，以及中间版本的调用内同步扣血。当前规则是：**提交只入队；固定 Tick K 产生的伤害在 K 末合批结算，目标组间并行、同组内稳定顺序。归零死亡/瘫痪在 K+1 起始提交。普通治疗、复活及未来维修保留各自明确的延迟边界。**

完整调用说明见 [统一生命与伤害](../Guide/Health-and-Damage.md)。下文是当前生效契约；末尾首次交付记录为历史记录。

## 1. 目标与边界

玩家、敌人、机器人和建筑组合相同的生命组件，生命数值和状态只有统一结算方写入。最大生命和归零策略属于创作参数；当前生命、代次和待转换属于 ECS 运行状态。来源定义、请求、结果和表现事件分离，不建立第二套库存或角色控制框架。

本轮只修改伤害的时序，不实现完整玩家死亡/安全点重生、枪械命中或维修物资事务，不增加护盾、护甲、暴击、抗性、持续伤害叠层、多人同步或通用效果图。基础类型保留 Kinetic 枚举，扩展类型不代表自动拥有抗性规则。

## 2. 当前时序

Tick 仍指 FixedStepSimulationSystemGroup 的一次正式模拟步，项目为 60 Tick/s，不能与渲染帧混用。

| 操作 | 生效边界 |
|---|---|
| 固定模拟主线程 Submit / SubmitDamage 的 Damage | 只做入口校验、去重、容量预约及入队，GeneratedTick=ExecuteTick=K；K 末与 Job 请求合批结算 |
| SubmitIntent 的已确认 Damage | 下一固定 Tick K 起始封口，分配 K/K 时间戳，在 K 末结算；它不是直接把原始鼠标输入当成命中 |
| Job / ECB Damage | GeneratedTick=ExecuteTick=K；依赖完成且 ECB 已回放后，在 K 的安全消费点结算，默认由 EndFixedStepSimulation ECB 后的 HealthRequestFlushSystem 消费 |
| Heal / Respawn 主线程请求 | K 接收，K+1 起始校验并结算 |
| 非伤害 SubmitIntent | K 起始封口，分配 GeneratedTick=K，到 K+1 执行 |
| Damage 新产生的归零转换 | K 扣至 0，EffectiveTick=K+1，K+1 起始正式提交 Dead/Disabled |

例：目标在 K 有 10 血，收到 15 点有效伤害请求，提交返回时仍为 10 血。K 末批次结算为 0 血 Active，并锁存待转换。K+1 起始进入 Dead/Disabled，只输出一次正式生效事件。不再人为拖至 K+1 才扣血、K+2 才归零生效。

归零待生效期间仍按 Active 参加模拟，不允许其他系统通过 Health.Current<=0 提前停机/禁用控制。追加普通伤害和治疗不能取消锁存后果；到期转换先于到期治疗/复活请求处理。完整行为门控通过正式 VitalStatus 读取，不依赖表现事件。

Submit 不读取目标 ECS 状态、不完成 Job、不写血量。每批在 EndFixedStepSimulation ECB 后同步生命相关组件依赖，调度 Burst IJobParallelFor 按目标并行，再统一等待本批完成并汇总结果。没有逐次 CompleteAllTrackedJobs。Job 生产者使用 ECB，不直接调用主线程入口。错过 K 内消费的主线程或 Job 伤害均失败，不静默回退为下一 Tick 扣血。

固定组暂停时，正常世界不自动消费伤害批次、延期治疗/复活或归零转换。暂停时的输入/攻击生产者应停止发出新伤害，不得手动调用 FlushInbox 绕过模拟阶段。本轮没有新增暂停控制器。

## 3. 通用组件与生命周期

- Health.Current / Maximum 使用 int，Maximum 在 [1,int.MaxValue]，Current 在 [0,Maximum]。新 Active 生命必须为正。
- HealthPolicy.OnZero 为 Death 或 Disable；VitalStatus 为 Active、Dead 或 Disabled。
- VitalIdentity.Value 为独立的世界内稳定身份，LifeGeneration 防止同 Entity 重生后旧请求命中新生命。不混用 PlayerId、建筑定义 ID、库存 RuntimeId 或 Entity 索引作为通用身份。
- PendingVitalTransition 保存身份、结果、产生/生效 Tick 和转换序号。它属于调度记录，不代表正式死亡/瘫痪。
- DamageTargetOwner 将碰撞子实体映射到生命根；DamageSource 保存整数伤害、类型、责任主体、直接来源关联和发出时阵营快照。

初始化使用 InitializeTarget 或 HealthAuthoring/Baker 的受控生命周期入口。其他模块不得直接写生命或 VitalStatus。Respawn 仅允许 Dead、无待转换的目标，在请求到期 Tick 恢复满血、Active 并增加代次；不能用普通治疗复活，也不能用 Respawn 解除 Disabled。

伤害执行时校验目标存在、身份、代次、合法生命、正式状态和阵营。来源或攻击者先销毁不取消合法伤害，结算不再读取失效来源实体。旧目标销毁、拆除或换代时，尚未消费的 Job 请求及待转换应拒绝/丢弃并留下诊断，不影响新生命。Collider Blob 的退休/释放继续遵循 EcsCharacterMotor / RetiredCharacterColliderCleanupSystem 的既有边界，本次不改动碰撞体生命周期。

## 4. 整数和输入规则

定义、内容管理器、Baking Blob、Health、DamageSource、请求、结果和快照统一使用 int。伤害/回复使用 long 中间值，钳制后写回 int，实际变化由前后生命差计算。不在每次结算时转换浮点数，不允许回绕。

基础伤害允许 0，SubmitDamage 返回 NoEffect 且不生成请求；普通伤害和治疗要求正整数。负数、非法类型/引用/身份和非法最大生命明确失败。不存在浮点生命请求入口；未来导入边界应拒绝小数、超范围和 NaN/Infinity，不能静默截断。

普通请求不能改变最大生命。Tick、请求/事件/转换序号使用 ulong，内部序号耗尽失败而不回绕，复活代次耗尽返回 GenerationExhausted。未来百分比修正优先使用整数千分比和 long 中间值，固定每阶段舍入顺序，溢出时整项失败；未来低于每 Tick 1 点的持续量通过整数余数累计，不用浮点截断丢失总量。本轮不引入倍率或持续效果。

## 5. 请求身份、顺序与归属

HealthRequest 以 Kind 区分 Damage、Heal 和 Respawn，不使用负伤害表达治疗。保存目标和代次、Amount、DamageType、责任主体、直接来源、物品来源、源阵营快照、SourceStableId、RequestId 和产生/执行 Tick。

主线程和 Job 伤害合并后按 ExecuteTick、SourceStableId、RequestId、目标身份和代次排序，Job inbox 容量仲裁前也排序。按实际 Entity（含 Version）分组，避免不同/错误身份对同一实体生成多个写入者；此分组键仅保证写入独占，不替代玩法排序键。DamageResolveJob 每个目标组执行一次，组内依次校验和扣血，只写回一次生命；不同组并行。主线程按原排序索引导出结果、安排待转换和分配序号，Job 完成先后不影响正式事件顺序。默认每固定 Tick 一批，显式自定义安全批次之间按其正式消费顺序生效。

固定 Tick 起始先提交到期转换，再处理到期治疗/复活，之后才是本 Tick 正常模拟及末尾伤害批次。已到期的治疗先于本 Tick 末伤害；未来治疗不会抢在已经确定的致死后果之前。不将同 Tick 总伤害和总治疗简单相抵。

生产者使用唯一、非零且稳定的 SourceStableId，以及非零、不复用的 RequestId。同一次范围攻击对同一生命根复用同一请求身份，跨不同目标合法独立结算。短期去重不等同于永久重放保护。多个碰撞体要映射到同一根；没有 Health 不等于没有物理遮挡，A04 必须使用实际 ECS 物理命中保留不可损坏物体的阻挡。

源阵营使用发出时快照，目标阵营在批次结算时读取。0 表示无队伍限制；默认拒绝相同非零阵营的伤害，AllowFriendlyFire 在世界内统一配置。改变阵营不追溯已经提交的结果，尚未结算的请求读取执行时归属。未来接管/权限规则须沿用统一入口。

Submit 返回接收情况，不把 None 当作目标成功受伤。Damage 的 RequestResolved 在批次完成后导出；延期操作的正式结果在执行 Tick 产生。失败不部分写入生命或状态，重复请求不再次扣血。

## 6. 系统组、队列和表现

CombatTickStartGroup 是 FixedStepSimulationSystemGroup 的 OrderFirst 子组，在同父组 BeginFixedStepSimulation ECB 之后、普通生产/物理之前。推进 Tick、初始化新目标、提交到期转换、接收延期输入、结算到期治疗/复活。

HealthRequestFlushSystem 是同一固定组的 OrderLast 系统，明确位于 EndFixedStepSimulation ECB 之后，合并当前 Tick 的主线程队列和 inbox。每批只同步 Health、PendingVitalTransition、VitalIdentity、HealthPolicy、VitalStatus 和 CombatFaction 相关依赖，并统一等待这一批伤害 Job 完成，不全局同步无关工厂 Job。生产者应使用 EndFixedStepSimulation ECB 并注册依赖；显式 FlushInbox 只用于自定义受控批次，不得逐次命中调用。不同父组之间不靠简单 UpdateAfter 假定顺序，也不随意移动现有生产/运输/物理系统。

请求/意图最多 4096；短期去重最多 8192 条，保存 120 Tick 后清理。事件最多 8192 条，为已接收请求预留结果及可能的归零/复活事件容量，事件积压时拒收新请求，不能挤掉已接收请求的正式事件。绕过主线程入口的大量原始 Job 拒绝诊断若无空位，只增加 DroppedEventCount，不覆盖正式记录；生产者须限制写入量。世界销毁清空请求、转换、事件和序号上下文，不保留静态跨世界缓存。

HealthPresentationExportSystem 在 PresentationSystemGroup 一次性导出所有已提交结果和转换事件，按事件序号消费，不能多渲染帧重复播放。HUD/音效/动画在下一次可用的表现更新/呈现时看到正式结果，不反向决定扣血、停机、掉落或奖励。

## 7. 后续阶段边界

- A03：将现有玩家/机器人接入统一组件和正式行为门控，死亡只处理一次，接安全点重生、库存/控制关联和碰撞退休。
- A04：射击、弹药消耗、ECS 物理命中、遮挡、多碰撞体/范围攻击去重和基础反馈，生成已确认伤害请求。
- B01：Disabled 保留建筑占格、库存和在制品；维修扣料与加血在到期 Tick 同一库存事务中提交，失败都不提交。维修产生的恢复资格下一 Tick 再提交，不清除供电、缺料和权限等独立限制。本轮不实现维修操作。

现有 ItemProcessSystem 只对具有 VitalStatus 的设备门控，正式 Disabled/Dead 后停止加工推进，归零待生效期间继续加工。未接生命的既有设备不自动变成可损坏对象。

## 8. 当前回归要求

- Submit / SubmitDamage 返回前生命不变，批次前无正式伤害结果；主线程和 Job 统一排序，只在批次结算时改血，不重放。
- SubmitIntent 在固定入口封口后，当 Tick 末结算伤害，不额外等待另一 Tick。
- 批次致死后为 0 血 Active，下一 Tick 才 Dead/Disabled，事件和正式限制各一次。
- 当前 Tick ECB 伤害在该 Tick 末消费，不拖至下一 Tick；过期/未来伤害时间戳拒绝。
- 目标组间并行、同目标按稳定键串行；错误生命身份不能制造同实体的多个写入组；主线程与 Job 请求合批。
- 重复攻击按目标去重、互相致死不取消对方已接受的攻击、串行汇总保证事件/转换顺序不受 Job 完成顺序影响。
- 普通治疗/复活/非伤害意图仍遵循原边界；旧代次、目标销毁、源销毁、友伤和整数上限继续通过。
- 正式快照/事件完整导出、容量背压和世界重开清理继续通过；真实生产门控保持正确。
- 先运行专项，再运行完整 Factory.Tests 和 Factory.PlayModeTests；记录 Editor 退出码/日志与剩余问题，不进行未经授权的大规模压力测试。

## 9. 2026-11-14 当前 Tick 并行批处理修订记录

提交入口改为纯入队；HealthRequestFlushSystem 统一消费，新增 Burst DamageResolveJob，实际 Entity 独占分组，不同目标并行。主线程保留排序、去重、容量预约和正式输出顺序。增加 DamageBatchCount、LastDamageBatchRequestCount 和 LastDamageBatchTargetCount 诊断。本次仅验证正确性，没有宣称量化帧率收益或执行大规模压力测试。

Unity 6000.3.19f1 验证：

| 检查 | 结果 | 记录 |
|---|---|---|
| 初轮批处理与生产门控专项 | 31/31，Editor 正常退出（0） | [XML](../../TestResults/a01-batch-focused.xml)，`Logs/a01-batch-focused.log` |
| 完整 Factory.Tests | 386/386，Editor 正常退出（0） | [XML](../../TestResults/a01-batch-editmode.xml)，`Logs/a01-batch-editmode.log` |
| 完整 Factory.PlayModeTests | 37/37，Editor 正常退出（0） | [XML](../../TestResults/a01-batch-playmode.xml)，`Logs/a01-batch-playmode.log` |
| 格式检查 | git diff --check 通过 | 无空白错误 |

初轮专项通过后补入互相致死用例，包含于完整回归。新增/修订用例覆盖合并主线程和真实 Job/ECB 请求、跨目标独立结算、同一 Entity 被不同身份引用时的单写入者保证、批内顺序、互相致死、错过批次的时间戳拒绝、归零下一 Tick 提交、治疗/复活、整数边界、来源/目标销毁、容量预约和表现输出。未执行性能或压力测试，当前结果不量化吞吐和帧率收益；串行排序/分组/去重/输出及单一热点目标仍可能成为后续测量中的瓶颈。

## 10. 2026-11-14 调用内即时扣血历史记录（已被批处理替代）

修改 HealthResolveSystem 的主线程伤害路径及 SubmitIntent 分派，增加当前 Tick 的 HealthRequestFlushSystem。治疗和复活队列保留。同步修改伤害时间戳、混合请求顺序、生产门控测试和接入指南。

Unity 6000.3.19f1 验证：

| 检查 | 结果 | 记录 |
|---|---|---|
| 即时扣血及生产门控专项 | 29/29 | [XML](../../TestResults/a01-immediate-focused.xml)，`Logs/a01-immediate-focused.log` |
| 完整 Factory.Tests | 383/383，Editor 正常退出（0） | [XML](../../TestResults/a01-immediate-editmode.xml)，`Logs/a01-immediate-editmode.log` |
| 完整 Factory.PlayModeTests | 37/37，Editor 正常退出（0） | [XML](../../TestResults/a01-immediate-playmode.xml)，`Logs/a01-immediate-playmode.log` |
| 格式检查 | git diff --check 通过 | 无空白错误 |

新增回归包括：无固定更新直接扣血、非伤害意图仍延期、Job 当前 Tick 时间戳及批次排序、EndFixedStepSimulation ECB 实际组序、归零下一 Tick 生效、即时伤害与排队治疗的先后关系。没有增加压力测试。

## 11. 首次交付历史记录（2026-11-11，旧伤害时序已被以上修订替代）
代码与调用契约详见 [统一生命与伤害](../Guide/Health-and-Damage.md)。

- `HealthComponents`：独立目标身份/生命代次、整数生命、策略、正式状态、来源、统一操作请求、待转换、结果和快照。
- `CombatTickStartGroup / HealthResolveSystem`：真实固定起始组；先提交上 Tick 转换，再处理到期请求；统一 Damage/Heal/Respawn 排序与写入，归零再延迟一 Tick。完成依赖后读取 Job inbox，生产者使用 EndFixedStepSimulation ECB，不等待渲染帧屏障。渲染意图在 Tick 入口封口后再延迟一 Tick。
- `HealthAuthoring`：最小创作与 Baking 校验，新目标通过生命周期入口初始化；当前不为场景玩家、机器人、建筑自动添加生命，不另建内容库。
- `ItemProcessSystem`：可选 VitalStatus 门控，归零待生效 Tick 继续加工，Disabled 生效 Tick 停止推进并保留槽位/在制品。行动测试探针也验证起始组在普通固定模拟前提交。真实玩家移动/开火和机器人决策待 A03/A04。
- `HealthPresentationExportSystem`：每渲染帧一次性广播所有已提交固定 Tick 事件，提供正式快照。内部 Pending 不作为死亡事件；正式后果不依赖表现消费者。
- 弹药定义、内容管理器 IntegerField、Blob 全部改用 int；现有内容库没有需迁移的小数弹药数据，没有引入浮点请求入口。
- 有界请求/意图、短期去重和事件队列；完整的容量、序号、阵营、生命周期及入口限制见接入指南。未引入整数倍率、持续伤害、抗性、压力场景或第二套库存。

未勾选的跨阶段验收项仍然有效，不视为本次已经通过：真实对象死亡处置/控制关联/掉落/清理及碰撞退休属于 A03；实际射线遮挡和命中属于 A04；维修扣料事务、瘫痪恢复和供电组合属于 B01。当前显式复活入口只建立新生命，不等同于完整玩家重生。没有更改 EcsCharacterMotor 或 Collider Blob 清理边界。

Unity 6000.3.19f1 验证记录：先完成初轮生命专项 23/23，再运行完整回归；最终生命专项已扩展为 25 项，包含于下表 EditMode 结果中，另有生产门控和整数内容定义回归。

| 检查 | 结果 | 结果与本地日志 |
|---|---|---|
| 完整 Factory.Tests | 380/380，Editor 正常退出（0） | [XML](../../TestResults/a01-final-editmode.xml)，`Logs/a01-final-editmode.log` |
| 完整 Factory.PlayModeTests | 37/37，Editor 正常退出（0） | [XML](../../TestResults/a01-playmode.xml)，`Logs/a01-playmode.log` |
| 格式检查 | git diff --check 通过 | 无空白错误 |

初次内容窗口测试使用 -nographics 时因无图形设备失败，移除该参数后完整窗口/内容回归通过。事件队列改为预留正式结果/转换容量；表现积压时拒收新请求，不丢弃已接收请求的正式事件。一次完整 EditMode 在 380 项通过后，Editor 退出阶段出现 WorkerManagerASIO 原生崩溃，日志保留于 `Logs/a01-editmode-shutdown-crash.log`；同参数重跑 380/380 并正常退出，未复现。没有为此修改包、Editor 或项目图形配置。
