using System;
using UnityEngine;

[DefaultExecutionOrder(-900)]
public sealed class GameManager : MonoBehaviour
{
    [SerializeField, Min(0.001f)] private float tickInterval = 1f / 60f;

    private float accumulator;

    public static GameManager Instance { get; private set; }
    public float TickInterval => tickInterval;
    public float InterpolationAlpha => tickInterval <= 0f ? 1f : Mathf.Clamp01(accumulator / tickInterval);
    public ulong LogicTickCount { get; private set; }
    public event Action<float> LogicTick;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one GameManager can drive the fixed-tick simulation.");
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        accumulator += Time.deltaTime;
        while (accumulator >= tickInterval)
        {
            accumulator -= tickInterval;
            LogicTickCount++;
            LogicTick?.Invoke(tickInterval);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        tickInterval = Mathf.Max(0.001f, tickInterval);
    }
}
