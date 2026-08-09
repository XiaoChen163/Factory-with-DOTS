using System;
using UnityEditor;
using UnityEngine;

public static class FactoryIconRenderer
{
    private const float MinimumExtent = 0.001f;

    public static byte[] RenderPng(
        GameObject prefab,
        FactoryIconBakeSettings settings)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        PreviewRenderUtility preview = new PreviewRenderUtility();
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            preview.AddSingleGO(instance);

            if (!TryCalculateBounds(instance, out Bounds bounds))
                throw new InvalidOperationException(
                    $"Prefab '{prefab.name}' has no enabled Renderer to bake.");

            settings.GetFraming(
                prefab,
                out Vector3 viewDirection,
                out float padding,
                out Vector3 centerOffset);
            if (viewDirection.sqrMagnitude < 0.0001f)
                viewDirection = new Vector3(-1f, -0.75f, -1f);
            viewDirection.Normalize();

            Camera camera = preview.camera;
            Vector3 target = bounds.center + centerOffset;
            float radius = Mathf.Max(bounds.extents.magnitude, MinimumExtent);
            float distance = radius * 4f + 1f;

            camera.transform.rotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            camera.transform.position = target - viewDirection * distance;
            camera.aspect = 1f;
            camera.orthographic = true;
            camera.orthographicSize = CalculateOrthographicSize(camera, bounds) *
                                      Mathf.Max(1.01f, padding);
            camera.nearClipPlane = Mathf.Max(0.001f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 2f + 1f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = settings.backgroundColor;
            camera.allowHDR = false;
            camera.allowMSAA = true;

            preview.ambientColor = settings.ambientColor;
            ConfigureLight(
                preview.lights[0],
                settings.keyLightColor,
                settings.keyLightIntensity,
                settings.keyLightEuler);
            ConfigureLight(
                preview.lights[1],
                settings.fillLightColor,
                settings.fillLightIntensity,
                settings.fillLightEuler);

            return RenderPreviewToPng(
                preview,
                camera,
                instance,
                Mathf.Max(32, settings.resolution));
        }
        finally
        {
            preview.Cleanup();
        }
    }

    private static byte[] RenderPreviewToPng(
        PreviewRenderUtility preview,
        Camera camera,
        GameObject instance,
        int resolution)
    {
        Rect rect = new Rect(0f, 0f, resolution, resolution);
        camera.backgroundColor = Color.black;
        Texture2D colorTexture = RenderStaticPreview(preview, camera, rect);
        Color32[] colorPixels = colorTexture.GetPixels32();

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        Material[][] originalMaterials = new Material[renderers.Length][];
        Material maskMaterial = CreateMaskMaterial();
        Texture2D outputTexture = null;

        try
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                originalMaterials[i] = renderers[i].sharedMaterials;
                Material[] masks = new Material[originalMaterials[i].Length];
                for (int materialIndex = 0; materialIndex < masks.Length; materialIndex++)
                    masks[materialIndex] = maskMaterial;
                renderers[i].sharedMaterials = masks;
            }

            Texture2D maskTexture = RenderStaticPreview(preview, camera, rect);
            Color32[] maskPixels = maskTexture.GetPixels32();
            Color32[] outputPixels = new Color32[colorPixels.Length];

            for (int i = 0; i < outputPixels.Length; i++)
            {
                Color32 color = colorPixels[i];
                Color32 mask = maskPixels[i];
                float alpha = (mask.r + mask.g + mask.b) / (3f * 255f);

                if (alpha <= 1f / 255f)
                {
                    outputPixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                outputPixels[i] = new Color(
                    Mathf.Clamp01(color.r / (255f * alpha)),
                    Mathf.Clamp01(color.g / (255f * alpha)),
                    Mathf.Clamp01(color.b / (255f * alpha)),
                    alpha);
            }

            outputTexture = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                false,
                false);
            outputTexture.SetPixels32(outputPixels);
            outputTexture.Apply(false, false);
            return ImageConversion.EncodeToPNG(outputTexture);
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (originalMaterials[i] != null)
                    renderers[i].sharedMaterials = originalMaterials[i];
            }
            UnityEngine.Object.DestroyImmediate(maskMaterial);
            if (outputTexture != null)
                UnityEngine.Object.DestroyImmediate(outputTexture);
        }
    }

    private static Texture2D RenderStaticPreview(
        PreviewRenderUtility preview,
        Camera camera,
        Rect rect)
    {
        preview.BeginStaticPreview(rect);
        camera.Render();
        Texture2D texture = preview.EndStaticPreview();
        if (texture == null)
            throw new InvalidOperationException("Unity returned no static preview texture.");
        return texture;
    }

    private static Material CreateMaskMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
        if (shader == null)
            throw new InvalidOperationException("No unlit shader is available for icon masking.");

        Material material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            color = Color.white
        };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        return material;
    }

    private static void ConfigureLight(
        Light light,
        Color color,
        float intensity,
        Vector3 euler)
    {
        light.color = color;
        light.intensity = Mathf.Max(0f, intensity);
        light.transform.rotation = Quaternion.Euler(euler);
    }

    private static bool TryCalculateBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        bounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static float CalculateOrthographicSize(Camera camera, Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float maxX = MinimumExtent;
        float maxY = MinimumExtent;

        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = center + Vector3.Scale(
                extents,
                new Vector3(x, y, z));
            Vector3 cameraSpace = camera.transform.InverseTransformPoint(corner);
            maxX = Mathf.Max(maxX, Mathf.Abs(cameraSpace.x));
            maxY = Mathf.Max(maxY, Mathf.Abs(cameraSpace.y));
        }

        return Mathf.Max(maxY, maxX / Mathf.Max(0.001f, camera.aspect));
    }
}
