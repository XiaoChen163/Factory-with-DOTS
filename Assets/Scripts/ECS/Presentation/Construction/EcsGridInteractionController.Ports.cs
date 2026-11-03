using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public sealed partial class EcsGridInteractionController
{
    public const float OutputPortInteractionDistance = 10f;
    private EntityQuery portHoverQuery;
    private TextMesh outputPortLabel;
    private Material outputPortLabelMaterial;

    private void UpdateOutputPortLabel()
    {
        if (inputMode != null && inputMode.BlocksWorldInput ||
            !TryGetHoveredOutputPort(out ushort number, out Vector3 position))
        {
            HideOutputPortLabel();
            return;
        }
        if (outputPortLabel == null)
        {
            var label = new GameObject("Output Port Number", typeof(TextMesh));
            label.hideFlags = HideFlags.DontSave;
            label.layer = gameObject.layer;
            label.transform.SetParent(transform, false);
            outputPortLabel = label.GetComponent<TextMesh>();
            outputPortLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            outputPortLabel.fontSize = 64;
            outputPortLabel.characterSize = 0.06f;
            outputPortLabel.anchor = TextAnchor.MiddleCenter;
            outputPortLabel.alignment = TextAlignment.Center;
            outputPortLabel.color = new Color(0.5f, 1f, 0.7f);
            outputPortLabelMaterial = new Material(Resources.Load<Shader>("FactoryWorldText"));
            outputPortLabelMaterial.hideFlags = HideFlags.HideAndDontSave;
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = outputPortLabelMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        outputPortLabel.gameObject.SetActive(true);
        outputPortLabel.text = number.ToString();
        // TextMesh refreshes its dynamic-font atlas when the glyphs are requested.
        outputPortLabelMaterial.mainTexture = outputPortLabel.font.material.mainTexture;
        outputPortLabel.transform.SetPositionAndRotation(position, inputCamera.transform.rotation);
        outputPortLabel.transform.localScale = Vector3.one * GetWorldGridConfig().CellSize;
    }

    private void HideOutputPortLabel()
    {
        if (outputPortLabel != null) outputPortLabel.gameObject.SetActive(false);
    }

    private void DestroyOutputPortLabel()
    {
        if (outputPortLabel != null) Destroy(outputPortLabel.gameObject);
        if (outputPortLabelMaterial != null) Destroy(outputPortLabelMaterial);
        outputPortLabel = null;
        outputPortLabelMaterial = null;
    }

    public bool TryGetHoveredOutputPort(out ushort number, out Vector3 worldPosition)
    {
        number = 0;
        worldPosition = default;
        if (inputCamera == null || !TryGetGrid(out World world, out _, out WorldGridConfig grid))
            return false;

        Ray ray = CreateInteractionRay();
        float nearestPort = OutputPortInteractionDistance;
        float nearestBody = OutputPortInteractionDistance;
        Vector3 selectedPosition = default;
        Vector3 selectedOutward = default;
        EntityManager manager = world.EntityManager;
        using NativeArray<Entity> entities = portHoverQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            GridPlacement placement = manager.GetComponentData<GridPlacement>(entity);
            if (placement.Kind == BuildingKind.Foundation || placement.Kind == BuildingKind.RampFoundation)
                continue; // Their real colliders are tested below.
            Vector3 anchor = EcsGridUtility.CellToWorldCenter(placement.AnchorCell, grid);
            float reach = OutputPortInteractionDistance +
                (placement.FootprintSize.x + placement.FootprintSize.y) * grid.CellSize;
            if ((anchor - ray.origin).sqrMagnitude > reach * reach)
                continue;

            // Buildings do not yet have physical colliders. Use their actual occupied
            // cells for body occlusion, including the near side of the selected machine.
            float height = placement.Kind == BuildingKind.Processor || placement.Kind == BuildingKind.Storage
                ? 1.2f * grid.CellSize : 0.5f * grid.CellSize;
            DynamicBuffer<OccupiedCellOffset> occupied = manager.GetBuffer<OccupiedCellOffset>(entity, true);
            foreach (OccupiedCellOffset offset in occupied)
            {
                Vector3 center = EcsGridUtility.CellToWorldCenter(
                    EcsGridUtility.GetBuildingCell(placement, offset.Value), grid);
                center.y += height * 0.5f;
                Bounds body = new(center, new Vector3(grid.CellSize, height, grid.CellSize));
                if (body.IntersectRay(ray, out float distance) && distance < nearestBody)
                    nearestBody = distance;
            }

            if (placement.Kind != BuildingKind.Processor || !manager.HasBuffer<BuildingPort>(entity))
                continue;
            foreach (BuildingPort port in manager.GetBuffer<BuildingPort>(entity, true))
            {
                if (!TryHitOutputPort(ray, placement, port, grid, nearestPort, out float distance, out Vector3 position))
                    continue;
                nearestPort = distance;
                selectedPosition = position;
                int2 direction = EcsGridUtility.Rotate(port.Direction, placement.QuarterTurns);
                selectedOutward = new Vector3(direction.x, 0f, direction.y);
                number = port.Number;
            }
        }
        if (number == 0 || nearestBody + 0.02f * grid.CellSize < nearestPort)
            return false;
        if (physicsWorldQuery.CalculateEntityCount() == 1)
        {
            Unity.Physics.PhysicsWorldSingleton physics = physicsWorldQuery.GetSingleton<Unity.Physics.PhysicsWorldSingleton>();
            var input = new Unity.Physics.RaycastInput
            {
                Start = ray.origin,
                End = ray.GetPoint(nearestPort),
                Filter = Unity.Physics.CollisionFilter.Default
            };
            var motor = GetComponentInParent<EcsCharacterMotor>();
            var collector = new OutputPortOcclusionCollector
                { IgnoredEntity = motor != null ? motor.CharacterEntity : Entity.Null };
            if (physics.CastRay(input, ref collector))
                return false;
        }
        if (Physics.Raycast(ray, nearestPort, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        worldPosition = selectedPosition + (selectedOutward * 0.1f + Vector3.up * 0.2f) * grid.CellSize;
        // Keep billboard glyphs off the machine face even when viewed obliquely.
        worldPosition += (ray.origin - worldPosition).normalized * (0.04f * grid.CellSize);
        return true;
    }

    private struct OutputPortOcclusionCollector : Unity.Physics.ICollector<Unity.Physics.RaycastHit>
    {
        public Entity IgnoredEntity;
        public bool EarlyOutOnFirstHit => true;
        public float MaxFraction => 1f;
        public int NumHits { get; private set; }
        public bool AddHit(Unity.Physics.RaycastHit hit)
        {
            if (hit.Entity == IgnoredEntity ||
                hit.Material.CollisionResponse == Unity.Physics.CollisionResponsePolicy.RaiseTriggerEvents)
                return false;
            NumHits++;
            return true;
        }
    }

    public static bool TryHitOutputPort(Ray ray, in GridPlacement placement, in BuildingPort port,
        in WorldGridConfig grid, float maxDistance, out float distance, out Vector3 position)
    {
        distance = 0f;
        position = default;
        if (port.Type != BuildingPortType.Output || port.Number == 0 || port.OutputSlot == 0)
            return false;
        int2 direction = EcsGridUtility.Rotate(port.Direction, placement.QuarterTurns);
        position = EcsGridUtility.CellToWorldCenter(
            EcsGridUtility.GetBuildingCell(placement, port.CellOffset), grid);
        position += new Vector3(-direction.x * 0.5f, 0.375f, -direction.y * 0.5f) * grid.CellSize;
        float limit = Mathf.Min(maxDistance, OutputPortInteractionDistance);
        if ((position - ray.origin).sqrMagnitude > OutputPortInteractionDistance * OutputPortInteractionDistance)
            return false;
        // OutputPortView is a 0.07 x 0.75 x 1 block. Include its full visible
        // surface plus a small tolerance, rather than only its central region.
        Vector3 size = new Vector3(direction.x != 0 ? 0.12f : 1.04f, 0.79f,
            direction.y != 0 ? 0.12f : 1.04f) * grid.CellSize;
        return new Bounds(position, size).IntersectRay(ray, out distance) && distance <= limit;
    }
}
