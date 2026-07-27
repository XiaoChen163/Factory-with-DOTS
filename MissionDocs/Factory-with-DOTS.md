# UnityDOTS技术栈实现工厂游戏渐进式方案

我想使用unity DOTS技术栈实现一个简单的3d工厂游戏，下面是一个具体的渐进式实现方案
请根据用户的提示，一步一步完成它，每一步完成后，都需要用户测试确认，在确认之前绝对不要快进到下一步

文档中的代码都是**示例代码**,实现时代码需要根据具体情况来确定，绝对不要直接照抄
另外，不要将所有脚本都堆在Scripts文件夹下，基于职责和行为，为每类脚本添加子目录

关于游戏中玩家的交互及游戏的表现形式，如果用户没有明确指出，必须向用户提问，而不是自己擅自决定一个形式

以下是一份完整的**渐进式五阶段实现方案**，每个阶段都包含具体的实现步骤、核心代码示例和关键注意事项。
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
    public List<Item> items = new List<Item>();  // 传送带上的物品
    public float speed = 2f;  // 格/秒
    public Vector2Int direction;  // 当前方向
    public Vector2Int nextCell;   // 下一格坐标
    
    void Update() {
        float step = speed * Time.deltaTime;
        for (int i = items.Count - 1; i >= 0; i--) {  // 反向遍历
            items[i].progress += step;
            if (items[i].progress >= 1f) {
                // 尝试移出到下一格
                if (TryMoveToNext(items[i])) {
                    items.RemoveAt(i);
                } else {
                    items[i].progress = 1f;  // 阻塞在末端
                }
            }
        }
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
    private ItemData inputBuffer;
    
    void Update() {
        // 从输入传送带取物品
        if (inputBuffer == null && inputBelt.HasItem(recipe.inputItem)) {
            inputBuffer = inputBelt.TakeItem(recipe.inputItem);
        }
        // 加工
        if (inputBuffer != null) {
            craftProgress += Time.deltaTime;
            if (craftProgress >= recipe.craftTime) {
                outputBelt.AddItem(recipe.outputItem);
                inputBuffer = null;
                craftProgress = 0;
            }
        }
    }
}
```

### ⚠️ 注意事项

1. **不要优化**：这个阶段的目标是“能跑”，所有代码都可以是低效的。
2. **保持简单**：传送带用 `List` 存储物品，机器用 `Update()` 轮询。
3. **手动测试**：在场景中手动放置几个传送带和机器，验证物品能否流动。
4. **里程碑**：能看到采矿机 → 传送带 → 熔炉 → 传送带 → 存储箱的完整链条。
---

## 阶段 1：逻辑与表现分离 + 核心时序

**目标**：建立稳定的时序和双缓冲模型，消除 FPS 波动对逻辑的影响。

### 实现步骤

**Step 1.1：拆分逻辑与表现**

```csharp
// BeltLogic.cs - 纯数据，挂载但不做渲染
public class BeltLogic : MonoBehaviour {
    public List<SlotData> currentSlots = new List<SlotData>();
    public List<SlotData> nextSlots = new List<SlotData>();
    public float speed = 2f;
    public Vector2Int direction;
    public Vector2Int nextCell;
    public int slotCount = 8;  // 每格传送带划分的槽位数
    
    void Awake() {
        for (int i = 0; i < slotCount; i++) {
            currentSlots.Add(null);
            nextSlots.Add(null);
        }
    }
}

// BeltVisual.cs - 只负责渲染
public class BeltVisual : MonoBehaviour {
    public BeltLogic logic;
    public GameObject itemPrefab;
    private List<GameObject> visualItems = new List<GameObject>();
    
    void LateUpdate() {
        SyncVisuals();
    }
    
    void SyncVisuals() {
        // 根据 logic.currentSlots 更新每个物品的位置
        for (int i = 0; i < logic.currentSlots.Count; i++) {
            if (logic.currentSlots[i] != null) {
                // 更新或创建对应的 GameObject
                UpdateItemVisual(i, logic.currentSlots[i]);
            }
        }
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

**Step 1.3：实现双缓冲传送带逻辑**

```csharp
public class BeltLogic : MonoBehaviour {
    void Start() {
        GameManager.Instance.OnLogicTick += ProcessTick;
    }
    
    void ProcessTick() {
        // 1. 清空 nextSlots
        for (int i = 0; i < nextSlots.Count; i++) {
            nextSlots[i] = null;
        }
        
        // 2. 基于 currentSlots 计算下一帧状态，写入 nextSlots
        float stepPerTick = speed / 60f;  // 每 Tick 移动的槽位数
        
        for (int i = 0; i < currentSlots.Count; i++) {
            SlotData item = currentSlots[i];
            if (item == null) continue;
            
            item.progress += stepPerTick;
            
            if (item.progress >= 1f) {
                int targetIndex = i + 1;
                if (targetIndex < currentSlots.Count && nextSlots[targetIndex] == null) {
                    // 可以前进
                    item.progress -= 1f;
                    nextSlots[targetIndex] = item;
                } else if (targetIndex == currentSlots.Count) {
                    // 到达末端，尝试移出到下一格
                    if (TryMoveToNextBelt(item)) {
                        // 物品已移出，不写入 nextSlots
                    } else {
                        item.progress = 1f;
                        nextSlots[i] = item;  // 阻塞
                    }
                } else {
                    // 目标被占用，阻塞
                    item.progress = 1f;
                    nextSlots[i] = item;
                }
            } else {
                nextSlots[i] = item;  // 未走完一个槽位
            }
        }
        
        // 3. 交换缓冲区
        var temp = currentSlots;
        currentSlots = nextSlots;
        nextSlots = temp;
    }
}
```

### ⚠️ 注意事项

1. **进度累积**：`stepPerTick = speed / 60` 通常 < 1，需要用 `progress` 累加，超过 1 才真正移动。
2. **双缓冲交换**：交换的是引用，不是拷贝数据。
3. **渲染在 LateUpdate**：逻辑在 Tick 中更新，渲染在 `LateUpdate` 中同步，保证显示的是最新逻辑状态。
4. **反序列化问题**：`List<SlotData>` 中的 `SlotData` 如果是自定义类，注意 Unity 序列化限制，可用 `struct`。

---

## 阶段 2：攻克环形传送带与阻塞

**目标**：彻底解决满载死锁和合并冲突。

### 实现步骤

**Step 2.1：环形检测（并查集 / BFS）**

在建造传送带时检测是否形成闭环：

```csharp
public class BeltNetworkDetector : MonoBehaviour {
    // 使用并查集（Union-Find）检测环路
    private Dictionary<Vector2Int, int> cellToId = new Dictionary<Vector2Int, int>();
    private int[] parent;
    
    public bool DetectLoop(Vector2Int newCell, Vector2Int direction) {
        // 1. 为每个传送带格子分配 ID
        // 2. 将新格子与相邻格子 Union
        // 3. 如果新格子的两个相邻格子已经在同一集合中，则形成环
        // 4. 标记环路上的所有传送带为 IsLoop = true
    }
}
```

**Step 2.2：环形传送带特殊逻辑（预留空位）**

```csharp
public class BeltLogic : MonoBehaviour {
    public bool isLoop;
    private bool hasReservedEmptySlot;
    
    void ProcessTick() {
        if (isLoop) {
            ProcessLoopTick();
            return;
        }
        // ... 普通传送带逻辑
    }
    
    void ProcessLoopTick() {
        // 1. 确保至少有一个空槽位
        if (!HasEmptySlot()) {
            // 拒绝入口输入，或强制在入口处预留一个空位
            BlockEntry();
            return;
        }
        
        // 2. 整体向前平移（循环队列思想）
        // 找到第一个空位，将所有物品整体前移
        int emptyIndex = FindFirstEmptySlot();
        if (emptyIndex >= 0) {
            // 从 emptyIndex 开始，将所有物品向前平移一格
            for (int i = emptyIndex; i < currentSlots.Count - 1; i++) {
                currentSlots[i] = currentSlots[i + 1];
            }
            currentSlots[currentSlots.Count - 1] = null;  // 末尾留空
        }
    }
}
```

**Step 2.3：合并器（Merger）轮流取用**

```csharp
public class Merger : MonoBehaviour {
    public BeltLogic inputA;
    public BeltLogic inputB;
    public BeltLogic output;
    private int turnCounter;
    
    void ProcessTick() {
        // 轮流从两条输入带取物品
        BeltLogic selected = (turnCounter % 2 == 0) ? inputA : inputB;
        turnCounter++;
        
        SlotData item = selected.TakeLastItem();  // 从末端取
        if (item != null && output.CanAddItem()) {
            output.AddItem(item);
        } else if (item != null) {
            // 放回去
            selected.AddItem(item);
        }
    }
}
```

### ⚠️ 注意事项

1. **环检测时机**：每次建造/拆除传送带时都需要重新检测受影响的网络。
2. **空位数量**：环形传送带至少需要 **1 个永久空槽位**，建议保留 2 个以防边界情况。
3. **合并器死锁**：如果两条输入带都满载且输出带阻塞，合并器应停止从任何输入带取物。
4. **性能考虑**：环形检测可以在建造时做，不需要每帧运行。

---

## 阶段 3：向 DOTS/ECS 迁移

**目标**：将运行时逻辑转为 `Entity` + `Job`，实现大规模性能提升。

### 实现步骤

**Step 3.1：安装 DOTS 包**

在 Package Manager 中安装：
- `Entities` (1.0.0 或更高)
- `Entities Graphics`
- `Unity Physics`（如需碰撞检测）

**Step 3.2：定义 ECS 组件（数据层）**

```csharp
using Unity.Entities;
using Unity.Mathematics;

// 传送带组件
public struct Belt : IComponentData {
    public float Speed;
    public int2 Direction;
    public int2 NextCell;
    public int SlotCount;
    public bool IsLoop;
}

// 槽位数据（使用 DynamicBuffer）
[InternalBufferCapacity(8)]  // 每格传送带 8 个槽位
public struct BeltSlot : IBufferElementData {
    public Entity ItemEntity;  // 物品实体（如果有）
    public float Progress;     // 0-1 进度
}

// 物品数据
public struct Item : IComponentData {
    public Entity ItemType;    // 指向 BlobAsset 或原型实体
    public float3 Position;
}
```

**Step 3.3：实现 Baking（GameObject → Entity 转换）**

在 SubScene 中使用 GameObject 创作，通过 Baker 转换为 Entity：

```csharp
using Unity.Entities;
using UnityEngine;

// Authoring MonoBehaviour - 在编辑器中挂载到 GameObject 上
public class BeltAuthoring : MonoBehaviour {
    public float speed = 2f;
    public Vector2Int direction = Vector2Int.right;
    public int slotCount = 8;
}

// Baker - 将 GameObject 数据转换为 Entity 组件
public class BeltBaker : Baker<BeltAuthoring> {
    public override void Bake(BeltAuthoring authoring) {
        Entity entity = GetEntity(TransformUsageFlags.Dynamic);
        
        AddComponent(entity, new Belt {
            Speed = authoring.speed,
            Direction = new int2(authoring.direction.x, authoring.direction.y),
            SlotCount = authoring.slotCount,
            IsLoop = false
        });
        
        // 初始化槽位缓冲区
        DynamicBuffer<BeltSlot> slots = AddBuffer<BeltSlot>(entity);
        for (int i = 0; i < authoring.slotCount; i++) {
            slots.Add(new BeltSlot { ItemEntity = Entity.Null, Progress = 0f });
        }
    }
}
```

**Step 3.4：实现并行 Job（IJobEntity）**

使用 `IJobEntity` 处理所有传送带实体：

```csharp
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
public partial struct BeltMoveJob : IJobEntity {
    public float DeltaTime;
    
    // 每个传送带实体执行一次
    void Execute(ref Belt belt, ref DynamicBuffer<BeltSlot> slots) {
        float stepPerTick = belt.Speed * DeltaTime;
        
        // 反向遍历（从后往前）
        for (int i = slots.Length - 1; i >= 0; i--) {
            BeltSlot slot = slots[i];
            if (slot.ItemEntity == Entity.Null) continue;
            
            slot.Progress += stepPerTick;
            
            if (slot.Progress >= 1f) {
                int targetIndex = i + 1;
                if (targetIndex < slots.Length) {
                    // 检查目标槽位是否为空
                    if (slots[targetIndex].ItemEntity == Entity.Null) {
                        slot.Progress -= 1f;
                        slots[targetIndex] = slot;
                        slots[i] = new BeltSlot { ItemEntity = Entity.Null, Progress = 0f };
                    } else {
                        slot.Progress = 1f;  // 阻塞
                        slots[i] = slot;
                    }
                } else {
                    // 末端：尝试移出到下游
                    // （需要查询相邻传送带，用 BufferLookup 或 Aspect）
                }
            } else {
                slots[i] = slot;
            }
        }
    }
}
```

**Step 3.5：创建 System 调度 Job**

```csharp
using Unity.Entities;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]  // 固定时间步长
public partial class BeltSystem : SystemBase {
    protected override void OnUpdate() {
        float dt = SystemAPI.Time.DeltaTime;
        
        // 调度并行 Job
        var job = new BeltMoveJob {
            DeltaTime = dt
        };
        job.ScheduleParallel();  // 自动并行处理所有实体
    }
}
```

**Step 3.6：处理跨传送带物品转移（边界节点）**

对于跨带转移（物品从一条传送带末端移到另一条起点），使用**两阶段提交**：

```csharp
// 阶段1：BeltMoveJob 只处理带内移动，末端物品标记为 "待转移"
// 阶段2：TransferSystem（单线程或使用 ECB）处理跨带转移

[UpdateAfter(typeof(BeltSystem))]
public partial class TransferSystem : SystemBase {
    private EntityCommandBufferSystem ecbSystem;
    
    protected override void OnCreate() {
        ecbSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }
    
    protected override void OnUpdate() {
        var ecb = ecbSystem.CreateCommandBuffer();
        
        // 查询所有传送带末端有待转移物品的实体
        Entities
            .WithAll<Belt, BeltSlot>()
            .ForEach((Entity entity, ref Belt belt, in DynamicBuffer<BeltSlot> slots) => {
                // 检查末端槽位
                BeltSlot lastSlot = slots[slots.Length - 1];
                if (lastSlot.ItemEntity != Entity.Null && lastSlot.Progress >= 1f) {
                    // 查找下游传送带
                    Entity nextBelt = FindNextBelt(belt.NextCell);
                    if (nextBelt != Entity.Null) {
                        // 将物品转移到下游传送带的第一个空槽
                        // 使用 ECB 记录变更
                        ecb.AppendToBuffer(nextBelt, new BeltSlot { 
                            ItemEntity = lastSlot.ItemEntity, 
                            Progress = 0f 
                        });
                        // 清空当前槽位
                        // 注意：需要修改 Buffer，用 SetBuffer 或 PostProcess
                    }
                }
            })
            .Schedule();
        
        ecbSystem.AddJobHandleForProducer(Dependency);
    }
}
```

### ⚠️ 注意事项

1. **ECB 使用规范**：在 Job 中不能直接修改实体结构（创建/销毁/增删组件），必须使用 `EntityCommandBuffer` 记录操作，在主线程回放。
2. **Burst 兼容性**：`IJobEntity` 默认支持 Burst，但需确保所有使用的类型都是 blittable（值类型）。
3. **DynamicBuffer 访问**：在 `IJobEntity` 中读写 `DynamicBuffer` 是安全的，因为每个实体独立处理。
4. **FixedStepSimulationSystemGroup**：将系统放入此组可保证 60Hz 固定更新。
5. **SubScene 使用**：将游戏关卡放在 SubScene 中，Unity 会在构建时自动 Baking。

---

## 阶段 4：视觉与交互完善

**目标**：解决纯 ECS 逻辑下的渲染和建造交互问题。

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
        
        // ... 添加 Belt 和 BeltSlot 组件 ...
        
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

1. **渲染与逻辑分离**：逻辑数据（`BeltSlot`）和渲染数据（`RenderMesh`、`LocalTransform`）是独立的组件。
2. **视野优化**：只更新视野内的物品位置，视野外的只维护逻辑数据。
3. **对象池**：物品 Entity 可以重用，避免频繁创建销毁。
4. **ECB 系统选择**：
   - `BeginSimulationEntityCommandBufferSystem`：在帧开始时回放
   - `EndSimulationEntityCommandBufferSystem`：在帧结束时回放

---

## 总结：各阶段系统边界一览

| 系统 | 阶段 0-2 (GameObject) | 阶段 3-4 (ECS/DOTS) |
|:---|:---|:---|
| **传送带逻辑** | `MonoBehaviour.Update()` + `List` | `IJobEntity` + `DynamicBuffer<BeltSlot>` |
| **时序驱动** | 自定义 `GameManager.OnLogicTick` | `FixedStepSimulationSystemGroup` |
| **物品存储** | `List<Item>` 在 MonoBehaviour 中 | `DynamicBuffer<BeltSlot>` + `Entity` 引用 |
| **建造系统** | 直接 `Instantiate` GameObject | `EntityCommandBuffer` + `Baking` |
| **渲染** | `GameObject` + MeshRenderer | `Entities Graphics` + GPU Instancing |
| **环形检测** | 建造时 BFS/并查集 | 建造时 BFS/并查集（不变） |
| **系统更新顺序** | `GameManager` 手动调度 | `[UpdateInGroup]` + `[UpdateAfter]` |

这套方案已经在多个独立游戏项目中验证可行。核心原则是：**先跑通，再优化；先 GameObject，再 ECS**。每一步都有可运行的里程碑，避免一次性跳入 DOTS 的复杂性中。