using System;
using Unity.Entities;

/// <summary>
/// One world-owned drain fans out committed events after all fixed ticks in a frame.
/// Subscribe before simulation; subscribers must not use events to perform formal consequences.
/// Sequence gaps plus DroppedEventCount on the resolver diagnose bounded-ring overflow.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class HealthPresentationExportSystem : SystemBase
{
    public event Action<HealthEvent> EventReceived;

    public bool TryGetSnapshot(Entity target, out HealthSnapshot snapshot)
    {
        var resolver = World.GetExistingSystemManaged<HealthResolveSystem>();
        snapshot = default;
        return resolver != null && resolver.TryGetSnapshot(target, out snapshot);
    }

    protected override void OnUpdate()
    {
        var resolver = World.GetExistingSystemManaged<HealthResolveSystem>();
        if (resolver == null) return;
        while (resolver.TryTakeEvent(out HealthEvent value)) EventReceived?.Invoke(value);
    }

    protected override void OnDestroy() => EventReceived = null;
}
