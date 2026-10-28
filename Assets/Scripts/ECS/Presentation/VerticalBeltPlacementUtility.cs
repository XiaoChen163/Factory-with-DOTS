using Unity.Mathematics;
using UnityEngine;

/// <summary>Projects the live pointer ray onto a view-facing vertical plane, then onto the entry column.</summary>
public static class VerticalBeltPlacementUtility
{
    public static bool TryResolveExit(Ray pointerRay, Vector3 viewForward, GridCell entry,
        in WorldGridConfig grid, out GridCell exit)
    {
        exit = entry;
        Vector3 normal = new Vector3(viewForward.x, 0f, viewForward.z);
        if (normal.sqrMagnitude < .000001f || grid.LayerHeight <= 0f || grid.CellSize <= 0f)
            return false;
        normal.Normalize();
        if (Mathf.Abs(Vector3.Dot(normal, pointerRay.direction)) < .00001f)
            return false;
        Vector3 center = (Vector3)EcsGridUtility.CellToWorldCenter(entry, grid);
        Plane plane = new Plane(normal, center);
        if (!plane.Raycast(pointerRay, out float distance) || !math.isfinite(distance))
            return false;
        Vector3 hit = pointerRay.GetPoint(distance);
        if (!math.all(math.isfinite((float3)hit))) return false;

        // Clamp before converting to integer so distant or grazing rays cannot overflow.
        double level = ((double)hit.y - grid.Origin.y) / grid.LayerHeight;
        long lower = System.Math.Max(0L, (long)entry.Level - VerticalBeltUtility.MaxPathNodes);
        long upper = System.Math.Min(int.MaxValue, (long)entry.Level + VerticalBeltUtility.MaxPathNodes);
        exit.Level = (int)System.Math.Round(System.Math.Clamp(level, lower, upper));
        return true;
    }
}
