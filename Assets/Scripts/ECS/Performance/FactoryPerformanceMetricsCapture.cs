using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Entities;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

public sealed class FactoryPerformanceMetricsCapture : MonoBehaviour
{
    private const string CaptureArgument = "-factoryPerformanceCapture";
    private const string OutputArgument = "-factoryPerformanceOutput";
    private const string WarmupArgument = "-factoryPerformanceWarmupSeconds";
    private const string SampleArgument = "-factoryPerformanceSampleSeconds";
    private const int MaxExpectedFramesPerSecond = 12000;
    private const string FixedStepKey = "fixed_step";
    // FixedStepSimulationSystemGroup reports a ~0.5us noise floor on frames
    // that do not run a tick, so only frames well above that floor count as
    // tick frames.
    private const double TickFrameThresholdMs = 0.01;

    private static readonly MetricTarget[] MetricTargets =
    {
        new MetricTarget("main_thread", "Main Thread"),
        new MetricTarget("player_loop", "PlayerLoop"),
        new MetricTarget("gc_allocated_in_frame", "GC Allocated In Frame"),
        new MetricTarget("total_used_memory", "Total Used Memory"),
        new MetricTarget("total_reserved_memory", "Total Reserved Memory"),
        new MetricTarget("gc_reserved_memory", "GC Reserved Memory"),
        new MetricTarget("system_used_memory", "System Used Memory"),
        new MetricTarget("fixed_step", "FixedStepSimulationSystemGroup"),
        new MetricTarget("belt_transfer", "BeltTransferSystem"),
        new MetricTarget("belt_progress", "BeltProgressSystem"),
        new MetricTarget("belt_item_position", "BeltItemPositionSystem"),
        new MetricTarget("item_process", "ItemProcessSystem"),
        new MetricTarget("item_port_adapter", "ItemPortAdapterSystem"),
        new MetricTarget("item_port_buffer_swap", "ItemPortBufferSwapSystem"),
        new MetricTarget("grid_placement_transform", "GridPlacementTransformSystem"),
        new MetricTarget("wait_for_job_group", "WaitForJobGroupID"),
        new MetricTarget("structural_changes", "Structural Changes")
    };

    private FactoryPerformanceScenarioDefinition definition;
    private string outputPath;
    private float warmupSeconds;
    private float sampleSeconds;

    public static void StartIfRequested(
        FactoryPerformanceScenarioDefinition definition)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (Array.IndexOf(arguments, CaptureArgument) < 0)
        {
            return;
        }

        GameObject captureObject = new GameObject(
            "Factory Performance Metrics Capture");
        DontDestroyOnLoad(captureObject);
        FactoryPerformanceMetricsCapture capture =
            captureObject.AddComponent<FactoryPerformanceMetricsCapture>();
        capture.definition = definition;
        capture.outputPath = ReadArgument(
            arguments,
            OutputArgument,
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "PerformanceReports",
                definition.Scenario + ".json"));
        capture.warmupSeconds = ReadFloatArgument(
            arguments,
            WarmupArgument,
            5f);
        capture.sampleSeconds = ReadFloatArgument(
            arguments,
            SampleArgument,
            10f);
        capture.StartCoroutine(capture.Capture());
    }

    private IEnumerator Capture()
    {
        Debug.Log(
            "[ECS Performance] Profiler warmup for " +
            warmupSeconds.ToString("F1", CultureInfo.InvariantCulture) +
            " seconds.");
        float warmupDeadline = Time.realtimeSinceStartup + warmupSeconds;
        while (Time.realtimeSinceStartup < warmupDeadline)
        {
            yield return null;
        }

        GC.Collect();
        yield return null;

        List<MetricRecorder> recorders = CreateMetricRecorders();
        World world = World.DefaultGameObjectInjectionWorld;
        Stage3SimulationStats initialStats = ReadStats(world);
        long managedMemoryBefore = GC.GetTotalMemory(false);
        int frameCapacity = Math.Max(
            1024,
            (int)Math.Ceiling(sampleSeconds * MaxExpectedFramesPerSecond));
        FrameSample[] frames = new FrameSample[frameCapacity];
        for (int i = 0; i < frameCapacity; i++)
        {
            frames[i] = new FrameSample
            {
                values = new double[recorders.Count]
            };
        }

        int frameCount = 0;
        bool skipFirstFrame = true;

        Debug.Log(
            "[ECS Performance] Sampling for " +
            sampleSeconds.ToString("F1", CultureInfo.InvariantCulture) +
            " seconds with " + recorders.Count +
            " resolved Profiler counters.");

        float captureStart = Time.realtimeSinceStartup;
        float captureDeadline = captureStart + sampleSeconds;
        while (Time.realtimeSinceStartup < captureDeadline)
        {
            yield return null;

            // The frame immediately after GC.Collect() carries a one-time
            // managed allocation spike from profiler counter setup; exclude
            // it so the sample only reflects steady-state frames.
            if (skipFirstFrame)
            {
                skipFirstFrame = false;
                continue;
            }

            if (frameCount >= frameCapacity)
            {
                Debug.LogWarning(
                    "[ECS Performance] Frame buffer capacity reached; " +
                    "ending sample early.");
                break;
            }

            FrameSample frame = frames[frameCount];
            frame.elapsedSeconds = Time.realtimeSinceStartup - captureStart;
            frame.deltaTimeMilliseconds = Time.unscaledDeltaTime * 1000.0;
            for (int i = 0; i < recorders.Count; i++)
            {
                frame.values[i] = recorders[i].ReadDisplayValue();
            }

            frameCount++;
        }

        float actualSampleSeconds = Time.realtimeSinceStartup - captureStart;
        Stage3SimulationStats finalStats = ReadStats(world);
        EntityCounts entityCounts = ReadEntityCounts(world);
        long managedMemoryAfter = GC.GetTotalMemory(false);

        FactoryPerformanceCaptureReport report = BuildReport(
            recorders,
            frames,
            frameCount,
            initialStats,
            finalStats,
            entityCounts,
            managedMemoryBefore,
            managedMemoryAfter,
            actualSampleSeconds);

        WriteResults(report, recorders, frames, frameCount);
        for (int i = 0; i < recorders.Count; i++)
        {
            recorders[i].Dispose();
        }

        Debug.Log(
            "[ECS Performance] CAPTURE COMPLETE: " + outputPath +
            ", mean frame=" + report.frameTimeMeanMilliseconds.ToString(
                "F3",
                CultureInfo.InvariantCulture) +
            " ms, p95=" + report.frameTimeP95Milliseconds.ToString(
                "F3",
                CultureInfo.InvariantCulture) + " ms.");
        ExitBatchMode(0);
    }

    private FactoryPerformanceCaptureReport BuildReport(
        List<MetricRecorder> recorders,
        FrameSample[] frames,
        int frameCount,
        Stage3SimulationStats initialStats,
        Stage3SimulationStats finalStats,
        EntityCounts entityCounts,
        long managedMemoryBefore,
        long managedMemoryAfter,
        float actualSampleSeconds)
    {
        double[] frameTimes = frames
            .Take(frameCount)
            .Select(frame => frame.deltaTimeMilliseconds)
            .ToArray();
        ulong tickDelta = finalStats.TickCount - initialStats.TickCount;
        ulong readyDelta = finalStats.TotalReadyRequestCount -
                           initialStats.TotalReadyRequestCount;
        ulong acceptedDelta = finalStats.TotalAcceptedTransferCount -
                              initialStats.TotalAcceptedTransferCount;

        FactoryPerformanceCaptureReport report =
            new FactoryPerformanceCaptureReport
            {
                scenario = definition.Scenario.ToString(),
                displayName = definition.DisplayName,
                description = definition.Description,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemoryMegabytes = SystemInfo.systemMemorySize,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                batchMode = Application.isBatchMode,
                gridWidth = definition.GridSize.x,
                gridHeight = definition.GridSize.y,
                expectedBelts = definition.BeltCount,
                expectedMergers = definition.MergerCount,
                expectedSplitters = definition.SplitterCount,
                expectedProcessors = definition.ProcessorCount,
                initialItems = definition.InitialItemCells.Length,
                beltEntities = entityCounts.belts,
                mergerEntities = entityCounts.mergers,
                splitterEntities = entityCounts.splitters,
                processorEntities = entityCounts.processors,
                storageEntities = entityCounts.storages,
                itemEntities = entityCounts.items,
                warmupSeconds = warmupSeconds,
                requestedSampleSeconds = sampleSeconds,
                actualSampleSeconds = actualSampleSeconds,
                sampledFrames = frameCount,
                frameTimeMeanMilliseconds = Mean(frameTimes),
                frameTimeP50Milliseconds = Percentile(frameTimes, 0.50),
                frameTimeP95Milliseconds = Percentile(frameTimes, 0.95),
                frameTimeP99Milliseconds = Percentile(frameTimes, 0.99),
                frameTimeMaxMilliseconds = Maximum(frameTimes),
                framesPerSecond = actualSampleSeconds > 0f
                    ? frameCount / actualSampleSeconds
                    : 0.0,
                fixedTickCount = tickDelta,
                fixedTicksPerSecond = actualSampleSeconds > 0f
                    ? tickDelta / actualSampleSeconds
                    : 0.0,
                readyRequestCount = readyDelta,
                acceptedTransferCount = acceptedDelta,
                acceptedTransfersPerSecond = actualSampleSeconds > 0f
                    ? acceptedDelta / actualSampleSeconds
                    : 0.0,
                acceptedTransfersPerTick = tickDelta > 0
                    ? acceptedDelta / (double)tickDelta
                    : 0.0,
                managedMemoryBeforeBytes = managedMemoryBefore,
                managedMemoryAfterBytes = managedMemoryAfter,
                managedMemoryDeltaBytes = managedMemoryAfter -
                                          managedMemoryBefore,
                metrics = new List<ProfilerMetricSummary>(),
                unresolvedMetrics = MetricTargets
                    .Where(target => recorders.All(
                        recorder => recorder.Key != target.key))
                    .Select(target => target.searchName)
                    .ToList()
            };

        int fixedStepIndex = -1;
        for (int i = 0; i < recorders.Count; i++)
        {
            if (recorders[i].Key == FixedStepKey)
            {
                fixedStepIndex = i;
                break;
            }
        }

        bool[] isTickFrame = new bool[frameCount];
        int tickFrameCount = 0;
        for (int i = 0; i < frameCount; i++)
        {
            bool isTick = fixedStepIndex >= 0 &&
                          frames[i].values[fixedStepIndex] >
                          TickFrameThresholdMs;
            isTickFrame[i] = isTick;
            if (isTick)
            {
                tickFrameCount++;
            }
        }

        for (int metricIndex = 0;
             metricIndex < recorders.Count;
             metricIndex++)
        {
            double[] values = frames
                .Take(frameCount)
                .Select(frame => frame.values[metricIndex])
                .ToArray();
            MetricRecorder recorder = recorders[metricIndex];
            double tickSum = 0.0;
            List<double> nonTickValues = new List<double>();
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                if (isTickFrame[frameIndex])
                {
                    tickSum += frames[frameIndex].values[metricIndex];
                }
                else
                {
                    nonTickValues.Add(frames[frameIndex].values[metricIndex]);
                }
            }

            double frameBaseline = nonTickValues.Count > 0
                ? Percentile(nonTickValues.ToArray(), 0.50)
                : 0.0;

            report.metrics.Add(new ProfilerMetricSummary
            {
                key = recorder.Key,
                profilerMarker = recorder.MarkerName,
                category = recorder.CategoryName,
                unit = recorder.DisplayUnit,
                sampleCount = values.Length,
                nonZeroSampleCount = values.Count(value => value != 0.0),
                mean = Mean(values),
                p50 = Percentile(values, 0.50),
                p95 = Percentile(values, 0.95),
                p99 = Percentile(values, 0.99),
                maximum = Maximum(values),
                sum = values.Sum(),
                tickFrames = tickFrameCount,
                perTick = tickDelta > 0
                    ? tickSum / (double)tickDelta
                    : 0.0,
                frameBaseline = frameBaseline,
                perTickNet = tickDelta > 0
                    ? (tickSum - tickFrameCount * frameBaseline) /
                      (double)tickDelta
                    : 0.0
            });
        }

        return report;
    }

    private void WriteResults(
        FactoryPerformanceCaptureReport report,
        List<MetricRecorder> recorders,
        FrameSample[] frames,
        int frameCount)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            fullOutputPath,
            JsonUtility.ToJson(report, true),
            Encoding.UTF8);

        string csvPath = Path.ChangeExtension(fullOutputPath, ".csv");
        StringBuilder csv = new StringBuilder();
        csv.Append("frame,elapsed_seconds,delta_time_ms");
        for (int i = 0; i < recorders.Count; i++)
        {
            csv.Append(',');
            csv.Append(recorders[i].Key);
            csv.Append('_');
            csv.Append(recorders[i].DisplayUnit);
        }
        csv.AppendLine();

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            FrameSample frame = frames[frameIndex];
            csv.Append(frameIndex);
            csv.Append(',');
            csv.Append(frame.elapsedSeconds.ToString(
                "F6",
                CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(frame.deltaTimeMilliseconds.ToString(
                "F6",
                CultureInfo.InvariantCulture));
            for (int metricIndex = 0;
                 metricIndex < frame.values.Length;
                 metricIndex++)
            {
                csv.Append(',');
                csv.Append(frame.values[metricIndex].ToString(
                    "F6",
                    CultureInfo.InvariantCulture));
            }
            csv.AppendLine();
        }

        File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
    }

    private static List<MetricRecorder> CreateMetricRecorders()
    {
        List<ProfilerRecorderHandle> handles =
            new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        List<ProfilerRecorderDescription> descriptions = handles
            .Where(handle => handle.Valid)
            .Select(ProfilerRecorderHandle.GetDescription)
            .Where(description => !string.IsNullOrEmpty(description.Name))
            .ToList();

        List<MetricRecorder> result = new List<MetricRecorder>();
        for (int targetIndex = 0;
             targetIndex < MetricTargets.Length;
             targetIndex++)
        {
            MetricTarget target = MetricTargets[targetIndex];
            ProfilerRecorderDescription? resolved = descriptions
                .Where(description => string.Equals(
                    description.Name,
                    target.searchName,
                    StringComparison.OrdinalIgnoreCase))
                .Cast<ProfilerRecorderDescription?>()
                .FirstOrDefault();

            if (!resolved.HasValue)
            {
                resolved = descriptions
                    .Where(description => description.Name.IndexOf(
                        target.searchName,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(description => description.Name.Length)
                    .Cast<ProfilerRecorderDescription?>()
                    .FirstOrDefault();
            }

            if (!resolved.HasValue)
            {
                continue;
            }

            ProfilerRecorderDescription description = resolved.Value;
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                description.Category,
                description.Name,
                1,
                ProfilerRecorderOptions.StartImmediately |
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                ProfilerRecorderOptions.SumAllSamplesInFrame);
            if (!recorder.Valid)
            {
                recorder.Dispose();
                continue;
            }

            result.Add(new MetricRecorder(
                target.key,
                description,
                recorder));
        }

        return result;
    }

    private static Stage3SimulationStats ReadStats(World world)
    {
        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Stage3SimulationStats>());
        Stage3SimulationStats stats = query.CalculateEntityCount() == 1
            ? query.GetSingleton<Stage3SimulationStats>()
            : default;
        query.Dispose();
        return stats;
    }

    private static EntityCounts ReadEntityCounts(World world)
    {
        EntityManager entityManager = world.EntityManager;
        EntityQuery belts = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BeltState>());
        EntityQuery mergers = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Merger>());
        EntityQuery splitters = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Splitter>());
        EntityQuery processors = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemProcessor>());
        EntityQuery storages = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageState>());
        EntityQuery items = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Item>());
        EntityCounts counts = new EntityCounts
        {
            belts = belts.CalculateEntityCount(),
            mergers = mergers.CalculateEntityCount(),
            splitters = splitters.CalculateEntityCount(),
            processors = processors.CalculateEntityCount(),
            storages = storages.CalculateEntityCount(),
            items = items.CalculateEntityCount()
        };
        belts.Dispose();
        mergers.Dispose();
        splitters.Dispose();
        processors.Dispose();
        storages.Dispose();
        items.Dispose();
        return counts;
    }

    private static string ReadArgument(
        string[] arguments,
        string name,
        string fallback)
    {
        int index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1]
            : fallback;
    }

    private static float ReadFloatArgument(
        string[] arguments,
        string name,
        float fallback)
    {
        string raw = ReadArgument(arguments, name, null);
        return float.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float parsed) && parsed >= 0f
            ? parsed
            : fallback;
    }

    private static double Mean(double[] values)
    {
        return values.Length == 0 ? 0.0 : values.Average();
    }

    private static double Maximum(double[] values)
    {
        return values.Length == 0 ? 0.0 : values.Max();
    }

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        double index = (sorted.Length - 1) * percentile;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sorted[lower];
        }

        double fraction = index - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static void ExitBatchMode(int exitCode)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(exitCode);
#else
        Application.Quit(exitCode);
#endif
    }

    private readonly struct MetricTarget
    {
        public MetricTarget(string key, string searchName)
        {
            this.key = key;
            this.searchName = searchName;
        }

        public readonly string key;
        public readonly string searchName;
    }

    private sealed class MetricRecorder : IDisposable
    {
        private ProfilerRecorder recorder;
        private readonly ProfilerMarkerDataUnit unit;

        public MetricRecorder(
            string key,
            ProfilerRecorderDescription description,
            ProfilerRecorder recorder)
        {
            Key = key;
            MarkerName = description.Name;
            CategoryName = description.Category.Name;
            unit = description.UnitType;
            DisplayUnit = unit == ProfilerMarkerDataUnit.TimeNanoseconds
                ? "ms"
                : unit == ProfilerMarkerDataUnit.Bytes
                    ? "bytes"
                    : unit.ToString().ToLowerInvariant();
            this.recorder = recorder;
        }

        public string Key { get; }
        public string MarkerName { get; }
        public string CategoryName { get; }
        public string DisplayUnit { get; }

        public double ReadDisplayValue()
        {
            double value = recorder.LastValueAsDouble;
            return unit == ProfilerMarkerDataUnit.TimeNanoseconds
                ? value / 1000000.0
                : value;
        }

        public void Dispose()
        {
            recorder.Dispose();
        }
    }

    private sealed class FrameSample
    {
        public double elapsedSeconds;
        public double deltaTimeMilliseconds;
        public double[] values;
    }

    private struct EntityCounts
    {
        public int belts;
        public int mergers;
        public int splitters;
        public int processors;
        public int storages;
        public int items;
    }
}

[Serializable]
public sealed class FactoryPerformanceCaptureReport
{
    public string scenario;
    public string displayName;
    public string description;
    public string timestampUtc;
    public string unityVersion;
    public string platform;
    public string operatingSystem;
    public string processor;
    public int processorCount;
    public int systemMemoryMegabytes;
    public string graphicsDevice;
    public bool batchMode;
    public int gridWidth;
    public int gridHeight;
    public int expectedBelts;
    public int expectedMergers;
    public int expectedSplitters;
    public int expectedProcessors;
    public int initialItems;
    public int beltEntities;
    public int mergerEntities;
    public int splitterEntities;
    public int processorEntities;
    public int storageEntities;
    public int itemEntities;
    public float warmupSeconds;
    public float requestedSampleSeconds;
    public float actualSampleSeconds;
    public int sampledFrames;
    public double frameTimeMeanMilliseconds;
    public double frameTimeP50Milliseconds;
    public double frameTimeP95Milliseconds;
    public double frameTimeP99Milliseconds;
    public double frameTimeMaxMilliseconds;
    public double framesPerSecond;
    public ulong fixedTickCount;
    public double fixedTicksPerSecond;
    public ulong readyRequestCount;
    public ulong acceptedTransferCount;
    public double acceptedTransfersPerSecond;
    public double acceptedTransfersPerTick;
    public long managedMemoryBeforeBytes;
    public long managedMemoryAfterBytes;
    public long managedMemoryDeltaBytes;
    public List<ProfilerMetricSummary> metrics;
    public List<string> unresolvedMetrics;
}

[Serializable]
public sealed class ProfilerMetricSummary
{
    public string key;
    public string profilerMarker;
    public string category;
    public string unit;
    public int sampleCount;
    public int nonZeroSampleCount;
    public double mean;
    public double p50;
    public double p95;
    public double p99;
    public double maximum;
    public double sum;
    public int tickFrames;
    public double perTick;
    public double frameBaseline;
    public double perTickNet;
}
