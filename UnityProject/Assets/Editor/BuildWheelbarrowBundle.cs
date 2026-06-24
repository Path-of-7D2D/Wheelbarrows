using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public static class BuildWheelbarrowBundle
{
    private const string ModelPath = "Assets/Wheelbarrow/Models/Wheelbarrow.fbx";
    private const string PrefabPath = "Assets/Wheelbarrow/Prefabs/WheelbarrowPrefab.prefab";
    private const string TexturesDir = "Assets/Wheelbarrow/Textures";
    private const string MaterialsDir = "Assets/Wheelbarrow/Materials";
    private const string BundleName = "wheelbarrow.unity3d";
    private const float VisualScale = 80f;

    public static void BuildAll()
    {
        EnsurePrefab();

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string outputDir = Path.Combine(projectRoot, "1A-Wheelbarrow", "Resources");
        Directory.CreateDirectory(outputDir);

        BuildPipeline.BuildAssetBundles(
            outputDir,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        DeleteIfExists(Path.Combine(outputDir, new DirectoryInfo(outputDir).Name));
        DeleteIfExists(Path.Combine(outputDir, new DirectoryInfo(outputDir).Name + ".manifest"));
        foreach (string manifest in Directory.GetFiles(outputDir, "*.manifest"))
        {
            DeleteIfExists(manifest);
        }

        Debug.Log($"Built {BundleName} to {outputDir}");
    }

    [MenuItem("Wheelbarrow/Build AssetBundle")]
    public static void BuildFromMenu()
    {
        BuildAll();
    }

    public static void ValidateBuiltBundle()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string bundlePath = Path.Combine(projectRoot, "1A-Wheelbarrow", "Resources", BundleName);
        if (!File.Exists(bundlePath))
        {
            throw new FileNotFoundException($"Missing built bundle at {bundlePath}");
        }

        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
        {
            throw new InvalidOperationException($"Could not load asset bundle at {bundlePath}");
        }

        try
        {
            string[] assetNames = bundle.GetAllAssetNames();
            foreach (string assetName in assetNames)
            {
                Debug.Log($"Bundle asset: {assetName}");
            }

            GameObject prefab = null;
            foreach (string assetName in assetNames)
            {
                if (assetName.EndsWith("wheelbarrowprefab.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    prefab = bundle.LoadAsset<GameObject>(assetName);
                    break;
                }
            }

            if (prefab == null)
            {
                throw new InvalidOperationException("Built bundle does not contain WheelbarrowPrefab.prefab");
            }

            AssertTransform(prefab.transform, "GameObject");
            AssertTransform(prefab.transform, "GameObject/Mesh");
            AssertTransform(prefab.transform, "GameObject/Mesh/M");
            AssertTransform(prefab.transform, "GameObject/Mesh/M/Forks");
            AssertTransform(prefab.transform, "GameObject/Mesh/M/Forks/Wheel0");
            AssertTransform(prefab.transform, "GameObject/Mesh/M/Crank");
            AssertTransform(prefab.transform, "GameObject/Mesh/M/Storage");
            AssertTransform(prefab.transform, "GameObject/Mesh/Wheel1");
            AssertTransform(prefab.transform, "Physics");
            AssertTransform(prefab.transform, "Physics/Wheel0");
            AssertTransform(prefab.transform, "Physics/Wheel1");

            Transform modelRoot = prefab.transform.Find("GameObject");
            Transform mesh = prefab.transform.Find("GameObject/Mesh");
            Transform physics = prefab.transform.Find("Physics");
            if (modelRoot.Find("Physics") != null || mesh.Find("Physics") != null)
            {
                throw new InvalidOperationException("WheelbarrowPrefab Physics must be a direct root child, not nested under GameObject/Mesh.");
            }

            if (!physics.CompareTag("Untagged"))
            {
                throw new InvalidOperationException("WheelbarrowPrefab direct Physics transform must stay untagged so 7D2D resolves it by root name.");
            }

            if (physics.GetComponent<Rigidbody>() == null)
            {
                throw new InvalidOperationException("WheelbarrowPrefab Physics transform is missing its Rigidbody");
            }

            if (physics.GetComponent<BoxCollider>() == null)
            {
                throw new InvalidOperationException("WheelbarrowPrefab Physics transform is missing its BoxCollider");
            }

            AssertWheelCollider(physics, "Wheel0");
            AssertWheelCollider(physics, "Wheel1");

            if (prefab.GetComponent<Rigidbody>() != null || prefab.GetComponent<Collider>() != null)
            {
                throw new InvalidOperationException("WheelbarrowPrefab root should not have physics components; collision belongs under Physics");
            }

            if (modelRoot.GetComponent<Rigidbody>() != null || modelRoot.GetComponent<Collider>() != null)
            {
                throw new InvalidOperationException("WheelbarrowPrefab GameObject should not have physics components; collision belongs under Physics");
            }

            if (mesh.GetComponent<Rigidbody>() != null || mesh.GetComponent<Collider>() != null)
            {
                throw new InvalidOperationException("WheelbarrowPrefab Mesh should not have physics components; collision belongs under Physics");
            }

            Bounds visualBounds = GetCombinedRendererBounds(mesh, out int rendererCount);
            float maxVisualDimension = Mathf.Max(visualBounds.size.x, visualBounds.size.y, visualBounds.size.z);
            Debug.Log($"Wheelbarrow visual renderers={rendererCount}, bounds center={visualBounds.center}, size={visualBounds.size}");
            if (rendererCount == 0)
            {
                throw new InvalidOperationException("WheelbarrowPrefab Mesh does not contain any renderers.");
            }

            if (maxVisualDimension < 0.5f || maxVisualDimension > 5f)
            {
                throw new InvalidOperationException($"WheelbarrowPrefab visual bounds look wrong: max dimension {maxVisualDimension:0.###}m");
            }

            Debug.Log("Wheelbarrow bundle validation passed.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static void EnsurePrefab()
    {
        ConfigureModelImport();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            throw new FileNotFoundException($"Missing generated model at {ModelPath}. Run tools/generate_wheelbarrow_model.py with Blender first.");
        }

        GameObject instance = new GameObject("WheelbarrowPrefab");
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        GameObject modelRoot = new GameObject("GameObject");
        modelRoot.transform.SetParent(instance.transform, false);
        modelRoot.transform.localPosition = Vector3.zero;
        modelRoot.transform.localRotation = Quaternion.identity;
        modelRoot.transform.localScale = Vector3.one;

        GameObject mesh = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (mesh == null)
        {
            mesh = UnityEngine.Object.Instantiate(model);
        }

        mesh.name = "Mesh";
        mesh.transform.SetParent(modelRoot.transform, false);
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localScale = Vector3.one;
        if (PrefabUtility.IsPartOfPrefabInstance(mesh))
        {
            PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        EnsureVehicleTransforms(mesh.transform);
        ApplyRustMaterials(mesh.transform);
        RemoveRootCollider(instance);
        RemoveRootCollider(modelRoot);
        RemoveRootCollider(mesh);
        EnsurePhysics(instance);

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        UnityEngine.Object.DestroyImmediate(instance);

        AssetImporter importer = AssetImporter.GetAtPath(PrefabPath);
        if (importer == null)
        {
            throw new InvalidOperationException($"Could not load prefab importer for {PrefabPath}");
        }

        importer.assetBundleName = BundleName;
        importer.SaveAndReimport();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void ConfigureModelImport()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Could not load model importer for {ModelPath}");
        }

        if (Math.Abs(importer.globalScale - VisualScale) > 0.001f)
        {
            importer.globalScale = VisualScale;
            importer.SaveAndReimport();
        }
    }

    // Replaces the FBX's flat placeholder materials with weathered/rusted Standard
    // materials built from tools/generate_textures.py output, matched by original name.
    private static void ApplyRustMaterials(Transform meshRoot)
    {
        Dictionary<string, Material> map = BuildRustMaterials();
        if (map.Count == 0)
        {
            Debug.LogWarning("Rust textures not found; keeping placeholder materials. Run tools/generate_textures.py.");
            return;
        }

        Renderer[] renderers = meshRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] shared = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null)
                {
                    continue;
                }

                string key = shared[i].name.Replace(" (Instance)", string.Empty).Trim();
                if (map.TryGetValue(key, out Material replacement))
                {
                    shared[i] = replacement;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = shared;
            }
        }

        AssetDatabase.SaveAssets();
    }

    private static Dictionary<string, Material> BuildRustMaterials()
    {
        var map = new Dictionary<string, Material>();

        Texture2D metal = ImportTexture("wb_metal_albedo.png", isNormal: false);
        Texture2D red = ImportTexture("wb_red_albedo.png", isNormal: false);
        Texture2D wood = ImportTexture("wb_wood_albedo.png", isNormal: false);
        Texture2D rubber = ImportTexture("wb_rubber_albedo.png", isNormal: false);
        Texture2D normal = ImportTexture("wb_metal_normal.png", isNormal: true);

        if (metal == null || red == null)
        {
            return map;
        }

        if (!AssetDatabase.IsValidFolder(MaterialsDir))
        {
            AssetDatabase.CreateFolder("Assets/Wheelbarrow", "Materials");
        }

        // original FBX material name -> rusted replacement
        map["dark_worn_metal"] = MakeMaterial("WB_Metal", metal, normal, 0.30f, 0.30f, 1.00f);
        map["weathered_red_metal"] = MakeMaterial("WB_Red", red, normal, 0.12f, 0.34f, 1.00f);
        map["dark_red_worn_edges"] = MakeMaterial("WB_RedDark", red, normal, 0.18f, 0.24f, 0.78f);
        map["matte_black_rubber"] = MakeMaterial("WB_Rubber", rubber, null, 0.00f, 0.16f, 1.00f);
        map["worn_handle_wood"] = MakeMaterial("WB_Wood", wood, null, 0.00f, 0.20f, 1.00f);
        return map;
    }

    private static Texture2D ImportTexture(string fileName, bool isNormal)
    {
        string path = TexturesDir + "/" + fileName;
        if (!File.Exists(path))
        {
            return null;
        }

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }

        if (importer != null)
        {
            bool dirty = false;
            TextureImporterType wanted = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != wanted)
            {
                importer.textureType = wanted;
                dirty = true;
            }

            if (!isNormal && !importer.sRGBTexture)
            {
                importer.sRGBTexture = true;
                dirty = true;
            }

            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material MakeMaterial(string name, Texture2D albedo, Texture2D normal, float metallic, float smoothness, float tint)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = Shader.Find("Standard");
        }

        material.mainTexture = albedo;
        material.SetColor("_Color", new Color(tint, tint, tint, 1f));
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);

        if (normal != null)
        {
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Bounds GetCombinedRendererBounds(Transform root, out int rendererCount)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        rendererCount = 0;
        Bounds bounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (rendererCount == 0)
            {
                bounds = renderer.bounds;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }

            rendererCount++;
        }

        return bounds;
    }

    private static void EnsureVehicleTransforms(Transform root)
    {
        EnsureTransform(root, "M");
        EnsureTransform(root, "M/Forks");
        EnsureTransform(root, "M/Forks/Wheel0");
        EnsureTransform(root, "M/Crank");
        EnsureTransform(root, "M/Crank/PedalL");
        EnsureTransform(root, "M/Crank/PedalR");
        EnsureTransform(root, "M/Storage");
        EnsureTransform(root, "Wheel1");
    }

    private static void RemoveRootCollider(GameObject instance)
    {
        BoxCollider collider = instance.GetComponent<BoxCollider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }
    }

    private static void EnsurePhysics(GameObject instance)
    {
        Transform physics = EnsureTransform(instance.transform, "Physics");
        physics.localPosition = Vector3.zero;
        physics.localRotation = Quaternion.identity;
        physics.localScale = Vector3.one;
        physics.gameObject.tag = "Untagged";

        Rigidbody rigidbody = physics.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = physics.gameObject.AddComponent<Rigidbody>();
        }

        rigidbody.mass = 120f;
        rigidbody.drag = 0.35f;
        rigidbody.angularDrag = 3.0f;
        rigidbody.useGravity = false;
        rigidbody.isKinematic = false;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

        BoxCollider body = physics.GetComponent<BoxCollider>();
        if (body == null)
        {
            body = physics.gameObject.AddComponent<BoxCollider>();
        }

        body.center = new Vector3(0f, 0.68f, -0.05f);
        body.size = new Vector3(0.86f, 0.55f, 1.20f);
        body.isTrigger = false;

        EnsureWheelCollider(physics, "Wheel0", new Vector3(0f, 0.34f, 0.82f), 0.31f, 0.08f, 0.55f);
        EnsureWheelCollider(physics, "Wheel1", new Vector3(0f, 0.33f, -0.78f), 0.29f, 0.08f, 0.55f);
    }

    private static void EnsureWheelCollider(
        Transform physics,
        string name,
        Vector3 localPosition,
        float radius,
        float suspensionDistance,
        float stiffness)
    {
        Transform wheel = EnsureTransform(physics, name);
        wheel.localPosition = localPosition;
        wheel.localRotation = Quaternion.identity;
        wheel.localScale = Vector3.one;

        WheelCollider collider = wheel.GetComponent<WheelCollider>();
        if (collider == null)
        {
            collider = wheel.gameObject.AddComponent<WheelCollider>();
        }

        collider.center = Vector3.zero;
        collider.mass = 8f;
        collider.radius = radius;
        collider.suspensionDistance = suspensionDistance;
        collider.forceAppPointDistance = 0f;
        collider.wheelDampingRate = 1.2f;

        JointSpring spring = collider.suspensionSpring;
        spring.spring = 6500f;
        spring.damper = 5200f;
        spring.targetPosition = 0.45f;
        collider.suspensionSpring = spring;

        WheelFrictionCurve forward = collider.forwardFriction;
        forward.extremumSlip = 0.55f;
        forward.extremumValue = 0.75f;
        forward.asymptoteSlip = 1.2f;
        forward.asymptoteValue = 0.35f;
        forward.stiffness = stiffness;
        collider.forwardFriction = forward;

        WheelFrictionCurve sideways = collider.sidewaysFriction;
        sideways.extremumSlip = 0.45f;
        sideways.extremumValue = 0.75f;
        sideways.asymptoteSlip = 1.0f;
        sideways.asymptoteValue = 0.35f;
        sideways.stiffness = stiffness;
        collider.sidewaysFriction = sideways;
    }

    private static void EnsureTag(string tag)
    {
        foreach (string existing in InternalEditorUtility.tags)
        {
            if (existing == tag)
            {
                return;
            }
        }

        InternalEditorUtility.AddTag(tag);
    }

    private static void AssertWheelCollider(Transform physics, string name)
    {
        Transform wheel = physics.Find(name);
        if (wheel == null || wheel.GetComponent<WheelCollider>() == null)
        {
            throw new InvalidOperationException($"WheelbarrowPrefab Physics/{name} is missing its WheelCollider");
        }
    }

    private static Transform EnsureTransform(Transform root, string path)
    {
        Transform existing = root.Find(path);
        if (existing != null)
        {
            return existing;
        }

        Transform parent = root;
        foreach (string segment in path.Split('/'))
        {
            Transform child = parent.Find(segment);
            if (child == null)
            {
                GameObject created = new GameObject(segment);
                child = created.transform;
                child.SetParent(parent, false);
            }

            parent = child;
        }

        return parent;
    }

    private static void AssertTransform(Transform root, string path)
    {
        if (root.Find(path) == null)
        {
            throw new InvalidOperationException($"WheelbarrowPrefab is missing transform path {path}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
