using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Collects first-person input and presents the official ECS character, including creative flight.</summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(EcsCharacterMotor))]
public sealed class FirstPersonPlayerController : MonoBehaviour
{
#if UNITY_EDITOR
    public static event System.Action<bool> EditorCursorCaptureRequested;
#endif
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField, Min(0.1f)] private float sprintSpeed = 7f;
    [SerializeField, Min(0.1f)] private float flightSpeed = 12f;
    [SerializeField, Min(1f)] private float flightSprintMultiplier = 1.75f;
    [SerializeField, Min(0.01f)] private float lookSensitivity = 0.12f;
    [SerializeField, Min(0.1f)] private float acceleration = 20f;
    [SerializeField, Min(0.1f)] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;
    [SerializeField, Range(0.1f, 0.5f)] private float doubleTapWindow = 0.3f;

    private EcsCharacterMotor motor;
    private PlayerInputModeController inputMode;
    private InputActionMap locomotion;
    private InputAction move, look, jump, descend, sprint;
    private float lastSpacePress = float.NegativeInfinity;
    private float pitch;
    private uint lastLandingVersion;
    private bool focused = true;
    private bool wasControlling;
    private PlayerSpawnFoundation spawnFoundation;
    private static readonly int Speed = Animator.StringToHash("Speed");

    public bool IsFlying => motor != null && motor.IsFlying;
    public float VerticalVelocity => motor != null ? motor.Velocity.y : 0f;
    public Camera ViewCamera => viewCamera;
    public bool IsSpawnFoundationReady => spawnFoundation?.IsReady == true;
    public bool IsControlling => focused &&
        (inputMode == null || !inputMode.RequiresPointer);

    private void Awake()
    {
        motor = GetComponent<EcsCharacterMotor>();
        spawnFoundation = new PlayerSpawnFoundation(transform.position);
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        inputMode = FindFirstObjectByType<PlayerInputModeController>();
        locomotion = new InputActionMap("FirstPerson");
        move = locomotion.AddAction("Move", InputActionType.Value);
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        look = locomotion.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
        jump = locomotion.AddAction("JumpAscend", InputActionType.Button, "<Keyboard>/space");
        descend = locomotion.AddAction("Descend", InputActionType.Button);
        descend.AddBinding("<Keyboard>/leftShift");
        descend.AddBinding("<Keyboard>/rightShift");
        sprint = locomotion.AddAction("Sprint", InputActionType.Button);
        sprint.AddBinding("<Keyboard>/leftCtrl");
        sprint.AddBinding("<Keyboard>/rightCtrl");
    }

    private void OnEnable() => locomotion?.Enable();

    private void OnDisable()
    {
        locomotion?.Disable();
        lastSpacePress = float.NegativeInfinity;
        motor?.Suspend();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDestroy()
    {
        locomotion?.Dispose();
        spawnFoundation?.Dispose();
    }

    private void OnApplicationFocus(bool value)
    {
        focused = value;
        lastSpacePress = float.NegativeInfinity;
        UpdateCursor();
    }

    private void Update()
    {
        bool controlling = IsControlling;
        UpdateCursor();
        if (!spawnFoundation.TryInitialize() || !motor.EnsureCreated(new FactoryCharacterSettings
        {
            WalkSpeed = moveSpeed, RunSpeed = sprintSpeed, FlySpeed = flightSpeed,
            FlySprintMultiplier = flightSprintMultiplier, Acceleration = acceleration,
            Gravity = gravity, JumpSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity)
        }))
        {
            lastSpacePress = float.NegativeInfinity;
            return;
        }
        motor.SynchronizePresentation();
        if (motor.LandingVersion != lastLandingVersion)
        {
            lastSpacePress = float.NegativeInfinity;
            lastLandingVersion = motor.LandingVersion;
        }
        if (!controlling || !wasControlling) lastSpacePress = float.NegativeInfinity;
        if (controlling && wasControlling && viewCamera != null)
        {
            Vector2 delta = look.ReadValue<Vector2>() * lookSensitivity;
            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
        wasControlling = controlling;
        bool requestJump = false, requestFlightToggle = false;
        if (controlling && jump.WasPressedThisFrame())
        {
            float now = Time.unscaledTime;
            if (now - lastSpacePress <= doubleTapWindow)
            {
                requestFlightToggle = true;
                lastSpacePress = float.NegativeInfinity;
            }
            else
            {
                requestJump = true;
                lastSpacePress = now;
            }
        }
        Vector2 input = controlling ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        motor.SubmitControl(new FactoryCharacterControl
        {
            Active = true, Controlling = controlling,
            MoveVector = transform.right * input.x + transform.forward * input.y,
            Rotation = transform.rotation,
            Sprint = controlling && sprint.IsPressed(),
            Ascend = controlling && jump.IsPressed(), Descend = controlling && descend.IsPressed(),
            JumpRequested = requestJump, ToggleFlightRequested = requestFlightToggle
        });
    }

    private void LateUpdate()
    {
        motor.SynchronizePresentation();
        if (animator != null)
        {
            Vector3 velocity = motor.Velocity;
            velocity.y = 0f;
            animator.SetFloat(Speed, velocity.magnitude, 0.1f, Time.deltaTime);
        }
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        bool capture = enabled && IsControlling;
#if UNITY_EDITOR
        EditorCursorCaptureRequested?.Invoke(capture);
#endif
        Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !capture;
    }

    private void OnGUI()
    {
        // Reapply the requested state after the editor has handled its own Escape event.
        UpdateCursor();
        if (!IsControlling || viewCamera == null) return;
        Rect viewport = viewCamera.pixelRect;
        float x = viewport.center.x;
        float y = Screen.height - viewport.center.y;
        GUI.DrawTexture(new Rect(x - 5f, y - 1f, 10f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1f, y - 5f, 2f, 10f), Texture2D.whiteTexture);
    }
}
