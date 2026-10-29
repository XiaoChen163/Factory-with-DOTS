using System;
using System.Collections.Generic;
using System.IO;

/// <summary>Validates resolved rows before they enter the runtime Blob.</summary>
public static class FactoryDatabaseValidation
{
    public static void Validate(FactoryDatabaseAsset source)
    {
        if (source == null || source.items == null || source.machineTypes == null ||
            source.buildings == null || source.recipes == null || source.buildings.Length == 0)
            throw new InvalidDataException("Factory database is missing required generated rows.");
        HashSet<ushort> items = new HashSet<ushort>();
        foreach (var item in source.items)
            Require(item.id != 0 && items.Add(item.id) && item.maxStack > 0, "Invalid or duplicate item.");
        HashSet<ushort> machines = new HashSet<ushort>();
        foreach (var machine in source.machineTypes)
            Require(machine.id != 0 && machines.Add(machine.id), "Invalid or duplicate machine type.");
        HashSet<ushort> ids = new HashSet<ushort>();
        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<byte, float> tierSpeeds = new Dictionary<byte, float>();
        HashSet<(BuildingKind, byte)> beltKinds = new HashSet<(BuildingKind, byte)>();
        foreach (var building in source.buildings)
        {
            string label = $"Building '{building.key}'";
            Require(building.id != 0 && ids.Add(building.id) && !string.IsNullOrWhiteSpace(building.key) &&
                keys.Add(building.key) && Enum.IsDefined(typeof(BuildingKind), building.kind), label + " has an invalid identity or behavior.");
            Require(building.visualPrefab != null, label + " is missing a prefab.");
            var offsets = FactoryBuildingFootprintUtility.GetOccupiedOffsets(building);
            FactoryBuildingFootprintUtility.ValidatePorts(building, offsets);
            bool processor = building.kind == BuildingKind.Processor;
            Require(processor ? machines.Contains(building.machineTypeId) : building.machineTypeId == 0,
                label + " has an invalid processor configuration.");
            bool belt = building.kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(building.kind);
            Require(belt ? building.beltTier > 0 && building.cellsPerSecond > 0f &&
                !float.IsNaN(building.cellsPerSecond) && !float.IsInfinity(building.cellsPerSecond) :
                building.beltTier == 0 && building.cellsPerSecond == 0f, label + " has invalid belt stats.");
            if (belt)
            {
                Require(beltKinds.Add((building.kind, building.beltTier)), label + " duplicates a belt behavior/tier.");
                Require(!tierSpeeds.TryGetValue(building.beltTier, out float speed) || speed == building.cellsPerSecond,
                    label + " speed differs from other belts of the same tier.");
                tierSpeeds[building.beltTier] = building.cellsPerSecond;
            }
            bool storage = building.kind == BuildingKind.Storage;
            Require(storage ? building.storageCapacity > 0 && building.storageSlotCount > 0 :
                building.storageCapacity == 0 && building.storageSlotCount == 0, label + " has invalid storage stats.");
            Require(building.kind == BuildingKind.RampFoundation ? RampUtility.IsAllowedRise(building.rampRiseHeightUnits) :
                building.rampRiseHeightUnits == 0, label + " has an invalid ramp rise.");
        }
        foreach (var building in source.buildings)
            if (VerticalBeltUtility.IsVertical(building.kind))
                Require(beltKinds.Contains((BuildingKind.VerticalBeltEntry, building.beltTier)) &&
                    beltKinds.Contains((BuildingKind.VerticalBeltExit, building.beltTier)) &&
                    beltKinds.Contains((BuildingKind.VerticalBeltSegment, building.beltTier)),
                    $"Vertical belt tier {building.beltTier} requires entry, exit and segment definitions.");
        HashSet<ushort> recipes = new HashSet<ushort>();
        foreach (var recipe in source.recipes)
        {
            Require(recipe.id != 0 && recipes.Add(recipe.id) && machines.Contains(recipe.machineTypeId) &&
                recipe.durationSeconds > 0f && !float.IsNaN(recipe.durationSeconds) && !float.IsInfinity(recipe.durationSeconds) &&
                (recipe.outputs?.Length ?? 0) > 0, $"Recipe '{recipe.key}' is invalid.");
            foreach (var ingredient in recipe.inputs ?? Array.Empty<FactoryRecipeIngredientTableRow>())
                Require(items.Contains(ingredient.itemId) && ingredient.count > 0, "Invalid recipe input.");
            foreach (var ingredient in recipe.outputs)
                Require(items.Contains(ingredient.itemId) && ingredient.count > 0, "Invalid recipe output.");
            foreach (var building in source.buildings)
            {
                if (building.kind != BuildingKind.Processor || building.machineTypeId != recipe.machineTypeId)
                    continue;
                for (int slot = 1; slot <= recipe.outputs.Length; slot++)
                {
                    bool bound = false;
                    foreach (var port in building.ports ?? Array.Empty<FactoryBuildingPortTableRow>())
                        if (port.type == BuildingPortType.Output && port.outputSlot == slot)
                            bound = true;
                    Require(bound, $"Building '{building.key}' has no outlet for recipe '{recipe.key}' output slot {slot}.");
                }
            }
        }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidDataException(message);
    }
}
