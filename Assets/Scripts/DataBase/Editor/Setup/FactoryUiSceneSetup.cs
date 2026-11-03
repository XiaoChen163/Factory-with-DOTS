using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class FactoryUiSceneSetup
{
    private const string ScenePath = "Assets/Scenes/Ecs.unity";
    private const string RootAssetPath = "Assets/UI/Runtime/GameUiRoot.uxml";
    private const string PanelSettingsPath = "Assets/Art/UI/PanelSettings.asset";
    private const string ThemePath = "Assets/UI/Runtime/FactoryRuntimeTheme.tss";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string DatabasePath = FactoryDatabaseCsvImporter.DatabaseAssetPath;
    private const string PresentationCatalogPath =
        "Assets/Data/Generated/FactoryPresentationCatalog.asset";

    [MenuItem("Factory/UI/Install Phase 2 Root In ECS Scene")]
    public static void InstallInEcsScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject root = GameObject.Find("GameUiRoot");
        if (root == null)
            root = new GameObject("GameUiRoot");

        UIDocument document = GetOrAdd<UIDocument>(root);
        GameUiController controller = GetOrAdd<GameUiController>(root);
        PlayerInputModeController inputMode =
            GetOrAdd<PlayerInputModeController>(root);

        VisualTreeAsset tree =
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RootAssetPath);
        PanelSettings panel =
            AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        Object actions = AssetDatabase.LoadMainAssetAtPath(InputActionsPath);
        Object theme = AssetDatabase.LoadMainAssetAtPath(ThemePath);
        FactoryDatabaseAsset database =
            AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(DatabasePath);
        FactoryPresentationCatalog presentationCatalog =
            AssetDatabase.LoadAssetAtPath<FactoryPresentationCatalog>(
                PresentationCatalogPath);
        if (presentationCatalog == null)
        {
            presentationCatalog = ScriptableObject.CreateInstance<FactoryPresentationCatalog>();
            AssetDatabase.CreateAsset(presentationCatalog, PresentationCatalogPath);
        }
        SerializedObject catalogObject = new(presentationCatalog);
        catalogObject.FindProperty("database").objectReferenceValue = database;
        catalogObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(presentationCatalog);

        if (tree == null || panel == null || actions == null || theme == null ||
            database == null)
            throw new MissingReferenceException(
                "Phase 2 UI root, PanelSettings, or Input Actions asset is missing.");

        document.visualTreeAsset = tree;
        document.panelSettings = panel;
        document.sortingOrder = 100;

        SerializedObject panelObject = new(panel);
        panelObject.FindProperty("themeUss").objectReferenceValue = theme;
        panelObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);

        SerializedObject controllerObject = new(controller);
        controllerObject.FindProperty("rootTree").objectReferenceValue = tree;
        controllerObject.FindProperty("presentationCatalog").objectReferenceValue =
            presentationCatalog;
        controllerObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject inputObject = new(inputMode);
        inputObject.FindProperty("actions").objectReferenceValue = actions;
        inputObject.FindProperty("uiDocument").objectReferenceValue = document;
        inputObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Factory UI] Phase 2 root installed in " + ScenePath + ".");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
}
