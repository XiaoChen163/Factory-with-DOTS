using Unity.Entities;
using Unity.Rendering;

public static class ItemRenderingUtility
{
    // DisableRendering applies to one entity, not its transform descendants.
    // FBX items keep their mesh entities in the root's flat LinkedEntityGroup.
    public static void SetVisible(
        Entity root,
        bool visible,
        in BufferLookup<LinkedEntityGroup> groups,
        in ComponentLookup<DisableRendering> disabled,
        ref EntityCommandBuffer ecb)
    {
        SetEntityVisible(root, visible, disabled, ref ecb);
        if (!groups.HasBuffer(root)) return;
        foreach (var member in groups[root])
            if (member.Value != root)
                SetEntityVisible(member.Value, visible, disabled, ref ecb);
    }

    private static void SetEntityVisible(
        Entity entity,
        bool visible,
        in ComponentLookup<DisableRendering> disabled,
        ref EntityCommandBuffer ecb)
    {
        bool hidden = disabled.HasComponent(entity);
        if (visible && hidden) ecb.RemoveComponent<DisableRendering>(entity);
        else if (!visible && !hidden) ecb.AddComponent<DisableRendering>(entity);
    }
}
