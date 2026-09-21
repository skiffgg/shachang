using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

// Command-line automation:  Unity.exe -batchmode -quit -projectPath ... -executeMethod Builder.Setup / Builder.Build
public static class Builder
{
    const string OUT = "Build/ShaChang.exe";

    public static void All() { Setup(); Build(); }

    public static void Setup()
    {
        // the room directory is plain HTTP; Unity blocks that in a player unless it is allowed here
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

        ImportSettings();
        Materials();
        Project();
        Scene();
        if (!File.Exists("Assets/StreamingAssets/tune.json")) File.WriteAllText("Assets/StreamingAssets/tune.json", JsonUtility.ToJson(new Tune(), true));
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("[Builder] setup done");
    }

    static void ImportSettings()
    {
        foreach (var p in new[] { "Assets/Resources/Models/enemy.glb", "Assets/Resources/Models/boss.glb", "Assets/Resources/Models/m16fp/m16fp.gltf" })
        {
            var imp = AssetImporter.GetAtPath(p); if (imp == null) continue;
            var so = new SerializedObject(imp); var it = so.GetIterator(); bool changed = false;
            while (it.Next(true)) if (it.name.ToLower().Contains("animationmethod") && it.intValue != 1) { it.intValue = 1; changed = true; }
            if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); imp.SaveAndReimport(); Debug.Log("[Builder] legacy anim " + p); }
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Tex" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool nrm = path.EndsWith("_nor.jpg"), rough = path.EndsWith("_rough.jpg");
            var type = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType == type && ti.maxTextureSize == 1024) continue;
            ti.textureType = type; ti.sRGBTexture = !(nrm || rough); ti.anisoLevel = 8; ti.wrapMode = TextureWrapMode.Repeat; ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }
        // audio: decompress so clips can be sliced at runtime; mono keeps memory down
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid); var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = ai.defaultSampleSettings; if (s.loadType == AudioClipLoadType.DecompressOnLoad && ai.forceToMono) continue;
            s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f;
            ai.defaultSampleSettings = s; ai.forceToMono = true; ai.SaveAndReimport();
        }
    }

    static Material Mat(string name, string shader)
    {
        Directory.CreateDirectory("Assets/Resources/Mats");
        var path = "Assets/Resources/Mats/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shader); return m;
    }

    static Material Surface(string name, string tex, float tile, Color tint, float smooth)
    {
        var m = Mat(name, "Standard");
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Tex/" + tex + "_diff.jpg"));
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Tex/" + tex + "_nor.jpg"));
        m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f);
        m.mainTextureScale = new Vector2(tile, tile); m.SetColor("_Color", tint); m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", 0);
        EditorUtility.SetDirty(m); return m;
    }

    static void Materials()
    {
        Surface("Ground", "dense_sand", 150, new Color(1f, 0.96f, 0.9f), 0.08f);
        Surface("Road", "concrete_floor_worn_001", 1, new Color(0.88f, 0.84f, 0.78f), 0.15f);
        Surface("Plaster", "beige_wall_001", 1, new Color(1f, 0.95f, 0.86f), 0.1f);
        Surface("Stone", "sandstone_blocks_05", 1, Color.white, 0.1f);
        Surface("Bags", "dense_sand", 1, new Color(0.78f, 0.68f, 0.5f), 0.05f);
        Surface("Wood", "concrete_floor_worn_001", 1, new Color(0.45f, 0.32f, 0.2f), 0.2f);
        var water = Mat("Water", "Standard"); water.color = new Color(0.12f, 0.32f, 0.36f, 0.85f); water.SetFloat("_Glossiness", 0.95f); water.SetFloat("_Metallic", 0.1f);
        water.SetFloat("_Mode", 3); water.SetInt("_SrcBlend", (int)BlendMode.One); water.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); water.SetInt("_ZWrite", 0);
        water.EnableKeyword("_ALPHAPREMULTIPLY_ON"); water.renderQueue = 3000;
        // a normal map is needed for the ripples; the runtime swaps in a generated one
        var wn = Resources.Load<Texture2D>("Tex/concrete_floor_worn_001_nor");
        if (wn) { water.SetTexture("_BumpMap", wn); water.EnableKeyword("_NORMALMAP"); water.SetFloat("_BumpScale", 0.6f); }
        EditorUtility.SetDirty(water);
        // cutout material for the grass tufts; created as an asset so the _ALPHATEST_ON variant survives build stripping
        var grass = Mat("Grass", "Standard"); grass.color = Color.white; grass.SetFloat("_Glossiness", 0.05f); grass.SetFloat("_Metallic", 0f);
        grass.SetFloat("_Mode", 1); grass.SetInt("_SrcBlend", (int)BlendMode.One); grass.SetInt("_DstBlend", (int)BlendMode.Zero); grass.SetInt("_ZWrite", 1);
        grass.SetFloat("_Cutoff", 0.35f); grass.EnableKeyword("_ALPHATEST_ON"); grass.SetOverrideTag("RenderType", "TransparentCutout"); grass.renderQueue = 2450;
        grass.doubleSidedGI = true; EditorUtility.SetDirty(grass);
        // cutout material for leaves and grass cards
        // Standard's cutout variant gets stripped from the player build, so leaves use the legacy
        // cutout shader instead — it is a single variant and always renders alpha-clipped.
        var foli = Mat("Foliage", "Legacy Shaders/Transparent/Cutout/Diffuse");
        foli.color = Color.white; foli.SetFloat("_Cutoff", 0.4f); EditorUtility.SetDirty(foli);
        var terr = Mat("Terrain", "Nature/Terrain/Standard"); EditorUtility.SetDirty(terr);
        var sky = Mat("Sky", "Skybox/Procedural");
        sky.SetFloat("_SunSize", 0.035f); sky.SetFloat("_AtmosphereThickness", 1.15f); sky.SetColor("_SkyTint", new Color(0.55f, 0.6f, 0.68f));
        sky.SetColor("_GroundColor", new Color(0.62f, 0.55f, 0.46f)); sky.SetFloat("_Exposure", 1.2f); EditorUtility.SetDirty(sky);
    }

    // FishNet's manager, transport and spawnable prefab list, created as real assets so the
    // engine initialises them the normal way.
    static void Networking()
    {
        Directory.CreateDirectory("Assets/Resources/Net");

        // player prefab: a networked avatar
        const string prefabPath = "Assets/Resources/Net/NetPlayer.prefab";
        var tmp = new GameObject("NetPlayer");
        tmp.AddComponent<FishNet.Object.NetworkObject>();
        tmp.AddComponent<NetPlayer>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(tmp, prefabPath);
        Object.DestroyImmediate(tmp);

        // match state prefab: rules plus the vehicle relay, spawned once by the server
        const string matchPath = "Assets/Resources/Net/Match.prefab";
        var mtmp = new GameObject("Match");
        mtmp.AddComponent<FishNet.Object.NetworkObject>();
        mtmp.AddComponent<TdmMatch>();
        var matchPrefab = PrefabUtility.SaveAsPrefabAsset(mtmp, matchPath);
        Object.DestroyImmediate(mtmp);

        const string listPath = "Assets/Resources/Net/SpawnablePrefabs.asset";
        var list = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(listPath);
        if (!list)
        {
            list = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
            AssetDatabase.CreateAsset(list, listPath);
        }
        list.Clear();
        list.AddObject(prefab.GetComponent<FishNet.Object.NetworkObject>(), true);
        list.AddObject(matchPrefab.GetComponent<FishNet.Object.NetworkObject>(), true);
        EditorUtility.SetDirty(list);

        // scene objects: the manager plus the match state
        var netGo = new GameObject("NetworkManager");
        var tug = netGo.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();
        tug.SetPort(7777); tug.SetMaximumClients(16);
        var nm = netGo.AddComponent<FishNet.Managing.NetworkManager>();
        nm.SpawnablePrefabs = list;
        netGo.AddComponent<NetBoot>();

        AssetDatabase.SaveAssets();
    }

    // Linux headless build for the match server (Vultr)
    public static void BuildLinuxServer()
    {
        Setup();
        const string outDir = "F:/Unity/Projects/ShaChang/ServerBuild";
        Directory.CreateDirectory(outDir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = outDir + "/shachang-server.x86_64",
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None
        };
        var r = BuildPipeline.BuildPlayer(opts);
        // the deployment kit travels with the build, so the tarball is everything the host needs
        var deploySrc = "F:/Unity/Projects/ShaChang/deploy";
        var deployDst = outDir + "/deploy";
        if (Directory.Exists(deploySrc))
        {
            Directory.CreateDirectory(deployDst);
            foreach (var f in Directory.GetFiles(deploySrc)) File.Copy(f, deployDst + "/" + Path.GetFileName(f), true);
        }
        Debug.Log("[Builder] linux server build: " + r.summary.result + " size=" + (r.summary.totalSize / 1048576) + "MB errors=" + r.summary.totalErrors);
        if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    static void Project()
    {
        PlayerSettings.companyName = "ShaChang"; PlayerSettings.productName = "沙场";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow; PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.resizableWindow = true; PlayerSettings.enableFrameTimingStats = true; PlayerSettings.runInBackground = true;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
        QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.Medium; QualitySettings.shadowDistance = 60; QualitySettings.shadowCascades = 2;
        QualitySettings.antiAliasing = 2; QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; QualitySettings.pixelLightCount = 3;
        QualitySettings.skinWeights = SkinWeights.TwoBones; QualitySettings.softParticles = false; QualitySettings.realtimeReflectionProbes = false;
        var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
        var so = new SerializedObject(gs); var arr = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var sn in new[] { "Sprites/Default", "Skybox/Procedural", "Standard", "Legacy Shaders/Transparent/Cutout/Diffuse", "Legacy Shaders/Transparent/Cutout/VertexLit", "Nature/Terrain/Standard", "Hidden/TerrainEngine/Splatmap/Standard-AddPass", "Hidden/TerrainEngine/Splatmap/Standard-Base", "Hidden/TerrainEngine/Splatmap/Standard-BaseGen", "Hidden/TerrainEngine/BillboardTree", "Hidden/TerrainEngine/Details/Vertexlit", "Hidden/TerrainEngine/Details/WavingDoublePass", "Hidden/TerrainEngine/Details/BillboardWavingDoublePass" })
        {
            var sh = Shader.Find(sn); if (!sh) { Debug.LogWarning("[Builder] shader not found " + sn); continue; } bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
            if (!has) { arr.InsertArrayElementAtIndex(arr.arraySize); arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh; }
        }
        var fs = so.FindProperty("m_FogStripping"); if (fs != null) fs.intValue = 1;            // Custom
        var fl = so.FindProperty("m_FogKeepLinear"); if (fl != null) fl.boolValue = true;
        var fe = so.FindProperty("m_FogKeepExp"); if (fe != null) fe.boolValue = true;
        var fe2 = so.FindProperty("m_FogKeepExp2"); if (fe2 != null) fe2.boolValue = true;
        so.ApplyModifiedProperties();
    }

    static void Scene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // camera with post-processing (M): bloom, ACES grading, vignette, AO / motion blur on high
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; var cam = camGo.AddComponent<Camera>(); cam.allowHDR = true; cam.allowMSAA = true;
        camGo.AddComponent<AudioListener>();
        var layer = camGo.AddComponent<PostProcessLayer>();
        var res = AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
        if (res) layer.Init(res); else Debug.LogWarning("[Builder] PostProcessResources not found");
        layer.volumeLayer = ~0; layer.volumeTrigger = camGo.transform; layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
        var profilePath = "Assets/Resources/Mats/PostFX.asset";
        var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(profilePath);
        if (!profile) { profile = ScriptableObject.CreateInstance<PostProcessProfile>(); AssetDatabase.CreateAsset(profile, profilePath); }
        profile.settings.Clear();
        var bloom = profile.AddSettings<Bloom>(); bloom.enabled.Override(true); bloom.intensity.Override(1.4f); bloom.threshold.Override(1.05f); bloom.softKnee.Override(0.6f); bloom.fastMode.Override(true);
        var cg = profile.AddSettings<ColorGrading>(); cg.enabled.Override(true); cg.tonemapper.Override(Tonemapper.ACES); cg.postExposure.Override(0.35f); cg.saturation.Override(6f); cg.contrast.Override(8f); cg.temperature.Override(6f);
        var vig = profile.AddSettings<Vignette>(); vig.enabled.Override(true); vig.intensity.Override(0.28f); vig.smoothness.Override(0.45f);
        var ao = profile.AddSettings<AmbientOcclusion>(); ao.enabled.Override(false); ao.intensity.Override(0.7f); ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        var mb = profile.AddSettings<MotionBlur>(); mb.enabled.Override(false); mb.shutterAngle.Override(120);
        var dof = profile.AddSettings<DepthOfField>(); dof.enabled.Override(false);
        foreach (var s in profile.settings) AssetDatabase.AddObjectToAsset(s, profile);
        EditorUtility.SetDirty(profile);
        Networking();
        var volGo = new GameObject("PostFX"); var vol = volGo.AddComponent<PostProcessVolume>(); vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = profile;
        new GameObject("Game").AddComponent<Game>();
        EditorSceneManager.SaveScene(sc, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
    }

    public static void Build()
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = OUT, target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.None });
        Debug.Log("[Builder] build result: " + r.summary.result + " size=" + (r.summary.totalSize / 1048576) + "MB errors=" + r.summary.totalErrors);
        if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(2);
    }
}

