using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

/// <summary>Source-table footprint validation shared by CSV import and baking.</summary>
public static class FactoryBuildingFootprintUtility
{
    // Bitmap rows run from local Z=0 upwards; columns run from local X=0.
    // Keep empty borders: dimensions also define the model pivot and port coordinates.
    public static int2[] GetOccupiedOffsets(in FactoryBuildingDefinitionRow building)
    {
        string label = $"Building '{building.key}'";
        if (building.footprintWidth == 0 || building.footprintHeight == 0)
            throw new InvalidDataException($"{label} footprint must be positive.");

        string bitmap = building.footprintBitmap ?? string.Empty;
        if (building.isRectangle && !string.IsNullOrWhiteSpace(bitmap))
            throw new InvalidDataException($"{label} is rectangular and must not declare a bitmap.");

        string[] rows = null;
        if (!building.isRectangle)
        {
            rows = bitmap.Split('/');
            if (rows.Length != building.footprintHeight)
                throw new InvalidDataException($"{label} bitmap row count must match footprint_height.");
            for (int z = 0; z < rows.Length; z++)
            {
                if (rows[z].Length != building.footprintWidth)
                    throw new InvalidDataException($"{label} bitmap row {z} must match footprint_width.");
                foreach (char cell in rows[z])
                    if (cell != '0' && cell != '1')
                        throw new InvalidDataException($"{label} bitmap may contain only 0, 1 and / row separators.");
            }
        }

        List<int2> offsets = new List<int2>();
        for (int z = 0; z < building.footprintHeight; z++)
        for (int x = 0; x < building.footprintWidth; x++)
            if (building.isRectangle || rows[z][x] == '1')
                offsets.Add(new int2(x, z));
        if (offsets.Count == 0)
            throw new InvalidDataException($"{label} footprint must occupy at least one cell.");
        return offsets.ToArray();
    }

    public static void ValidatePorts(in FactoryBuildingDefinitionRow building, int2[] offsets)
    {
        HashSet<int2> occupied = new HashSet<int2>(offsets);
        HashSet<(BuildingPortType, byte)> identities = new HashSet<(BuildingPortType, byte)>();
        HashSet<(BuildingPortType, int2, int2)> faces = new HashSet<(BuildingPortType, int2, int2)>();
        foreach (FactoryBuildingPortTableRow port in building.ports ?? Array.Empty<FactoryBuildingPortTableRow>())
        {
            string label = $"Building '{building.key}' port {port.type}:{port.index}";
            int2 cell = new int2(port.cellOffset.x, port.cellOffset.y);
            int2 direction = new int2(port.direction.x, port.direction.y);
            if (port.type != BuildingPortType.Input && port.type != BuildingPortType.Output)
                throw new InvalidDataException($"{label} has an invalid type.");
            if (!((direction.x == 0 && (direction.y == 1 || direction.y == -1)) ||
                  (direction.y == 0 && (direction.x == 1 || direction.x == -1))))
                throw new InvalidDataException($"{label} direction must be cardinal.");
            if (!identities.Add((port.type, port.index)) || !faces.Add((port.type, cell, direction)))
                throw new InvalidDataException($"{label} duplicates an index or connection face.");
            int2 ownerCell = port.type == BuildingPortType.Input ? cell + direction : cell - direction;
            if (occupied.Contains(cell) || !occupied.Contains(ownerCell))
                throw new InvalidDataException($"{label} must connect an empty cell to an occupied boundary cell.");
        }
    }
}
