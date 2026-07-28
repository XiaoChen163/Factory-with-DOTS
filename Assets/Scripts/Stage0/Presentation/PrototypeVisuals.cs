using UnityEngine;

    public static class PrototypeVisuals
    {
        private static Material gridMaterial;

        public static Material GridMaterial
        {
            get
            {
                if (gridMaterial == null)
                {
                    gridMaterial = CreateMaterial(new Color(0.22f, 0.27f, 0.31f));
                }

                return gridMaterial;
            }
        }

        public static Material CreateMaterial(Color color)
        {
            Shader shader = FindCompatibleShader();
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

        public static Material CreatePreviewMaterial()
        {
            Shader shader = FindCompatibleShader();
            Material material = new Material(shader);
            Color previewColor = new Color(0.08f, 0.52f, 1f, 0.42f);
            material.color = previewColor;

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            return material;
        }

        public static GameObject CreatePreviewCell(Transform parent, bool isBelt)
        {
            GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            preview.name = "Placement Preview";
            preview.transform.SetParent(parent, false);
            preview.transform.localScale = isBelt
                ? new Vector3(0.90f, 0.18f, 0.72f)
                : new Vector3(0.92f, 0.68f, 0.92f);
            preview.GetComponent<Renderer>().sharedMaterial = CreatePreviewMaterial();

            Collider collider = preview.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            return preview;
        }

        public static void CreateBeltVisual(Transform parent)
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                "Belt Body",
                parent,
                new Vector3(0f, 0.10f, 0f),
                new Vector3(0.92f, 0.16f, 0.72f),
                new Color(0.16f, 0.18f, 0.22f));

            GameObject arrow = CreatePrimitive(
                PrimitiveType.Cube,
                "Direction Arrow",
                parent,
                new Vector3(0.20f, 0.21f, 0f),
                new Vector3(0.38f, 0.06f, 0.16f),
                new Color(0.96f, 0.78f, 0.18f));
            arrow.transform.localRotation = Quaternion.identity;

            CreatePrimitive(
                PrimitiveType.Cube,
                "Arrow Head",
                parent,
                new Vector3(0.38f, 0.21f, 0f),
                new Vector3(0.12f, 0.07f, 0.34f),
                new Color(0.96f, 0.78f, 0.18f));
        }

        public static void CreateMachineVisual(
            Transform parent,
            Vector2Int footprint,
            Color color,
            string label,
            bool hasInput,
            bool hasOutput)
        {
            Vector3 center = new Vector3((footprint.x - 1) * 0.5f, 0.36f, (footprint.y - 1) * 0.5f);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Machine Body",
                parent,
                center,
                new Vector3(footprint.x - 0.12f, 0.70f, footprint.y - 0.12f),
                color);

            CreateWorldLabel(label, parent, center + Vector3.up * 0.38f, 44, Color.white, 0.18f);

            if (hasInput)
            {
                CreatePortMarker(parent, new Vector3(-0.48f, 0.18f, 0f), new Color(0.2f, 0.8f, 1f), "IN");
            }

            if (hasOutput)
            {
                CreatePortMarker(parent, new Vector3(1.48f, 0.18f, 0f), new Color(1f, 0.72f, 0.16f), "OUT");
            }
        }

        public static ItemInstance CreateItem(ItemData data)
        {
            GameObject visual = CreatePrimitive(
                PrimitiveType.Sphere,
                data.DisplayName,
                null,
                Vector3.zero,
                Vector3.one * 0.25f,
                data.DisplayColor);
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            return new ItemInstance(data, visual);
        }

        public static TextMesh CreateWorldLabel(
            string text,
            Transform parent,
            Vector3 localPosition,
            int fontSize,
            Color color,
            float characterSize)
        {
            GameObject labelObject = new GameObject("Label " + text);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = fontSize;
            label.characterSize = characterSize;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = color;
            return label;
        }

        private static void CreatePortMarker(Transform parent, Vector3 localPosition, Color color, string label)
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                label + " Port",
                parent,
                localPosition,
                new Vector3(0.18f, 0.18f, 0.46f),
                color);
            CreateWorldLabel(label, parent, localPosition + Vector3.up * 0.12f, 22, Color.white, 0.08f);
        }

        private static GameObject CreatePrimitive(
            PrimitiveType primitiveType,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;
            Renderer renderer = instance.GetComponent<Renderer>();
            renderer.material = CreateMaterial(color);
            return instance;
        }

        private static Shader FindCompatibleShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            return shader;
        }
}
