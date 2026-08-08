# Factory-with-DOTS project map

## Editor assemblies

- `Assets/Scripts/Factory.Runtime.asmdef` -> `Factory.Runtime`
- `Assets/Scripts/Data/Editor/Factory.Editor.asmdef` -> `Factory.Editor` (references `Factory.Runtime`)
- `Assets/Scripts/DotsTest/Factory.DotsTest.asmdef` -> `Factory.DotsTest`
- `Assets/Tests/Factory.Tests.asmdef` -> `Factory.Tests`

## Performance pipeline scripts

- `Assets/Scripts/Data/Editor/FactoryPerformanceBatchRunner.cs` - batch entry point; opens a performance scene then enters Play Mode
- `Assets/Scripts/ECS/Performance/FactoryPerformanceScenarioBootstrap.cs` - builds the ECS scenario at runtime; reads `-factoryPerformance*` arguments
- `Assets/Scripts/ECS/Performance/FactoryPerformanceMetricsCapture.cs` - Profiler capture; writes JSON/CSV reports; exits the Editor when done
- `Assets/Scripts/ECS/Performance/FactoryPerformanceScenarioLayout.cs` - scenario definitions and layouts

## Scenes

- `Assets/Scenes/Performance/Perf_4096_Mk4_HalfLoaded.unity`
- `Assets/Scenes/Performance/Perf_F16_Mk4_1024Items.unity`
- `Assets/Scenes/Performance/Perf_4096_Mk4_Blocking.unity`
- `Assets/Scenes/Performance/Perf_Straight_Scalable.unity`
- `Assets/Scenes/Performance/Perf_512_MixedJunction.unity`
- `Assets/Scenes/Performance/Perf_4096_Mk4_FullLoop.unity`
- `Assets/Scenes/Performance/Perf_ProducerConsumer.unity`
- `Assets/Scenes/Performance/Perf_ContinuousBeltBuild.unity`
- `Assets/Scenes/Stage3Ecs.unity` - shared base scene loaded additively by the bootstrap
- `Assets/Scenes/Stage3EntitiesSubscene.unity` - shared ECS SubScene

## Report schema (JSON)

Key fields: `scenario`, `displayName`, `unityVersion`, `batchMode`, `gridWidth/Height`, entity counts, `warmupSeconds`, `requestedSampleSeconds`, `actualSampleSeconds`, `sampledFrames`, `frameTimeMeanMilliseconds`, `frameTimeP50/P95/P99/MaxMilliseconds`, `framesPerSecond`, `fixedTickCount`, `fixedTicksPerSecond`, `acceptedTransferCount`, `acceptedTransfersPerSecond`, managed memory before/after, `peakBeltEntities`, `buildStressComplete`, `completedPlacements`, `completedRemovals`, `metrics[]` with `key`, `profilerMarker`, `category`, `unit`, `sampleCount`, `mean`, `p50`, `p95`, `p99`, `maximum`, `sum`.

The CSV next to the JSON has one row per frame: frame index, elapsed seconds, delta time ms, then one column per resolved metric.

## Verified reference values

Run on 2026-08-06, `Perf_Straight_Scalable`, 128 belts, 50% load, 1s warmup + 2s sample:

- `sampledFrames`: 2903
- mean frame: 0.705 ms, P50: 0.637 ms, P95: 1.045 ms, P99: 1.305 ms
- PlayerLoop mean: 0.600 ms
- Total Used Memory mean: about 834 MB
- fixed ticks/s: about 64
- accepted transfers/s: about 372
