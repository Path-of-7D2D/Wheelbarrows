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

    // Diagnostic: instantiate the built prefab and log every renderer's path, active
    // state, world bounds and lossy scale, so we can see where the wheel ended up.
    public static void DumpModel()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            throw new FileNotFoundException($"Missing prefab at {PrefabPath}");
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                Transform t = r.transform;
                string path = t.name;
                for (Transform p = t.parent; p != null; p = p.parent)
                {
                    path = p.name + "/" + path;
                }

                Debug.Log($"RENDERER {path} active={r.gameObject.activeInHierarchy} " +
                    $"mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "<none>")} " +
                    $"lossyScale={t.lossyScale} boundsCenter={r.bounds.center} boundsSize={r.bounds.size}");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(inst);
        }
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
        RigArtistModel(mesh.transform);
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

    // Builds Standard materials from the artist's vanilla 7D2D texture sets and assigns
    // them to the imported meshes, matched by FBX material name.
    private static void ApplyRustMaterials(Transform meshRoot)
    {
        Dictionary<string, Material> map = BuildRustMaterials();
        if (map.Count == 0)
        {
            Debug.LogWarning("Wheelbarrow textures not found in Assets/Wheelbarrow/Textures; keeping imported materials.");
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

        if (!AssetDatabase.IsValidFolder(MaterialsDir))
        {
            AssetDatabase.CreateFolder("Assets/Wheelbarrow", "Materials");
        }

        // Artist model materials: vanilla 7D2D texture sets matched by FBX material name.
        Texture2D miniBike = ImportTexture("minibike.tga", isNormal: false, maxSize: 2048);
        Texture2D miniBikeN = ImportTexture("minibike_n.tga", isNormal: true, maxSize: 2048);
        Texture2D banditWall = ImportTexture("banditWallMetal.tga", isNormal: false, maxSize: 2048);
        Texture2D banditWallN = ImportTexture("banditWallMetal_n.tga", isNormal: true, maxSize: 2048);
        if (miniBike != null)
        {
            map["miniBike"] = MakeMaterial("WB_MiniBike", miniBike, miniBikeN, 0.55f, 0.40f, 1.00f);
        }
        if (banditWall != null)
        {
            map["banditWallMetal"] = MakeMaterial("WB_BanditWall", banditWall, banditWallN, 0.55f, 0.35f, 1.00f);
        }

        return map;
    }

    private static Texture2D ImportTexture(string fileName, bool isNormal, int maxSize = 0)
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

            // Cap large vanilla atlases so the bundle stays a sane size.
            if (maxSize > 0 && importer.maxTextureSize != maxSize)
            {
                importer.maxTextureSize = maxSize;
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

    // The artist FBX ships as M/{hull, frame, wheel} (shallow, so the meshes keep their
    // scale). Build the deeper vehicle rig here in Unity, where empties are clean
    // scale-1: drop Forks/Wheel0 onto the wheel and slot the wheel mesh under them so
    // it can spin, place Storage on the tub, and add hand-IK grips at the handle ends.
    private static void RigArtistModel(Transform meshRoot)
    {
        Transform m = meshRoot.Find("M");
        if (m == null)
        {
            return;
        }

        Transform wheel = FindByNameContains(meshRoot, "wheel_lod");
        Transform frame = FindByNameContains(meshRoot, "chasis") ?? FindByNameContains(meshRoot, "frame");
        Transform hull = FindByNameContains(meshRoot, "hull");
        if (wheel == null && frame == null && hull == null)
        {
            return; // not the artist model (procedural pipeline)
        }

        if (wheel != null)
        {
            Transform forks = EnsureTransform(meshRoot, "M/Forks");
            Transform wheel0 = EnsureTransform(meshRoot, "M/Forks/Wheel0");
            Vector3 wc = RendererCenter(wheel);
            forks.position = wc;
            forks.rotation = m.rotation;
            wheel0.position = wc;
            wheel0.rotation = m.rotation;
            wheel.SetParent(wheel0, worldPositionStays: true);
        }

        if (hull != null)
        {
            Transform storage = EnsureTransform(meshRoot, "M/Storage");
            storage.position = RendererCenter(hull);
        }

        if (frame != null)
        {
            ComputeHandleGrips(frame, out Vector3 gripL, out Vector3 gripR);
            CreateGrip(m, "Grip_Left_End", gripL);
            CreateGrip(m, "Grip_Right_End", gripR);
        }
    }

    private static Vector3 RendererCenter(Transform t)
    {
        Renderer r = t.GetComponent<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    // Rear handle ends = frame verts at min Z (handles point to -Z), split left/right.
    private static void ComputeHandleGrips(Transform frame, out Vector3 gripL, out Vector3 gripR)
    {
        gripL = frame.position;
        gripR = frame.position;
        MeshFilter mf = frame.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null)
        {
            return;
        }

        Vector3[] verts = mesh.vertices;
        float minZ = float.MaxValue;
        for (int i = 0; i < verts.Length; i++)
        {
            float z = frame.TransformPoint(verts[i]).z;
            if (z < minZ)
            {
                minZ = z;
            }
        }

        float cut = minZ + 0.20f;
        float centerX = RendererCenter(frame).x;
        Vector3 sumL = Vector3.zero, sumR = Vector3.zero;
        int nL = 0, nR = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = frame.TransformPoint(verts[i]);
            if (w.z > cut)
            {
                continue;
            }

            if (w.x < centerX)
            {
                sumL += w;
                nL++;
            }
            else
            {
                sumR += w;
                nR++;
            }
        }

        if (nL > 0)
        {
            gripL = sumL / nL;
        }

        if (nR > 0)
        {
            gripR = sumR / nR;
        }
    }

    private static void CreateGrip(Transform parent, string name, Vector3 worldPos)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.position = worldPos;
        go.transform.rotation = parent.rotation;
    }

    private static Transform FindByNameContains(Transform root, string nameLower)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.ToLowerInvariant().Contains(nameLower))
            {
                return t;
            }
        }

        return null;
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
