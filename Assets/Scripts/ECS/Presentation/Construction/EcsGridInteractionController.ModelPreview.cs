using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class EcsGridInteractionController
{
    private GridCell previewBuildingAnchor;
    private Material modelPreviewMaterial;
    private readonly List<ModelPreview> modelPreviews = new();

    private sealed class ModelPreview
    {
        public GameObject Source;
        public Transform Root;
        public Transform Model;
        public Transform DirectionTriangle;
        public readonly List<Mesh> OwnedMeshes = new();

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            foreach (Mesh mesh in OwnedMeshes) Object.Destroy(mesh);
        }
    }

    private void ShowModelPreviews(in WorldGridConfig grid, Color color)
    {
        HideModelPreviews();
        if (SelectedKind == BuildingKind.Foundation || SelectedKind == BuildingKind.RampFoundation ||
            previewCells.Count == 0 || !TryGetDatabase(out var reference))
            return;

        if (modelPreviewMaterial == null)
        {
            // Loading the shader as a resource also retains it in player builds.
            Shader shader = Resources.Load<Shader>("FactoryPlacementPreview");
            if (shader == null) return;
            modelPreviewMaterial = new Material(shader)
            {
                name = "Building Model Placement Preview",
                hideFlags = HideFlags.DontSave
            };
        }
        modelPreviewMaterial.SetColor("_BaseColor", color);
        EntityManager manager = cachedWorld.EntityManager;
        if (!manager.HasBuffer<BuildingVisualPrefabEntry>(databaseEntity)) return;
        DynamicBuffer<BuildingVisualPrefabEntry> prefabs =
            manager.GetBuffer<BuildingVisualPrefabEntry>(databaseEntity, true);
        ref FactoryDatabaseBlob database = ref reference.Value;
        int count = SelectedKind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(SelectedKind)
            ? previewCells.Count : 1;
        for (int i = 0; i < count; i++)
        {
            BuildingDefinitionId id = SelectedBuildingDefinition;
            byte turns = quarterTurns;
            if (verticalPathStarted)
            {
                if (!VerticalBeltUtility.TryGetPathDefinitions(ref database, verticalEntryLevel,
                        out var segmentId, out var exitId)) continue;
                id = i == count - 1 ? exitId : segmentId;
                if (i < count - 1)
                    turns = VerticalPreviewExitCell.Level > verticalPathStart.Level ? (byte)0 : (byte)2;
            }
            if (!FactoryDatabaseUtility.TryGetBuildingDefinition(ref database, id, out var building)) continue;
            GameObject source = null;
            for (int p = 0; p < prefabs.Length; p++)
                if (prefabs[p].BuildingDefinition == id) { source = prefabs[p].PreviewPrefab.Value; break; }
            if (source == null) continue;
            while (modelPreviews.Count <= i) modelPreviews.Add(null);
            ModelPreview ghost = modelPreviews[i];
            if (ghost == null || ghost.Source != source)
            {
                ghost?.Destroy();
                ghost = new ModelPreview { Source = source };
                ghost.Root = NewPreviewTransform("Building Model Preview " + i, previewRoot);
                ghost.Model = CopyPreviewHierarchy(source.transform, ghost.Root, ghost);
                ghost.DirectionTriangle = ghost.Model.Find("Direction Triangle");
                modelPreviews[i] = ghost;
            }

            GridCell cell = count == 1 && SelectedKind != BuildingKind.Belt && !verticalPathStarted
                ? previewBuildingAnchor : previewCells[i];
            if (SelectedKind == BuildingKind.Belt && i < previewDisplayDirections.Count)
            {
                int2 direction = previewDisplayDirections[i];
                // Corners face their outgoing segment; diagonal directions are only for the arrow.
                if (i + 1 < previewCells.Count)
                    direction = previewCells[i + 1].Horizontal - cell.Horizontal;
                else if (i > 0) direction = cell.Horizontal - previewCells[i - 1].Horizontal;
                turns = EcsGridUtility.QuarterTurnsFromDirection(direction);
            }
            float3 center = EcsGridUtility.CellToWorldCenter(cell, grid);
            float2 offset = EcsGridUtility.GetVisualCenterOffset(
                new int2(building.FootprintWidth, building.FootprintHeight), turns) * grid.CellSize;
            center.x += offset.x;
            center.z += offset.y;
            quaternion rotation = EcsGridUtility.RotationFromQuarterTurns(turns);
            Vector3 scale = Vector3.one;
            if (SelectedKind == BuildingKind.Belt && TryGetRamp(cachedWorld, cell, out var ramp))
            {
                int2 travel = EcsGridUtility.Rotate(new int2(1, 0), turns);
                float rise = (math.all(travel == ramp.Connector.UphillDirection) ? 1f : -1f) *
                    (ramp.Connector.HighHeight.ToWorldY(grid) - ramp.Connector.LowHeight.ToWorldY(grid));
                center = RampUtility.GetSurfaceCenter(ramp.Connector, grid);
                rotation = math.mul(rotation, quaternion.RotateZ(math.atan2(rise, grid.CellSize)));
                scale.x = RampUtility.GetSlopeLength(ramp.Connector, grid) / grid.CellSize;
            }
            ghost.Root.SetPositionAndRotation((Vector3)center,
                new Quaternion(rotation.value.x, rotation.value.y, rotation.value.z, rotation.value.w));
            ghost.Root.localScale = scale;
            ghost.Model.localPosition = source.transform.localPosition;
            ghost.Model.localRotation = source.transform.localRotation;
            ghost.Model.localScale = source.transform.localScale;
            if (building.Kind == BuildingKind.Belt && ghost.DirectionTriangle != null &&
                i < previewDisplayDirections.Count)
            {
                // Match the placed belt: the model faces the outgoing segment, while
                // its arrow bisects the incoming and outgoing directions at corners.
                int2 localDirection = EcsGridUtility.Rotate(previewDisplayDirections[i], -turns);
                ghost.DirectionTriangle.localRotation = Quaternion.Euler(
                    0f, -math.atan2(localDirection.y, localDirection.x) * math.TODEGREES, 0f);
            }
            if (VerticalBeltUtility.IsVertical(building.Kind))
            {
                ghost.Model.localScale = Vector3.Scale(source.transform.localScale,
                    new Vector3(grid.CellSize, grid.LayerHeight, grid.CellSize));
                if (building.Kind == BuildingKind.VerticalBeltSegment && VerticalBeltUtility.Step(turns) < 0)
                {
                    ghost.Model.localPosition = new Vector3(0f, grid.LayerHeight, 0f);
                    ghost.Model.localRotation = Quaternion.Euler(180f, 0f, 0f);
                }
            }
            ghost.Root.gameObject.SetActive(true);
        }
    }

    private static Transform NewPreviewTransform(string name, Transform parent)
    {
        var result = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = 2 };
        result.transform.SetParent(parent, false);
        return result.transform;
    }

    private Transform CopyPreviewHierarchy(Transform source, Transform parent, ModelPreview ghost)
    {
        Transform copy = NewPreviewTransform(source.name, parent);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        copy.gameObject.SetActive(source.gameObject.activeSelf);
        Mesh mesh = null;
        Renderer renderer = source.GetComponent<MeshRenderer>();
        if (renderer != null && source.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
        else if (source.TryGetComponent<SkinnedMeshRenderer>(out var skinned))
        {
            renderer = skinned;
            if (skinned.sharedMesh != null)
            {
                mesh = new Mesh { hideFlags = HideFlags.DontSave };
                skinned.BakeMesh(mesh);
                ghost.OwnedMeshes.Add(mesh);
            }
        }
        if (mesh != null && renderer.enabled)
        {
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var visual = copy.gameObject.AddComponent<MeshRenderer>();
            var materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = modelPreviewMaterial;
            visual.sharedMaterials = materials;
            visual.shadowCastingMode = ShadowCastingMode.Off;
            visual.receiveShadows = false;
            visual.lightProbeUsage = LightProbeUsage.Off;
            visual.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        // Copy only transforms and meshes: prefab scripts and colliders never run on ghosts.
        foreach (Transform child in source) CopyPreviewHierarchy(child, copy, ghost);
        return copy;
    }

    private void HideModelPreviews()
    {
        foreach (var ghost in modelPreviews)
            if (ghost != null) ghost.Root.gameObject.SetActive(false);
    }

    private void DestroyModelPreviews()
    {
        foreach (var ghost in modelPreviews) ghost?.Destroy();
        modelPreviews.Clear();
        if (modelPreviewMaterial != null) Destroy(modelPreviewMaterial);
    }
}
