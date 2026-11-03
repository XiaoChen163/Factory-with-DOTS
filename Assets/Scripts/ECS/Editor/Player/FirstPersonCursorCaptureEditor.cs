using System.Reflection;
using UnityEditor;

/// <summary>Keeps Game view's Escape override consistent with the player's requested cursor mode.</summary>
[InitializeOnLoad]
public static class FirstPersonCursorCaptureEditor
{
    // Unity 6000.3 GameView.OnGUI disables both cursor lock and hiding before forwarding Escape.
    // Its protected helper changes only the "Other" reason, preserving focus/pause/dialog restrictions.
    private static readonly MethodInfo AllowCursor = typeof(EditorWindow).Assembly
        .GetType("UnityEditor.GameView")?.GetMethod("AllowCursorLockAndHide",
            BindingFlags.Instance | BindingFlags.NonPublic);

    static FirstPersonCursorCaptureEditor()
    {
        FirstPersonPlayerController.EditorCursorCaptureRequested += ApplyCapturePermission;
    }

    private static void ApplyCapturePermission(bool capture)
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        EditorWindow window = EditorWindow.focusedWindow;
        if (window == null || window.GetType().FullName != "UnityEditor.GameView") return;
        AllowCursor?.Invoke(window, new object[] { capture });
    }
}
