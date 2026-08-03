# UnityDOTS技术栈实现工厂游戏渐进式方案

我想使用unity DOTS技术栈实现一个简单的3d工厂游戏，下面是一个具体的渐进式实现方案
请根据用户的提示，一步一步完成它，每一步完成后，都需要用户测试确认，在确认之前绝对不要快进到下一步

文档中的代码都是**示例代码**,实现时代码需要根据具体情况来确定，绝对不要直接照抄
另外，不要将所有脚本都堆在Scripts文件夹下，基于职责和行为，为每类脚本添加子目录

关于游戏中玩家的交互及游戏的表现形式，如果用户没有明确指出，必须向用户提问，而不是自己擅自决定一个形式

以下是一份完整的**渐进式五阶段实现方案**，每个阶段都包含具体的实现步骤、核心代码示例和关键注意事项。

### 全阶段不变量：一格一物品

- 一格传送带在任意时刻最多持有一个物品。
- 传送带只保存 `currentItem + progress`，不使用物品 `List`、槽位数组或 `DynamicBuffer<BeltSlot>`。
- 物品可以连续移动表现，但逻辑容量始终是每格 1 个。
- 固定 Tick、状态快照、两阶段提交、环路原子移动和合流仲裁仍然保留，用于保证确定性。

## 阶段 0：纯 GameObject 原型（验证核心玩法）

**目标**：跑通“采集 → 运输 → 生产”的最小闭环，暂不关心性能

### 实现步骤

**Step 0.1：定义基础数据**

用 `ScriptableObject` 定义物品和配方：

```csharp
// ItemData.cs
[CreateAssetMenu]
public class ItemData : ScriptableObject {
    public string itemName;
    public Sprite icon;
}

// RecipeData.cs
[CreateAssetMenu]
public class RecipeData : ScriptableObject {
    public ItemData inputItem;
    public int inputCount = 1;
    public ItemData outputItem;
    public int outputCount = 1;
    public float craftTime = 2f;  // 秒
}
```

**Step 0.2：搭建网格建造系统**

参考_Dependence中的GridBuild, 实现3d中2d的网格建造系统（只有x和y轴没有z轴）
这个系统需要玩家在一个固定大小的（n * n）平面上放置建筑/传送带，传送带占一个格子，建筑可能占多个格子
格子的一个单位对应unity中的一个单位距离
建筑需要有和传送带交互的端口（输入输出口）
为了方便调试，每个格子上需要有坐标以及玩家点击后在控制台上输出玩家点了哪个格子

**Step 0.3：实现传送带基础逻辑（最简版）**

在 `MonoBehaviour.Update()` 中每帧移动物品：

```csharp
public class Belt : MonoBehaviour {
    public Item currentItem;  // null 表示当前格为空
    public float speed = 2f;  // 格/秒
    public Vector2Int direction;  // 当前方向
    public Vector2Int nextCell;   // 下一格坐标
    
    void Update() {
        if (currentItem == null) return;

        currentItem.progress = Mathf.Min(
            currentItem.progress + speed * Time.deltaTime,
            1f
        );

        if (currentItem.progress >= 1f) {
            if (TryMoveToNext(currentItem)) {
                currentItem = null;
            } else {
                currentItem.progress = 1f;  // 下游被占用，阻塞在末端
            }
        }
    }

    public bool TryAccept(Item item) {
        if (currentItem != null) return false;
        currentItem = item;
        currentItem.progress = 0f;
        return true;
    }
}
```

**Step 0.4：实现机器基础逻辑**

```csharp
public class Furnace : MonoBehaviour {
    public RecipeData recipe;
    public Belt inputBelt;
    public Belt outputBelt;
    private float craftProgress;
    private Item inputBuffer;
    private Item pendingOutput;
    
    void Update() {
        // 输出阻塞时保留成品，不丢失也不覆盖下游
        if (pendingOutput != null) {
            if (outputBelt.TryAccept(pendingOutput)) {
                pendingOutput = null;
            }
            return;
        }

        // 从输入传送带取出它持有的唯一物品
        if (inputBuffer == null) {
            inputBelt.TryTake(recipe.inputItem, out inputBuffer);
        }

        // 加工
        if (inputBuffer != null) {
            craftProgress += Time.deltaTime;
            if (craftProgress >= recipe.craftTime) {
                pendingOutput = new Item(recipe.outputItem);
                inputBuffer = null;
                craftProgress = 0;
            }
        }
    }
}
```

### ⚠️ 注意事项

1. **不要优化**：这个阶段的目标是“能跑”，所有代码都可以是低效的。
2. **保持简单**：传送带直接保存单个 `currentItem`，机器用 `Update()` 轮询。
3. **手动测试**：在场景中手动放置几个传送带和机器，验证物品能否流动。
4. **里程碑**：能看到采矿机 → 传送带 → 熔炉 → 传送带 → 存储箱的完整链条。
---

## 阶段 1：逻辑与表现分离 + 核心时序

**目标**：建立稳定的固定 Tick、状态快照和两阶段提交，消除 FPS 与 `Update()` 顺序对逻辑的影响。

### 实现步骤

**Step 1.1：拆分逻辑与表现**

```csharp
// BeltLogic.cs - 纯逻辑状态，不负责渲染
public class BeltLogic : MonoBehaviour {
    public ItemState currentItem;  // null 表示空
    [Range(0f, 1f)]
    public float progress;
    public float speed = 2f;
    public Vector2Int direction;
    public Vector2Int nextCell;

    // 仅供逻辑 Tick 的两阶段提交使用，不参与序列化
    [System.NonSerialized] public ItemState nextItem;
    [System.NonSerialized] public float nextProgress;
}

// BeltVisual.cs - 只负责渲染
public class BeltVisual : MonoBehaviour {
    public BeltLogic logic;
    private GameObject itemVisual;
    
    void LateUpdate() {
        if (logic.currentItem == null) {
            SetItemVisible(false);
            return;
        }

        SetItemVisible(true);
        itemVisual.transform.position = GetPosition(logic.progress);
    }
}
```

**Step 1.2：实现固定时间步长（Tick）**

创建游戏管理器驱动逻辑帧：

```csharp
public class GameManager : MonoBehaviour {
    public static GameManager Instance;
    public float tickInterval = 1f / 60f;  // 60 Ticks/秒
    private float accumulator;
    
    public event System.Action OnLogicTick;
    
    void Awake() { Instance = this; }
    
    void Update() {
        accumulator += Time.deltaTime;
        while (accumulator >= tickInterval) {
            accumulator -= tickInterval;
            OnLogicTick?.Invoke();  // 触发逻辑帧
        }
    }
}
```

**Step 1.3：实现快照 + 转移请求 + 统一提交**

```csharp
public class BeltSimulation : MonoBehaviour {
    private readonly List<BeltLogic> belts = new();
    private readonly List<TransferRequest> requests = new();

    void ProcessTick() {
        // 1. 复制 current 到 next；本 Tick 内 current 保持只读
        foreach (BeltLogic belt in belts) {
            belt.PrepareNextState();
        }

        // 2. 推进单个物品；到达末端时只生成请求
        requests.Clear();
        foreach (BeltLogic belt in belts) {
            if (belt.currentItem == null) continue;

            belt.nextProgress = Mathf.Min(
                belt.progress + belt.speed * GameManager.Instance.tickInterval,
                1f
            );
            if (belt.nextProgress >= 1f) {
                requests.Add(new TransferRequest(belt, belt.nextCell));
            }
        }

        // 3. 基于同一份快照统一解决空位、连续移动、环路和合流冲突
        TransferArbiter.Resolve(requests);

        // 4. 所有传送带同时提交 next 状态
        foreach (BeltLogic belt in belts) {
            belt.CommitNextState();
        }
    }
}
```

### ⚠️ 注意事项

1. **一格一物品**：`currentItem == null` 是唯一的空位判断，不再维护槽位集合。
2. **不要边遍历边写下游**：否则结果会依赖 MonoBehaviour 的执行顺序。
3. **统一提交**：本 Tick 的判断只读取 current 快照，最终一次性写回 next 状态。
4. **渲染在 LateUpdate**：根据最新逻辑状态插值显示，不参与运输判定。

---

## 阶段 2：攻克环形传送带与阻塞

在开始阶段2之前，我需要将以下逻辑修改：（可能和后边的步骤有出入，以此为准）
单个传送带现在只能有一个输入和一个输出，也就是说，一个传送带不能接受两个输入
传送带的合并和分流现在交给专门的合并器和分流器实现（1格大小）合并器和分流器都是3合1或是1分3
合并器和分流器采取轮询调度的策略

对现有的传送带的视图做出修改：传送带不再是一个略小的长方体，而是占据一整个格子的底面为正方形的长方体
传送带初始时用黄色围绕4个临边，表示没有连接（比传送带中心略微突起），当有传送带连接时，将黄色边隐藏，表示此面已经连接
传送带的中心有一个三角形指向传送带的方向，当传送带在拐角时，三角形斜45度指向出口

### 实现步骤

**Step 2.1：环形检测（沿 Next 指针查找）**

在建造传送带时检测是否形成闭环：

```csharp
public class BeltNetworkDetector : MonoBehaviour {
    public bool TryFindLoop(BeltLogic start, out List<BeltLogic> loop) {
        var visitedAt = new Dictionary<BeltLogic, int>();
        var path = new List<BeltLogic>();
        BeltLogic current = start;

        while (current != null) {
            if (visitedAt.TryGetValue(current, out int loopStart)) {
                loop = path.GetRange(loopStart, path.Count - loopStart);
                return true;
            }

            visitedAt[current] = path.Count;
            path.Add(current);
            current = GetNextBelt(current.nextCell);
        }

        loop = null;
        return false;
    }
}
```

**Step 2.2：环路原子移动**

```csharp
public static class LoopTransferResolver {
    public static void Resolve(IReadOnlyList<BeltLogic> loop) {
        // 所有判断都读取 current 快照，不能逐条传送带立即写回
        bool everyItemIsReady = true;
        foreach (BeltLogic belt in loop) {
            if (belt.currentItem == null || belt.progress < 1f) {
                everyItemIsReady = false;
                break;
            }
        }

        if (everyItemIsReady) {
            // 满环也可以整体旋转：每格在同一次提交中同时腾空并接收上游物品
            foreach (BeltLogic belt in loop) {
                belt.AcceptFromPreviousInNextState();
            }
        }
    }
}
```

满环不再要求永久预留空格。关键是仲裁器必须把完整环路视为一个原子移动组；如果逐格检查当前占用状态，满环仍会被错误判定为死锁。

**Step 2.3：合流请求轮流仲裁**

```csharp
public class TransferArbiter {
    private readonly Dictionary<Vector2Int, int> nextWinner = new();

    void ResolveTarget(Vector2Int target, List<TransferRequest> requests) {
        if (!TargetWillBeEmpty(target)) return;

        int winnerIndex = nextWinner.GetValueOrDefault(target) % requests.Count;
        TransferRequest winner = requests[winnerIndex];
        AcceptInNextState(winner);

        // 只有成功接收后才轮换，保证两个输入长期公平
        nextWinner[target] = winnerIndex + 1;

        foreach (TransferRequest loser in requests) {
            if (loser != winner) {
                KeepBlockedAtEndInNextState(loser.source);
            }
        }
    }
}
```

### ⚠️ 注意事项

1. **环检测时机**：每次建造/拆除传送带时都需要重新检测受影响的网络。
2. **满环语义**：满环允许整体原子旋转，但满环上的外部入口必须拒绝新物品。
3. **合流冲突**：同一 Tick 只能有一个请求赢得目标格；失败请求保持在源格末端。
4. **性能考虑**：环形检测可以在建造时做，不需要每帧运行。

---

## 阶段 3：向 DOTS/ECS 迁移

**目标**：将运行时逻辑转为 `Entity` + `Job`，实现大规模性能提升。

> 当前 Stage 3 实现的性能审计、目标架构、分阶段优化任务和验收指标，见 [ECS 性能优化策略与实施计划](ECS-Performance-Optimization-Plan.md)。优化时必须保留本节定义的固定 Tick、统一快照、两阶段提交、环路原子移动和确定性仲裁语义。

### 实现步骤


**Step 3.1：定义 ECS 组件（数据层）**

```csharp
using Unity.Entities;
using Unity.Mathematics;

// 传送带组件
public struct Belt : IComponentData {
    public float Speed;
    public int2 Direction;
    public int2 NextCell;
    public Entity CurrentItem;  // Entity.Null 表示当前格为空
    public float Progress;      // 当前物品在本格内的 0-1 进度
    public bool IsLoop;
}

// 物品数据
public struct Item : IComponentData {
    public Entity ItemType;    // 指向 BlobAsset 或原型实体
    public float3 Position;
}
```

**Step 3.2：实现 Baking（GameObject → Entity 转换）**

在 SubScene 中使用 GameObject 创作，通过 Baker 转换为 Entity：

```csharp
using Unity.Entities;
using UnityEngine;

// Authoring MonoBehaviour - 在编辑器中挂载到 GameObject 上
public class BeltAuthoring : MonoBehaviour {
    public float speed = 2f;
    public Vector2Int direction = Vector2Int.right;
}

// Baker - 将 GameObject 数据转换为 Entity 组件
public class BeltBaker : Baker<BeltAuthoring> {
    public override void Bake(BeltAuthoring authoring) {
        Entity entity = GetEntity(TransformUsageFlags.Dynamic);
        
        AddComponent(entity, new Belt {
            Speed = authoring.speed,
            Direction = new int2(authoring.direction.x, authoring.direction.y),
            CurrentItem = Entity.Null,
            Progress = 0f,
            IsLoop = false
        });
    }
}
```

**Step 3.3：实现并行 Job（IJobEntity）**

使用 `IJobEntity` 处理所有传送带实体：

```csharp
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
public partial struct BeltProgressJob : IJobEntity {
    public float DeltaTime;
    
    // 这里只推进本实体自己的单个物品，不跨实体写入
    void Execute(ref Belt belt) {
        if (belt.CurrentItem == Entity.Null) {
            belt.Progress = 0f;
            return;
        }

        belt.Progress = math.min(
            belt.Progress + belt.Speed * DeltaTime,
            1f
        );
    }
}
```

**Step 3.4：创建 System 调度 Job**

```csharp
using Unity.Entities;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]  // 固定时间步长
public partial class BeltSystem : SystemBase {
    protected override void OnUpdate() {
        float dt = SystemAPI.Time.DeltaTime;
        
        // 调度并行 Job
        var job = new BeltProgressJob {
            DeltaTime = dt
        };
        job.ScheduleParallel();  // 自动并行处理所有实体
    }
}
```

**Step 3.5：处理跨传送带物品转移（边界节点）**

对于跨带转移（物品从一条传送带末端移到另一条起点），使用**两阶段提交**：

```csharp
// 阶段1：BeltProgressJob 只推进本格 Progress
// 阶段2：TransferSystem 读取统一快照，收集并仲裁跨格请求
// 阶段3：一次性提交源 Belt.CurrentItem = Entity.Null、
//        目标 Belt.CurrentItem = item、目标 Progress = 0

[UpdateAfter(typeof(BeltSystem))]
public partial class TransferSystem : SystemBase {
    protected override void OnUpdate() {
        Dependency.Complete();

        var snapshot = CaptureBeltSnapshot();
        var requests = CollectReadyTransfers(snapshot);

        // 同一目标的多个请求按轮转优先级选一个；
        // 完整环路作为一个原子移动组处理
        var accepted = ResolveConflictsAndLoops(snapshot, requests);

        ApplyAcceptedTransfers(accepted);
        KeepRejectedSourcesBlockedAtProgressOne(requests, accepted);
    }
}
```

### ⚠️ 注意事项

1. **不需要槽位 Buffer**：一条传送带的完整运行时状态都在单个 `Belt` 组件中。
2. **跨实体写入集中处理**：并行 Job 只更新自己的 `Progress`；跨传送带移动由后置系统统一仲裁。
3. **ECB 使用边界**：只改已有 `Belt` 组件内容不属于结构变化；创建/销毁物品实体或增删组件时才使用 ECB。
4. **FixedStepSimulationSystemGroup**：将系统放入此组可保证 60Hz 固定更新。
5. **SubScene 使用**：将游戏关卡放在 SubScene 中，Unity 会在构建时自动 Baking。

---

## 阶段 4：视觉与交互完善

**目标**：解决纯 ECS 逻辑下的渲染和建造交互问题。

### 阶段 4 架构规则：ECS 运行时 + GameObject 创作与交互

阶段 4 及后续开发统一采用**混合架构**，但必须保持单一运行时状态源：

> 只要建筑会生产、加工、存储、分配或转移物品，它的运行时状态就必须使用 ECS。

#### 对象选型

| 对象 | 实现方式 | 规则 |
|:---|:---|:---|
| 传送带、传送带上的物品 | ECS | 继续使用 `Belt`、`Item` 组件和固定步长系统 |
| 矿机、熔炉、仓库 | ECS | 生产、配方、缓存和库存均由组件保存，由 System 批量更新 |
| 合并器、分流器、机械臂 | ECS | 调度状态、轮询游标和物品转移必须进入统一 ECS 仲裁流程 |
| 发电机、电线杆、物流网络节点 | ECS | 只要参与周期模拟或网络计算，就使用组件和 System |
| 建筑预制体、关卡摆放 | GameObject Authoring + Baker | GameObject 仅用于编辑器创作，进入运行时前 Baking 为 Entity |
| 建筑渲染 | 优先 Entities Graphics | 数量少且表现复杂的附属特效可以使用 GameObject，但不得保存逻辑状态 |
| 建造输入、玩家控制、摄像机 | GameObject / MonoBehaviour | 只采集输入，通过 ECB 请求创建、删除或修改 Entity |
| UI、菜单、教程、音效管理 | GameObject 或 UI Toolkit | 只能读取 ECS 状态或发送命令，不直接拥有工厂模拟状态 |
| 地面、天空、纯装饰物 | GameObject | 不参与模拟的静态环境不需要迁移到 ECS |

#### 强制边界

1. **ECS 是唯一真相来源**：同一座建筑禁止同时维护可写的 MonoBehaviour 状态和 `IComponentData` 状态。
2. **Authoring 不参与运行时模拟**：`MinerAuthoring`、`FurnaceAuthoring` 等组件只保存烘焙参数，逻辑写入对应 Entity 组件。
3. **输入通过命令进入 ECS**：建造、拆除、旋转和配方切换由输入层生成请求，通过 ECB 在明确的同步点提交。
4. **表现层只读逻辑状态**：GameObject 特效、UI 和音效可以读取 Entity 状态，但不能反向直接修改组件；修改必须发送命令。
5. **跨建筑运输统一仲裁**：矿机、熔炉、仓库、合并器和分流器不得绕过 `TransferSystem` 直接写入相邻建筑。
6. **结构变化使用 ECB**：创建/销毁 Entity、增加/删除组件使用 ECB；普通数值更新直接写组件。
7. **高频批量逻辑进入 Job**：数量可能增长到数百或数千的建筑逻辑应使用 Burst Job；少量输入和边界仲裁可保留在主线程 System。
8. **旧 GameObject 逻辑只作为迁移参考**：阶段 0-2 的 `Miner`、`Furnace`、`Storage`、`MergerLogic`、`SplitterLogic` 不再作为阶段 4 正式运行时实现。

#### 推荐数据流

```text
GameObject / UI 输入
        ↓ 创建建造、拆除、设置请求
EntityCommandBuffer
        ↓ 在固定同步点回放
ECS 建筑、生产与物流系统
        ↓ 只读运行时结果
Entities Graphics / GameObject 表现 / UI / 音效
```

#### 后续迁移顺序

1. 先为矿机、熔炉、仓库、合并器和分流器定义纯数据 ECS 组件。
2. 使用 Authoring + Baker 将现有场景配置转换为 Entity。
3. 将生产、加工、库存和调度逻辑迁移到固定步长 System。
4. 接入统一跨建筑转移仲裁，验证阻塞、连续移动和轮询公平性。
5. 最后替换旧 GameObject 运行时脚本，只保留创作、输入和表现职责。

### 实现步骤

**Step 4.1：传送带渲染（Entities Graphics）**

使用 `Entities Graphics` 包渲染 Entity：

```csharp
using Unity.Entities;
using Unity.Rendering;

// 在 Baker 中添加渲染组件
public class BeltBaker : Baker<BeltAuthoring> {
    public override void Bake(BeltAuthoring authoring) {
        Entity entity = GetEntity(TransformUsageFlags.Dynamic);
        
        // ... 添加单一 Belt 组件（包含 CurrentItem 与 Progress）...
        
        // 添加渲染组件
        AddComponent(entity, new URPMaterialPropertyBaseColor { 
            Value = (Vector4)Color.gray 
        });
        
        // 根据方向设置不同的 Mesh
        Mesh beltMesh = GetBeltMesh(authoring.direction);
        AddComponent(entity, new RenderMesh {
            mesh = beltMesh,
            material = GetBeltMaterial()
        });
    }
}
```

**Step 4.2：物品渲染（GPU Instancing）**

不要为每个物品创建独立网格，使用 GPU Instancing 批量渲染：

```csharp
[BurstCompile]
public partial struct ItemRenderJob : IJobEntity {
    public NativeArray<float4x4> InstanceMatrices;
    public int InstanceIndex;
    
    void Execute(ref Item item, in LocalTransform transform) {
        // 收集所有物品的变换矩阵
        InstanceMatrices[InstanceIndex] = float4x4.TRS(
            item.Position,
            quaternion.identity,
            new float3(0.1f, 0.1f, 0.1f)
        );
        InstanceIndex++;
    }
}
```

**Step 4.3：建造交互（射线检测 + ECB）**

使用射线检测获取点击的 Entity，通过 ECB 创建新传送带：

```csharp
public class BuildingInputSystem : SystemBase {
    private EntityCommandBufferSystem ecbSystem;
    
    protected override void OnCreate() {
        ecbSystem = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
    }
    
    protected override void OnUpdate() {
        if (!Input.GetMouseButtonDown(0)) return;
        
        // 发射射线（使用 Unity Physics 或传统 Physics）
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit)) {
            // 获取点击位置的网格坐标
            Vector3Int cell = GridMap.WorldToCell(hit.point);
            
            // 使用 ECB 创建传送带实体
            var ecb = ecbSystem.CreateCommandBuffer();
            Entity beltEntity = ecb.Instantiate(beltPrefabEntity);
            ecb.SetComponent(beltEntity, new Belt {
                Speed = 2f,
                Direction = currentDirection,
                // ...
            });
            ecb.SetComponent(beltEntity, new LocalTransform {
                Position = GridMap.CellToWorld(cell),
                // ...
            });
        }
    }
}
```

### ⚠️ 注意事项

1. **渲染与逻辑分离**：逻辑数据（`Belt.CurrentItem`、`Belt.Progress`）和渲染数据（`RenderMesh`、`LocalTransform`）相互独立。
2. **视野优化**：只更新视野内的物品位置，视野外的只维护逻辑数据。
3. **对象池**：物品 Entity 可以重用，避免频繁创建销毁。
4. **ECB 系统选择**：
   - `BeginSimulationEntityCommandBufferSystem`：在帧开始时回放
   - `EndSimulationEntityCommandBufferSystem`：在帧结束时回放


**Step 4.4：Prefab替换直接在代码中生成**

> GameObject Prefab 负责建筑创作，Baker 转成 Entity Prefab，运行时通过 ECB 实例化。

#### 推荐结构

```text
Assets/Prefabs/Buildings/
├── Belt.prefab
├── Miner.prefab
├── Furnace.prefab
├── Storage.prefab
├── Merger.prefab
└── Splitter.prefab
```

每个 Prefab 可以直接配置：

- Mesh、材质和子物体
- 建筑占地尺寸
- 网格对齐锚点
- 输入、输出端口
- Authoring 参数
- 碰撞体和选择范围
- 特效挂点

#### Prefab 注册表

用一个 Authoring 资产引用所有 GameObject Prefab，并在 Baking 时保存对应 Entity Prefab：

```csharp
public class BuildingPrefabCatalogAuthoring : MonoBehaviour
{
    public GameObject minerPrefab;
    public GameObject furnacePrefab;
    public GameObject storagePrefab;
}

public struct BuildingPrefabCatalog : IComponentData
{
    public Entity Miner;
    public Entity Furnace;
    public Entity Storage;
}

public class BuildingPrefabCatalogBaker
    : Baker<BuildingPrefabCatalogAuthoring>
{
    public override void Bake(BuildingPrefabCatalogAuthoring authoring)
    {
        Entity entity = GetEntity(TransformUsageFlags.None);

        AddComponent(entity, new BuildingPrefabCatalog
        {
            Miner = GetEntity(
                authoring.minerPrefab,
                TransformUsageFlags.Dynamic),
            Furnace = GetEntity(
                authoring.furnacePrefab,
                TransformUsageFlags.Dynamic),
            Storage = GetEntity(
                authoring.storagePrefab,
                TransformUsageFlags.Dynamic)
        });
    }
}
```

这些引用会在 Baking 后指向带有 `Prefab` 组件的 Entity。

#### 运行时生成

```csharp
Entity building = ecb.Instantiate(catalog.Furnace);

ecb.SetComponent(building, new GridPosition
{
    Value = targetCell
});

ecb.SetComponent(building, new LocalTransform
{
    Position = GridToWorld(targetCell),
    Rotation = quaternion.RotateY(rotation),
    Scale = 1f
});
```

当前 Entities 1.4 的 `EntityCommandBuffer.Instantiate(Entity)` API可直接完成此操作。

#### 网格对齐建议

逻辑位置始终使用整数格坐标，不要用浮点世界坐标作为建筑定位依据：

```csharp
public struct GridPosition : IComponentData
{
    public int2 Value;
}

public struct BuildingFootprint : IComponentData
{
    public int2 Size;
    public int2 Pivot;
}
```

对于非矩形建筑，可以使用：

```csharp
public struct OccupiedCellOffset : IBufferElementData
{
    public int2 Value;
}
```

生成建筑时：

1. 旋转占格偏移。
2. 检查每个目标格是否为空。
3. 实例化 Entity Prefab。
4. 写入 `GridPosition` 和旋转。
5. 更新网格占用表。

#### 材质和网格定制

建议优先采用以下顺序：

- 建筑外形完全不同：建立不同 Prefab。
- 同一建筑不同等级：建立 Prefab Variant 或等级配置。
- 只改变颜色：使用 `URPMaterialPropertyBaseColor` 等每实例材质属性。
- 运行时更换 Mesh/Material：使用 `MaterialMeshInfo` 和预先烘焙的 `RenderMeshArray`。
- 不要为每座建筑复制一份独立 Material，否则会破坏批处理。

建筑的逻辑组件与渲染组件仍然分离：

```text
Furnace、GridPosition、BuildingFootprint
            ↓ 逻辑

LocalTransform、MaterialMeshInfo、颜色属性
            ↓ 表现
```

#### 子物体处理

带有多个视觉子物体的 GameObject Prefab 会烘焙成 Entity 层级，实例化根 Entity Prefab 时可以一并复制关联实体。

但输入、输出端口更推荐烘焙成根实体上的 Buffer：

```csharp
public struct BuildingPort : IBufferElementData
{
    public int2 CellOffset;
    public int2 Direction;
    public byte PortType;
}
```

这样运输系统不必遍历视觉子实体。

#### 两个重要限制

- 不要在存档中直接保存 Prefab 的 `Entity` 值。`Entity` 只在当前 World 有效；存档应保存稳定的 `BuildingTypeId`，加载后通过 Prefab 注册表重新映射。
- GameObject Prefab 中的 Authoring 只负责配置和 Baking，运行时建筑状态仍由 ECS 组件唯一保存。

因此，下一步很适合先建立统一的 `BuildingPrefabCatalog`、`GridPosition`、`BuildingFootprint` 和 `BuildingPort`，再为矿机、熔炉、仓库等制作正式建筑 Prefab。

---

## 总结：各阶段系统边界一览

| 系统 | 阶段 0-2 (GameObject) | 阶段 3-4 (ECS/DOTS) |
|:---|:---|:---|
| **传送带逻辑** | 单个 `currentItem + progress` | 单个 `Belt.CurrentItem + Progress` 组件 |
| **时序驱动** | 自定义 `GameManager.OnLogicTick` | `FixedStepSimulationSystemGroup` |
| **物品存储** | `ItemInstance` 单一引用 | `Entity CurrentItem`，`Entity.Null` 表示空 |
| **建造系统** | 直接 `Instantiate` GameObject | `EntityCommandBuffer` + `Baking` |
| **渲染** | `GameObject` + MeshRenderer | `Entities Graphics` + GPU Instancing |
| **环形检测** | 建造时 BFS/并查集 | 建造时 BFS/并查集（不变） |
| **系统更新顺序** | `GameManager` 手动调度 | `[UpdateInGroup]` + `[UpdateAfter]` |

这套方案已经在多个独立游戏项目中验证可行。核心原则是：**先跑通，再优化；先 GameObject，再 ECS**。每一步都有可运行的里程碑，避免一次性跳入 DOTS 的复杂性中。
