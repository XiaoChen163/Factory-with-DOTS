using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public sealed class FactoryBuildStressDriver : MonoBehaviour
{
    private const float InteractionTimeoutSeconds = 30f;

    private FactoryPerformanceScenarioDefinition definition;
    private EcsGridInteractionController interaction;
    private Coroutine runCoroutine;
    private bool begun;

    public bool IsFinished { get; private set; }
    public bool HasFailed { get; private set; }
    public string Status { get; private set; } = "Waiting for build input";
    public int CompletedPlacements { get; private set; }
    public int CompletedRemovals { get; private set; }

    public float EstimatedDurationSeconds =>
        definition == null
            ? 0f
            : definition.Scale *
              (definition.DragSeconds + definition.TimeDelaySeconds) +
              definition.Scale * definition.TimeDelaySeconds + 10f;

    public static FactoryBuildStressDriver Create(
        FactoryPerformanceScenarioDefinition definition)
    {
        GameObject driverObject = new GameObject(
            "Factory Build Stress Driver");
        DontDestroyOnLoad(driverObject);
        FactoryBuildStressDriver driver =
            driverObject.AddComponent<FactoryBuildStressDriver>();
        driver.definition = definition;
        return driver;
    }

    public void Begin()
    {
        if (begun)
        {
            return;
        }

        begun = true;
        runCoroutine = StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return WaitForInteraction();
        if (interaction == null)
        {
            Fail("The ECS grid interaction controller is unavailable.");
            yield break;
        }

        if (!interaction.TrySelectBuildingLevel(
                new BuildingLevelId
                {
                    Value =
                        FactoryPerformanceScenarioLayout.Mk4BeltLevelId
                }))
        {
            Fail("Mk4 belt is not available in the building menu.");
            yield break;
        }

        Status = "Placing " + definition.Scale + " belt lines...";
        for (int beltIndex = 0;
             beltIndex < definition.Scale;
             beltIndex++)
        {
            int row = beltIndex * 2;
            int2 start = new int2(0, row);
            int2 end = new int2(definition.Scale - 1, row);
            yield return RunPlacement(start, end);
            CompletedPlacements++;
            yield return WaitForDelay(definition.TimeDelaySeconds);
        }

        Status = "Removing " + definition.Scale + " belt lines...";
        for (int removalIndex = 0;
             removalIndex < definition.Scale;
             removalIndex++)
        {
            int beltIndex = definition.DemolitionOrder == 0
                ? removalIndex
                : definition.Scale - 1 - removalIndex;
            int row = beltIndex * 2;
            int2 startCell = new int2(0, row);
            interaction.SimulateHover(startCell);
            yield return null;
            interaction.SimulateRemove(startCell, true);
            CompletedRemovals++;
            yield return WaitForDelay(definition.TimeDelaySeconds);
        }

        interaction.StopSimulatedHover();
        IsFinished = true;
        Status = "Finished";
        Debug.Log(
            "[ECS Performance] Build stress driver finished: " +
            CompletedPlacements + " placed, " + CompletedRemovals +
            " removed.");
    }

    private IEnumerator WaitForInteraction()
    {
        float deadline =
            Time.realtimeSinceStartup + InteractionTimeoutSeconds;
        while (interaction == null &&
               Time.realtimeSinceStartup < deadline)
        {
            EcsGridInteractionController[] found =
                FindObjectsByType<EcsGridInteractionController>(
                    FindObjectsSortMode.None);
            interaction = found.Length > 0 ? found[0] : null;
            yield return null;
        }
    }

    private IEnumerator RunPlacement(int2 start, int2 end)
    {
        interaction.CancelBeltPath();
        interaction.SimulateHover(start);
        yield return null;
        interaction.SimulatePrimaryClick(start);

        List<int2> path = BuildPath(start, end);
        float dragStart = Time.realtimeSinceStartup;
        while (definition.DragSeconds > 0f &&
               Time.realtimeSinceStartup - dragStart <
               definition.DragSeconds)
        {
            float progress = math.clamp(
                (Time.realtimeSinceStartup - dragStart) /
                definition.DragSeconds,
                0f,
                1f);
            int pathIndex = math.clamp(
                (int)(progress * path.Count),
                0,
                path.Count - 1);
            interaction.SimulateHover(path[pathIndex]);
            yield return null;
        }

        interaction.SimulateHover(end);
        yield return null;
        interaction.SimulatePrimaryClick(end);
    }

    private static List<int2> BuildPath(int2 start, int2 end)
    {
        int length = math.abs(end.x - start.x) +
                     math.abs(end.y - start.y);
        List<int2> path = new List<int2>(length + 1);
        int2 step = new int2(
            end.x == start.x ? 0 : end.x > start.x ? 1 : -1,
            end.y == start.y ? 0 : end.y > start.y ? 1 : -1);
        for (int i = 0; i <= length; i++)
        {
            path.Add(start + step * i);
        }

        return path;
    }

    private IEnumerator WaitForDelay(float seconds)
    {
        if (seconds <= 0f)
        {
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private void Fail(string reason)
    {
        HasFailed = true;
        Status = "FAILED - " + reason;
        Debug.LogError("[ECS Performance] " + Status);
    }
}
