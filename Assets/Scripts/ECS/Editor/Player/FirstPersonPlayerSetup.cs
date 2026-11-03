using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Rebuilds the retained Starter Assets robot and installs it in the ECS scene.</summary>
public static class FirstPersonPlayerSetup
{
    private const string Robot = "Assets/Art/Characters/Robot/";
    private const string ControllerPath = Robot + "Animations/RobotLocomotion.controller";
    private const string PrefabPath = "Assets/Prefabs/Player/FirstPersonRobot.prefab";

    public static void UpgradeToEcsCharacterController()
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var legacy in prefab.GetComponentsInChildren<UnityEngine.CharacterController>(true))
                Object.DestroyImmediate(legacy);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Ecs.unity", OpenSceneMode.Single);
        foreach (var player in Object.FindObjectsByType<FirstPersonPlayerController>(FindObjectsSortMode.None))
            foreach (var legacy in player.GetComponents<UnityEngine.CharacterController>()) Object.DestroyImmediate(legacy);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ECS Character] Removed all legacy player CharacterControllers; prefab and scene migrated.");
    }

    public static void RemoveLegacyFloor()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Ecs.unity", OpenSceneMode.Single);
        GameObject floor = GameObject.Find("Factory Floor");
        if (floor != null) Object.DestroyImmediate(floor);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[First Person] Removed legacy floor; spawn foundations are created at runtime.");
    }

    public static void ValidateAndPreview()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Ecs.unity", OpenSceneMode.Single);
        FirstPersonPlayerController player = Object.FindFirstObjectByType<FirstPersonPlayerController>();
        if (player == null || player.ViewCamera == null) throw new MissingReferenceException("First-person scene setup is missing.");
        Animator animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new System.InvalidOperationException("Robot must have a valid humanoid avatar.");
        foreach (string dependency in AssetDatabase.GetDependencies(PrefabPath, true))
            if (dependency.StartsWith("Assets/Starter Assets/"))
                throw new System.InvalidOperationException("Prefab still depends on Starter Assets: " + dependency);
        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (renderer.sharedMaterials.Length != renderer.sharedMesh.subMeshCount || renderer.sharedMaterials.Any(material => material == null))
                throw new System.InvalidOperationException("Robot submesh materials are incomplete.");
        LoadClip("Stand--Idle.anim.fbx").SampleAnimation(animator.gameObject, 0f);
        GameObject previewObject = new GameObject("Robot Preview Camera");
        Camera previewCamera = previewObject.AddComponent<Camera>();
        previewCamera.transform.position = player.transform.position + new Vector3(2.4f, 1.7f, 2.4f);
        previewCamera.transform.LookAt(player.transform.position + Vector3.up * 0.95f);
        previewCamera.fieldOfView = 35f;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.17f, 0.2f, 0.25f);
        RenderTexture target = new RenderTexture(768, 768, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D pixels = new Texture2D(768, 768, TextureFormat.RGB24, false);
        try
        {
            previewCamera.targetTexture = target;
            previewCamera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
            pixels.Apply();
            System.IO.Directory.CreateDirectory("TestResults");
            System.IO.File.WriteAllBytes("TestResults/FirstPersonRobot.png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            previewCamera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(previewObject);
        }
        Debug.Log("[First Person] Asset validation passed; robot preview saved.");
    }

    [MenuItem("Factory/Player/Install First Person Robot")]
    public static void Install()
    {
        AssetDatabase.Refresh();
        EnsureFolder("Assets/Prefabs/Player");
        AnimatorController animation = CreateAnimator();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Ecs.unity", OpenSceneMode.Single);
        FirstPersonPlayerController previous = Object.FindFirstObjectByType<FirstPersonPlayerController>();
        Camera camera = Camera.main;
        if (camera == null) throw new MissingReferenceException("ECS scene must contain a Main Camera.");
        if (previous != null && PrefabUtility.IsPartOfPrefabInstance(previous.gameObject))
            PrefabUtility.UnpackPrefabInstance(previous.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        // Preserve the camera's URP configuration and the existing building interaction component.
        camera.transform.SetParent(null);
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(camera.gameObject);
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        GameObject root = new GameObject("First Person Robot");
        root.transform.position = new Vector3(16f, 0.1f, -4f);
        root.AddComponent<EcsCharacterMotor>();
        FirstPersonPlayerController player = root.AddComponent<FirstPersonPlayerController>();

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Robot + "Models/Armature.fbx");
        if (source == null) throw new MissingReferenceException("Retained Starter Assets robot is missing.");
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
        model.name = "Robot Visual";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        Animator animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = animation;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        int playerLayer = EnsurePlayerLayer();
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = playerLayer;
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            string[] parts = { "Body", "Arms", "Legs" };
            // The imported robot has a single skinned mesh with these three submeshes.
            renderer.sharedMaterials = parts.Select(part => AssetDatabase.LoadAssetAtPath<Material>(
                Robot + "Materials/M_Armature_" + part + ".mat")).ToArray();
        }
        camera.transform.SetParent(root.transform, false);
        camera.transform.localPosition = new Vector3(0f, 1.62f, 0.05f);
        camera.transform.localRotation = Quaternion.identity;
        camera.fieldOfView = 75f;
        camera.nearClipPlane = 0.04f;
        // The full robot remains visible in Scene view and casts shadows, without clipping its own camera.
        camera.cullingMask &= ~(1 << playerLayer);
        SerializedObject settings = new SerializedObject(player);
        settings.FindProperty("viewCamera").objectReferenceValue = camera;
        settings.FindProperty("animator").objectReferenceValue = animator;
        settings.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);

        GameObject floor = GameObject.Find("Factory Floor");
        if (floor != null) Object.DestroyImmediate(floor);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[First Person] Robot prefab, speed blend tree and ECS scene installed.");
    }

    private static AnimatorController CreateAnimator()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null) return controller;
        controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        AnimatorState state = controller.layers[0].stateMachine.AddState("Locomotion");
        controller.layers[0].stateMachine.defaultState = state;
        BlendTree tree = new BlendTree { name = "Idle Walk Run", blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(LoadClip("Stand--Idle.anim.fbx"), 0f);
        tree.AddChild(LoadClip("Locomotion--Walk_N.anim.fbx"), 4f);
        tree.AddChild(LoadClip("Locomotion--Run_N.anim.fbx"), 7f);
        state.motion = tree;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip LoadClip(string file) =>
        AssetDatabase.LoadAllAssetsAtPath(Robot + "Animations/" + file)
            .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__preview__"));

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static int EnsurePlayerLayer()
    {
        int existing = LayerMask.NameToLayer("Player");
        if (existing >= 0) return existing;
        SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
            layers.GetArrayElementAtIndex(i).stringValue = "Player";
            tags.ApplyModifiedPropertiesWithoutUndo();
            return i;
        }
        throw new System.InvalidOperationException("No free layer for the first-person robot.");
    }
}
