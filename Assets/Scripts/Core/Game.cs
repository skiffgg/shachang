using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.PostProcessing;

public enum Mode { Menu, Endless, Extraction, Conquest, Online }

// Boots everything, runs the two modes, day/night + weather, post-processing quality, HUD and menus.
public class Game : MonoBehaviour
{
    public static Game I;
    public static bool Headless;                 // dedicated server: no shaders, no camera, no audio
    public Player player; public Mode mode = Mode.Menu; public bool paused, night; public float weatherK;
    public int wave, score, kills, points, lootValue;
    string message = ""; float messageT; readonly List<(string, float)> feed = new List<(string, float)>();
    float fps, fpsAcc; int fpsN; string shotPath; float shotAt = -1;
    float dayT = 0.3f, timeLeft, extractHold, weatherTimer = 120, reinforceT;
    bool showHelp, showShop, gameOver, won, showJoin; public bool showMap;
    string joinIp = "127.0.0.1", joinPort = "7777", lobbyUrl = "http://66.135.26.234:8080"; string endText = "";
    Texture2D mapTex, ringTex, vigTex;
    public Vector3 wp; public string wpName = ""; int wpIndex = -1;
    public float scoreP, scoreE, respawnT; bool showBag;      // G: conquest state
    Light sun, flashlight; ParticleSystem dust; AudioSource wind; bool? torch;   // null = automatic at night
    PostProcessVolume volume; PostProcessLayer ppLayer; int quality = -1;
    static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

    public static GameObject Model(string name)
    {
        if (!cache.TryGetValue(name, out var g)) { g = Resources.Load<GameObject>("Models/" + name); cache[name] = g; if (!g) Debug.LogError("missing model " + name); }
        return g;
    }

    public static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(); var b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
        foreach (var r in rs) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        return b;
    }

    public static BoxCollider AddBoundsCollider(GameObject go, float shrink = 1f)
    {
        var rot = go.transform.rotation; go.transform.rotation = Quaternion.identity;
        var b = WorldBounds(go); go.transform.rotation = rot;
        var c = go.AddComponent<BoxCollider>(); var s = go.transform.lossyScale; var p = go.transform.position;
        c.center = new Vector3((b.center.x - p.x) / s.x, (b.center.y - p.y) / s.y, (b.center.z - p.z) / s.z);
        c.size = new Vector3(b.size.x / s.x * shrink, b.size.y / s.y, b.size.z / s.z * shrink);
        return c;
    }

    void Awake()
    {
        I = this; Tune.Load();
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1;
        var gpu = SystemInfo.graphicsDeviceName.ToLower(); bool integrated = gpu.Contains("intel") || gpu.Contains("uhd") || gpu.Contains("iris") || gpu.Contains("radeon(tm) graphics") || gpu.Contains("vega");
        quality = PlayerPrefs.GetInt("quality_v2", integrated ? 1 : 2);
        Sfx.volume = PlayerPrefs.GetFloat("volume", 0.9f); Tune.I.mouseSens = PlayerPrefs.GetFloat("sens", Tune.I.mouseSens);
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) { if (args[i] == "-shot") { shotPath = args[i + 1]; shotAt = 9999f; } if (args[i] == "-mode") autoMode = args[i + 1]; if (args[i] == "-dayt") dayT = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture); if (args[i] == "-weapon") autoWeapon = int.Parse(args[i + 1]); if (args[i] == "-pose") autoPose = args[i + 1]; if (args[i] == "-delay") shotDelay = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture); if (args[i] == "-quality") quality = int.Parse(args[i + 1]); }
    }
    string autoMode, autoPose; int autoWeapon = -1; public bool autoHeliUp, autoAds, autoBoom, forceStorm, attDebug;
    public static bool NetDebug, AutoFire, AutoAim;   // test hooks for verifying an online match

    IEnumerator Start()
    {
        Headless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
        Fx.Disabled = Headless;
        SetupLighting();
        var w = new GameObject("World").AddComponent<World>(); w.Build();
        var surf = w.gameObject.AddComponent<NavMeshSurface>(); surf.collectObjects = CollectObjects.Children; surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders; surf.BuildNavMesh();
        ppLayer = Camera.main ? Camera.main.GetComponent<PostProcessLayer>() : null; volume = FindAnyObjectByType<PostProcessVolume>();
        ApplyQuality();
        yield return null;
        RenderMap();
        var argl = new List<string>(System.Environment.GetCommandLineArgs());
        if (argl.Contains("-nopp") && ppLayer) ppLayer.enabled = false;
        if (argl.Contains("-noshadow")) QualitySettings.shadows = ShadowQuality.Disable;
        if (argl.Contains("-noworld")) foreach (Transform c in w.transform) if (!c.GetComponent<Terrain>()) c.gameObject.SetActive(false);
        noEnemies = argl.Contains("-noenemies");
        if (!Headless) wind = Sfx.Loop(transform, "wind", 0.15f, false);
        // menu backdrop camera orbit
        yield return null;
        if (autoMode == "endless") StartMode(Mode.Endless); else if (autoMode == "extract") StartMode(Mode.Extraction); else if (autoMode == "conquest") StartMode(Mode.Conquest); else if (shotPath != null) shotAt = Time.realtimeSinceStartup + shotDelay;
        if (argl.Contains("-heli") && player) { yield return new WaitForSeconds(0.5f); var h = Vehicle.All.Find(v => v.kind == "heli"); if (h) { player.transform.position = h.transform.position + Vector3.right * 2; player.SendMessage("TryEnterVehicle", SendMessageOptions.DontRequireReceiver); autoHeliUp = true; } }
        if (argl.Contains("-showmap")) showMap = true;
        if (argl.Contains("-drivetest")) StartCoroutine(VehicleTest(true));
        if (argl.Contains("-watchveh")) StartCoroutine(VehicleTest(false));
        if (argl.Contains("-ads")) autoAds = true;
        if (argl.Contains("-boom")) autoBoom = true;
        if (argl.Contains("-bag")) showBag = true;
        if (argl.Contains("-attdbg")) attDebug = true;
        NetDebug = argl.Contains("-netdebug"); AutoAim = argl.Contains("-autoaim"); AutoFire = argl.Contains("-autofire") || AutoAim;
        NetBoot.ParseArgs();
        if (NetBoot.IsServerBuild) { StartCoroutine(RunDedicatedServer()); yield break; }
        var lobbyArg = ParseArg("-lobby");
        if (!string.IsNullOrEmpty(lobbyArg)) { lobbyUrl = lobbyArg; LobbyClient.LobbyUrl = lobbyArg; showJoin = true; LobbyClient.Create().Refresh(); }
        if (NetBoot.WantsClient) StartOnline(NetBoot.ConnectIp, NetBoot.Port);
        if (argl.Contains("-lean")) StartCoroutine(LeanDebug());
        if (argl.Contains("-storm")) { weatherK = 1f; stormTarget = 1f; forceStorm = true; }
        if (argl.Contains("-mg") && player) { yield return new WaitForSeconds(0.5f); var pv = Vehicle.All.Find(x => x.kind == "pickup"); if (pv) { player.transform.position = pv.transform.position + Vector3.right * 2; player.TryEnterVehicle(); } }
        if (argl.Contains("-allatt") && player) { yield return new WaitForSeconds(1.5f); var lo = player.loadout; lo.holo = lo.silencer = lo.grip = lo.laser = true; player.RefreshLoadout(); }
        if (autoWeapon >= 0 && player) { yield return new WaitForSeconds(1f); player.SendMessage("Switch", autoWeapon, SendMessageOptions.DontRequireReceiver); }
    }

    // ---------------------------------------------------------------- online
    public IEnumerator RunDedicatedServer()
    {
        Debug.Log("[Server] world ready, starting network");
        mode = Mode.Online;
        var boot = NetBoot.Create();
        if (!boot) { Debug.LogError("[Server] NetworkManager missing from scene"); yield break; }
        SpawnMatchVehicles();
        boot.StartServer();
        yield return null;
        var lobby = ParseArg("-lobby");
        if (!string.IsNullOrEmpty(lobby))
        {
            LobbyClient.Create().StartHeartbeat(lobby, NetBoot.Port, ParseArg("-name") ?? ("房间 " + NetBoot.Port),
                () => TdmMatch.I ? TdmMatch.I.PlayerCount : 0,
                () => TdmMatch.I && TdmMatch.I.PlayerCount > 0 ? "playing" : "waiting");
            Debug.Log("[Server] reporting to lobby " + lobby);
        }
        Debug.Log("[Server] team deathmatch running");
    }

    // test hook: lock onto the closest hostile networked player and keep shooting at him
    float autoFireT;
    void AutoFireTick()
    {
        if (!player || player.dead || NetPlayer.All.Count < 2) return;
        NetPlayer mine = player.net, best = null; float bd = 999;
        if (!mine) return;
        foreach (var np in NetPlayer.All)
        {
            if (!np || np == mine || !np.Alive || np.team.Value == mine.team.Value) continue;
            float d = Vector3.Distance(np.transform.position, player.transform.position);
            if (d < bd) { bd = d; best = np; }
        }
        if (!best) return;
        player.DebugAim(best.Eye);
        if (AutoAim) return;                        // watch the enemy without killing him
        autoFireT -= Time.deltaTime;
        if (autoFireT > 0) return;
        autoFireT = 0.35f;
        player.Fire();
    }

    public static string ParseArg(string key)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i].ToLower() == key) return a[i + 1];
        return null;
    }

    public void StartOnline(string ip, ushort port)
    {
        mode = Mode.Online; paused = false; gameOver = false;
        var boot = NetBoot.Create();
        var pgo = new GameObject("Player"); pgo.transform.position = World.OnGround(new Vector3(0, 0, -6)) + Vector3.up * 0.1f;
        player = pgo.AddComponent<Player>();
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        SpawnMatchVehicles();
        boot.StartClient(ip, port);
        Say("正在连接 " + ip + ":" + port, 3f);
    }

    // The same cars on every machine. An online match only syncs the seat and the pose, so the
    // spawn list has to be identical on both clients and on the server, which needs them for cover.
    public static void SpawnMatchVehicles()
    {
        Vehicle.Spawn("jeep", World.OnGround(new Vector3(-138, 0, -126)), 40);
        Vehicle.Spawn("pickup", World.OnGround(new Vector3(-162, 0, -106)), 100);
        Vehicle.Spawn("bike", World.OnGround(new Vector3(-150, 0, -138)), 10);
        Vehicle.Spawn("jeep", World.OnGround(new Vector3(138, 0, 126)), 220);
        Vehicle.Spawn("pickup", World.OnGround(new Vector3(162, 0, 106)), 280);
        Vehicle.Spawn("bike", World.OnGround(new Vector3(150, 0, 138)), 190);
        Vehicle.Spawn("heli", World.OnGround(new Vector3(-20, 0, -12)), 90);
        Vehicle.Spawn("heli", World.OnGround(new Vector3(20, 0, 12)), 270);
        NetVehicles.Reset();
    }

    public void StartMode(Mode m)
    {
        mode = m; paused = false; gameOver = false; won = false; wave = 0; score = 0; kills = 0; points = 0; lootValue = 0;
        var pgo = new GameObject("Player"); pgo.transform.position = World.OnGround(new Vector3(0, 0, -6)) + Vector3.up * 0.1f;
        player = pgo.AddComponent<Player>();
        Vehicle.Spawn("jeep", World.OnGround(new Vector3(7, 0, -3)), 30);
        Vehicle.Spawn("pickup", World.OnGround(new Vector3(-6, 0, 12)), 200);
        Vehicle.Spawn("bike", World.OnGround(new Vector3(4, 0, 14)), 120);
        Vehicle.Spawn("heli", World.OnGround(new Vector3(-20, 0, -12)), 90);
        Vehicle.Spawn("jeep", World.OnGround(World.Town + new Vector3(-12, 0, -8)), 0);
        scoreP = scoreE = 0; respawnT = 0;
        if (m == Mode.Endless) StartCoroutine(Waves());
        else if (m == Mode.Conquest) StartCoroutine(Conquest());
        else StartCoroutine(Extraction());
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        if (shotPath != null) shotAt = Time.realtimeSinceStartup + shotDelay;
    }
    float shotDelay = 10;

    // ------------------------------------------------------------------ lighting, time of day, weather, post fx
    void SetupLighting()
    {
        sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
        RenderSettings.sun = sun; var sky = Resources.Load<Material>("Mats/Sky"); if (sky) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight; RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        if (Headless) return;                        // everything below is cosmetic
        var dg = new GameObject("Dust"); dg.transform.SetParent(transform); dust = dg.AddComponent<ParticleSystem>(); dust.Stop();
        var mn = dust.main; mn.startLifetime = 3.2f; mn.startSpeed = new ParticleSystem.MinMaxCurve(7, 16); mn.startSize = new ParticleSystem.MinMaxCurve(1.5f, 6f); mn.maxParticles = 900;
        mn.startColor = new ParticleSystem.MinMaxGradient(new Color(0.88f, 0.75f, 0.55f, 0.30f), new Color(0.75f, 0.6f, 0.4f, 0.5f));
        mn.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); mn.simulationSpace = ParticleSystemSimulationSpace.World; mn.loop = true;
        var em = dust.emission; em.rateOverTime = 0; var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(70, 16, 70);
        var no = dust.noise; no.enabled = true; no.strength = 2.5f; no.frequency = 0.25f; no.scrollSpeed = 1.2f;
        var cl = dust.colorOverLifetime; cl.enabled = true; var cg = new Gradient();
        cg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(0, 1) }); cl.color = cg;
        var vel = dust.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(8, 14); vel.y = new ParticleSystem.MinMaxCurve(0, 0); vel.z = new ParticleSystem.MinMaxCurve(0, 0);
        var dpr = dg.GetComponent<ParticleSystemRenderer>(); dpr.material = Fx.SoftSprite; dpr.maxParticleSize = 0.5f; dust.Play();
    }

    void TickEnvironment(float dt)
    {
        if (Headless) { dayT = (dayT + dt / 480f) % 1f; return; }
        dayT = (dayT + dt / 480f) % 1f;     // 8-minute day
        float ang = dayT * 360 - 90;        // 0.25 = noon
        float elev = Mathf.Sin(dayT * Mathf.PI * 2 - Mathf.PI / 2 + Mathf.PI) * -1; // -1..1
        elev = Mathf.Sin((dayT - 0.0f) * Mathf.PI * 2);
        // night is short and never pitch black: you still have to be able to see a man at 40 m
        float dayK = Mathf.Clamp01(elev * 3f + 0.78f);
        night = dayK < 0.32f;
        sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(-10, 65, Mathf.Clamp01(elev)) + (elev < 0 ? 25 : 0), ang * 0.6f - 40, 0);
        sun.intensity = Mathf.Lerp(0.30f, 1.25f, dayK) * (1 - weatherK * 0.45f);      // moonlight floor
        sun.color = Color.Lerp(new Color(0.55f, 0.62f, 0.9f), Color.Lerp(new Color(1f, 0.62f, 0.38f), new Color(1f, 0.94f, 0.82f), Mathf.Clamp01(elev * 2)), dayK);
        RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.26f, 0.30f, 0.44f), new Color(0.62f, 0.7f, 0.82f), dayK);
        RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.24f, 0.24f, 0.30f), new Color(0.72f, 0.64f, 0.52f), dayK);
        RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.12f, 0.12f, 0.14f), new Color(0.38f, 0.3f, 0.22f), dayK);
        var fogDay = Color.Lerp(new Color(0.82f, 0.78f, 0.7f), new Color(0.78f, 0.6f, 0.4f), weatherK);
        RenderSettings.fogColor = Color.Lerp(new Color(0.05f, 0.06f, 0.1f), fogDay, dayK);
        // landmarks (the mountain, the town) have to stay visible across the map
        float clearEnd = Mathf.Lerp(150, 230, dayK) + quality * 60;
        RenderSettings.fogStartDistance = Mathf.Lerp(60, 8, weatherK); RenderSettings.fogEndDistance = Mathf.Lerp(clearEnd, 55, weatherK);
        if (Camera.main)
        {
            var mc = Camera.main; mc.farClipPlane = RenderSettings.fogEndDistance + 25;
            bool storming = weatherK > 0.35f;
            mc.clearFlags = storming ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (storming) mc.backgroundColor = Color.Lerp(RenderSettings.fogColor, new Color(0.62f, 0.5f, 0.32f), 0.5f);
        }
        if (RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure")) RenderSettings.skybox.SetFloat("_Exposure", Mathf.Lerp(0.3f, 1.2f, dayK));
        // sandstorm
        if (forceStorm) { stormTarget = 1; weatherTimer = 999; }
        weatherTimer -= dt; if (weatherTimer <= 0) { weatherTimer = Random.Range(90f, 180f); stormTarget = stormTarget > 0 ? 0 : (Random.value < 0.5f ? 1 : 0); if (stormTarget > 0) Say("沙尘暴来袭，能见度下降", 2.5f); }
        weatherK = Mathf.MoveTowards(weatherK, stormTarget, dt / 12f);
        var em = dust.emission; em.rateOverTime = weatherK * weatherK * 420;
        if (Camera.main) dust.transform.position = Camera.main.transform.position + Vector3.left * 22 + Vector3.up * 2;
        if (wind) wind.volume = (0.12f + weatherK * 0.6f) * Sfx.volume;
        // flashlight at night
        if (player && player.cam)
        {
            if (!flashlight) { flashlight = new GameObject("Flashlight").AddComponent<Light>(); flashlight.type = LightType.Spot; flashlight.spotAngle = 62; flashlight.range = 60; flashlight.shadows = LightShadows.None; flashlight.color = new Color(1, 0.96f, 0.88f); }
            flashlight.transform.SetPositionAndRotation(player.cam.transform.position + player.cam.transform.right * 0.2f, player.cam.transform.rotation);
            flashlight.intensity = Mathf.MoveTowards(flashlight.intensity, (torch ?? night) && !player.inVehicle ? 3.4f : 0, dt * 4);
        }
    }
    float stormTarget;

    void ApplyQuality()
    {
        // 0 low, 1 balanced (integrated GPUs), 2 high
        QualitySettings.shadowDistance = quality == 0 ? 20 : quality == 1 ? 30 : 70;
        QualitySettings.shadows = quality <= 1 ? ShadowQuality.HardOnly : ShadowQuality.All; QualitySettings.shadowCascades = quality <= 1 ? 1 : 2;
        var cam = Camera.main; if (cam) { var d = new float[32]; d[8] = quality == 0 ? 60 : quality == 1 ? 80 : 120; d[9] = quality == 0 ? 110 : quality == 1 ? 150 : 220; cam.layerCullDistances = d; cam.layerCullSpherical = true; }
        QualitySettings.shadowResolution = quality == 2 ? ShadowResolution.High : quality == 1 ? ShadowResolution.Low : ShadowResolution.Low;
        QualitySettings.antiAliasing = quality == 0 ? 0 : 2;
        if (World.terrain) { World.terrain.heightmapPixelError = quality == 0 ? 20 : quality == 1 ? 10 : 4; World.terrain.basemapDistance = quality == 0 ? 15 : quality == 1 ? 25 : 150; World.terrain.detailObjectDistance = quality == 0 ? 38 : quality == 1 ? 55 : 90; }
        QualitySettings.pixelLightCount = quality == 2 ? 3 : 1;
        if (volume && volume.profile)
        {
            if (volume.profile.TryGetSettings(out Bloom b)) { b.enabled.value = quality >= 2; b.fastMode.value = true; }
            if (volume.profile.TryGetSettings(out AmbientOcclusion ao)) ao.enabled.value = quality >= 2;
            if (volume.profile.TryGetSettings(out MotionBlur mb)) mb.enabled.value = false;
            if (volume.profile.TryGetSettings(out DepthOfField dof)) dof.enabled.value = false;
        }
        if (ppLayer) { ppLayer.antialiasingMode = quality == 2 ? PostProcessLayer.Antialiasing.FastApproximateAntialiasing : PostProcessLayer.Antialiasing.None; ppLayer.enabled = quality >= 1; }
    }

    // ------------------------------------------------------------------ enemies
    public void Noise(Vector3 pos, float radius) { foreach (var e in Enemy.All.ToArray()) if (e) e.Hear(pos, radius); }

    Squad SpawnSquad(Vector3 center, int n, int waveNum, bool boss = false)
    {
        var sq = new Squad();
        for (int i = 0; i < n; i++)
        {
            var p = center + new Vector3(Random.Range(-6f, 6f), 0, Random.Range(-6f, 6f));
            if (!NavMesh.SamplePosition(World.OnGround(p), out var h, 8, NavMesh.AllAreas)) continue;
            EType t = boss && i == 0 ? EType.Boss : RollType(waveNum, i);
            var e = new GameObject("Enemy_" + t).AddComponent<Enemy>(); e.transform.position = h.position; e.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            e.Init(t, sq, waveNum);
        }
        sq.Assign(); return sq;
    }

    EType RollType(int w, int i)
    {
        float r = Random.value;
        if (w >= 2 && r < 0.18f) return EType.Rusher; if (w >= 2 && r < 0.3f) return EType.Sniper; if (w >= 3 && r < 0.42f) return EType.Heavy;
        return EType.Rifle;
    }

    Vector3 SpawnPointAway(float min, float max)
    {
        for (int k = 0; k < 30; k++)
        {
            float a = Random.Range(0, Mathf.PI * 2), d = Random.Range(min, max);
            var p = player.transform.position + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if (Mathf.Abs(p.x) < 200 && Mathf.Abs(p.z) < 200) return World.OnGround(p);
        }
        return World.OnGround(new Vector3(60, 0, 60));
    }

    bool noEnemies;
    IEnumerator Waves()
    {
        yield return new WaitForSeconds(3f);
        while (noEnemies) yield return null;
        while (!gameOver)
        {
            wave++; Say("第 " + wave + " 波", 2.5f);
            int squads = 1 + wave / 3;
            for (int s = 0; s < squads; s++) { var sq = SpawnSquad(SpawnPointAway(55, 90), 2 + Mathf.Min(wave / 2, 3), wave, wave % 5 == 0 && s == 0); sq.Report(player.transform.position, false); yield return new WaitForSeconds(1.5f); }
            if (wave >= 4 && wave % 2 == 0) { Vehicle.Spawn("tank", SpawnPointAway(90, 120), 0); Feed("！ 敌方坦克出现"); }
            while (Enemy.All.Exists(e => e && e.alive) || Vehicle.All.Exists(v => v.enemy && !v.dead)) yield return new WaitForSeconds(1f);
            points += 300 + wave * 100; Say("本波清除！+" + (300 + wave * 100) + " 积分  按 B 打开商店", 3f);
            // supply drop near the player
            for (int i = 0; i < 3; i++) Loot.Drop(World.OnGround(player.transform.position + Random.insideUnitSphere * 6) + Vector3.up * 0.3f, i == 0 ? LootKind.Ammo : i == 1 ? LootKind.Med : LootKind.Armor);
            yield return new WaitForSeconds(8f);
        }
    }

    // ---------------------------------------------------------------- G. conquest
    public void OnCaptured(CapturePoint cp, int oldOwner)
    {
        if (cp.owner == 0) { Say("已占领 " + cp.label + "！", 2.5f); Feed("★ 占领 " + cp.label); Sfx.Ui(Sfx.Raw("pickup"), 0.9f); }
        else { Say("！ " + cp.label + " 被敌军夺走", 2.5f); Feed("！ 失去 " + cp.label); }
    }

    CapturePoint NearestPoint(Vector3 from, int owner)
    {
        CapturePoint best = null; float bd = 1e9f;
        foreach (var cp in CapturePoint.All) { if (cp.owner != owner) continue; float d = Vector3.Distance(cp.transform.position, from); if (d < bd) { bd = d; best = cp; } }
        return best;
    }

    IEnumerator Conquest()
    {
        foreach (var t in new[] { (World.Town, "城镇"), (World.Oasis, "绿洲"), (World.Ruins, "遗迹"), (World.Canyon, "峡谷"), (World.Village1, "北村") })
            CapturePoint.Create(t.Item1, t.Item2);
        Say("夺取并守住据点：走进旗杆周围的圈内开始占领，先得 300 分获胜", 5f);
        // opening positions: the nearest point is ours, the two farthest are theirs, the rest neutral
        var sorted = new List<CapturePoint>(CapturePoint.All);
        sorted.Sort((a, b) => Vector3.Distance(a.transform.position, player.transform.position).CompareTo(Vector3.Distance(b.transform.position, player.transform.position)));
        sorted[0].owner = 0;
        for (int i = sorted.Count - 2; i < sorted.Count; i++) sorted[i].owner = 1;
        // garrisons stand outside the ring and walk in, so nothing is captured in the first seconds
        for (int i = 1; i < sorted.Count; i++)
        {
            var cp = sorted[i];
            var sq = SpawnSquad(cp.transform.position + new Vector3(Random.Range(-28f, 28f), 0, Random.Range(-28f, 28f)), i >= sorted.Count - 2 ? 3 : 2, 2);
            sq.objective = cp.transform.position; sq.hasObjective = true;
            yield return new WaitForSeconds(0.4f);
        }
        float atkT = 70;
        while (!gameOver)
        {
            yield return null;
            if (paused) continue;
            float dt = Time.deltaTime;
            int mine = 0, theirs = 0;
            foreach (var cp in CapturePoint.All) { cp.Tick(dt); if (cp.owner == 0) mine++; else if (cp.owner == 1) theirs++; }
            scoreP += mine * dt; scoreE += theirs * dt; score = Mathf.FloorToInt(scoreP);
            if (scoreP >= 300) { End(true, "据点战胜利！得分 " + (int)scoreP + " · 击杀 " + kills); yield break; }
            if (scoreE >= 300) { End(false, "敌军控制了战场 · 击杀 " + kills); yield break; }
            atkT -= dt;
            if (atkT <= 0)
            {
                atkT = Random.Range(65f, 95f);
                var target = NearestPoint(player.transform.position, 0) ?? NearestPoint(player.transform.position, -1);
                var sq = SpawnSquad(SpawnPointAway(60, 100), 3, 3 + Mathf.FloorToInt(scoreP / 80));
                sq.objective = target ? target.transform.position : player.transform.position; sq.hasObjective = true;
                Feed("！ 敌军小队正在进攻 " + (target ? target.label : "你的位置"));
            }
        }
    }

    IEnumerator LeanDebug()
    {
        yield return new WaitForSeconds(1.2f);
        var v = Vehicle.All.Find(x => x.kind == "pickup");
        if (v && player) { player.transform.position = v.transform.position + Vector3.right * 2; player.TryEnterVehicle(); yield return null; player.SendMessage("ForceLean", SendMessageOptions.DontRequireReceiver); }
    }

    IEnumerator Respawn(CapturePoint cp)
    {
        respawnT = 6;
        while (respawnT > 0 && !gameOver) { respawnT -= Time.unscaledDeltaTime; yield return null; }
        if (gameOver) yield break;
        player.Revive(World.OnGround(cp.transform.position + new Vector3(Random.Range(-5f, 5f), 0, Random.Range(-5f, 5f))) + Vector3.up * 0.3f);
        Say("在 " + cp.label + " 重新投入战斗", 2f);
    }

    IEnumerator Extraction()
    {
        timeLeft = 15 * 60;
        var w = World.I;
        foreach (var s in w.lootSpots) { float r = Random.value; Loot.Drop(s, r < 0.55f ? LootKind.Valuable : r < 0.72f ? LootKind.Attachment : r < 0.86f ? LootKind.Med : LootKind.Ammo); }
        foreach (var c in new[] { World.Town, World.Oasis, World.Ruins, World.Canyon }) { SpawnSquad(c + new Vector3(8, 0, 8), 3, 2); SpawnSquad(c + new Vector3(-10, 0, -6), 2, 2); }
        Say("搜刮物资，然后去撤离点（地图上的绿色标记）", 4);
        reinforceT = 120;
        while (!gameOver)
        {
            yield return null;
            timeLeft -= Time.deltaTime; reinforceT -= Time.deltaTime;
            if (reinforceT <= 0) { reinforceT = 110; var sq = SpawnSquad(SpawnPointAway(70, 110), 3, 3); sq.Report(player.transform.position, false); Feed("！ 敌方增援正在赶来"); }
            if (timeLeft <= 0) { End(false, "时间耗尽，没能撤离"); yield break; }
            bool inZone = false; foreach (var e in w.extracts) if (Vector3.Distance(new Vector3(e.x, 0, e.z), new Vector3(player.transform.position.x, 0, player.transform.position.z)) < 8) inZone = true;
            extractHold = inZone ? extractHold + Time.deltaTime : 0;
            if (extractHold >= 10) { End(true, "撤离成功！带出物资价值 $" + lootValue + " · 击杀 " + kills); yield break; }
        }
    }

    public void OnKill(Enemy e, bool head)
    {
        kills++; int pts = e.type == EType.Boss ? 1000 : head ? 150 : 100; score += pts; points += pts;
        Feed((head ? "爆头击杀 " : "击杀 ") + (e.type == EType.Boss ? "Boss" : e.type == EType.Sniper ? "狙击手" : e.type == EType.Heavy ? "重甲兵" : e.type == EType.Rusher ? "突击兵" : "步枪兵") + " +" + pts);
    }
    public void OnTankDestroyed(Vehicle v) { score += 800; points += 800; Say("摧毁坦克 +800", 2); }
    public void OnStructureDestroyed(Destructible d) { }
    public void OnPlayerDied()
    {
        if (mode == Mode.Conquest)
        {
            var cp = NearestPoint(player.transform.position, 0);
            if (cp) { StartCoroutine(Respawn(cp)); return; }
            End(false, "所有据点失守 · 击杀 " + kills); return;
        }
        End(false, mode == Mode.Extraction ? "阵亡，丢失全部物资" : "阵亡 · 坚持到第 " + wave + " 波 · 得分 " + score);
    }

    void End(bool win, string text)
    {
        gameOver = true; won = win; endText = text; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        string key = mode == Mode.Endless ? "best_endless" : mode == Mode.Conquest ? "best_conquest" : "best_extract"; int val = mode == Mode.Endless || mode == Mode.Conquest ? score : (win ? lootValue : 0);
        if (val > PlayerPrefs.GetInt(key, 0)) { PlayerPrefs.SetInt(key, val); endText += "  · 新纪录！"; }
    }

    // simple navigation: N cycles through the named places (and the extraction points)
    public void NextWaypoint()
    {
        var list = new List<(Vector3 p, string n)>(World.Marks);
        if (mode == Mode.Extraction) foreach (var e in World.I.extracts) list.Add((e, "撤离点"));
        wpIndex = (wpIndex + 1) % (list.Count + 1);
        if (wpIndex == list.Count) { wpName = ""; Say("已取消导航目标"); return; }
        wp = list[wpIndex].p; wpName = list[wpIndex].n;
        Say("导航目标：" + wpName + "  " + Mathf.RoundToInt(Vector3.Distance(wp, player ? player.transform.position : Vector3.zero)) + " m");
    }

    public void SetWaypoint(Vector3 p, string n) { wp = p; wpName = n; Say("导航目标：" + n); }

    public void Say(string m, float t = 2f) { message = m; messageT = t; }
    public void Feed(string m) { feed.Insert(0, (m, Time.time)); if (feed.Count > 5) feed.RemoveAt(5); }

    // ------------------------------------------------------------------ loop
    // -netdebug: report what this machine thinks the other players are doing
    float netProbeT;
    void NetProbe()
    {
        if (!NetDebug || Time.time < netProbeT) return;
        netProbeT = Time.time + 3f;
        var mine = player ? player.net : null;
        float d = -1; int others = 0;
        foreach (var np in NetPlayer.All)
        {
            if (!np || np == mine) continue;
            others++;
            if (player) d = Vector3.Distance(np.transform.position, player.transform.position);
        }
        Debug.Log("[Net] t=" + Time.time.ToString("0") + " others=" + others + " dist=" + d.ToString("0") +
                  " me=" + (player ? player.transform.position.ToString("0") : "?") + " veh=" + (player && player.inVehicle));
    }

    void LateUpdate()
    {
        NetProbe();
        NetVehicles.Tick(player);
        if (AutoFire) AutoFireTick();
        if (driveTest && player && player.inVehicle && player.Vehicle)      // test hook: roll forward
        {
            var t = player.Vehicle.transform;
            var np = t.position + t.forward * 9f * Time.deltaTime;
            t.position = new Vector3(np.x, World.Height(np) + 0.4f, np.z);
            player.transform.position = t.position;
        }
        if (watchVeh && player) player.DebugAim(watchVeh.transform.position + Vector3.up);
    }

    bool driveTest; Vehicle watchVeh;

    IEnumerator VehicleTest(bool drive)
    {
        float t0 = Time.time;                                  // the player only exists once the match is joined
        while (!player && Time.time - t0 < 40f) yield return null;
        if (!player) yield break;
        yield return new WaitForSeconds(5f);
        var v = Vehicle.All.Find(x => x.kind == "pickup");
        if (NetDebug) Debug.Log("[Net] vehicle test drive=" + drive + " found=" + (v != null) + " index=" + NetVehicles.IndexOf(v));
        if (!v) yield break;
        if (drive)
        {
            player.transform.position = v.transform.position + v.transform.right * 2f;
            yield return null;
            player.TryEnterVehicle(); driveTest = true;
        }
        else
        {
            player.transform.position = World.OnGround(v.transform.position + v.transform.right * 16f) + Vector3.up * 0.4f;
            watchVeh = v;
        }
    }

    void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        fpsAcc += Time.unscaledDeltaTime; fpsN++; if (fpsAcc >= 0.5f) { fps = fpsN / fpsAcc; fpsAcc = 0; fpsN = 0; }
        messageT -= Time.deltaTime;
        if (mode != Mode.Menu && !gameOver && Input.GetKeyDown(KeyCode.Escape)) { paused = !paused; showShop = false; Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = paused; Time.timeScale = paused ? 0 : 1; }
        if (mode == Mode.Endless && !gameOver && Input.GetKeyDown(KeyCode.B)) { showShop = !showShop; paused = showShop; Time.timeScale = showShop ? 0 : 1; Cursor.lockState = showShop ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = showShop; }
        if (Input.GetKeyDown(KeyCode.H) && !showShop) showHelp = !showHelp;
        if (Input.GetKeyDown(KeyCode.M) && mode != Mode.Menu) { showMap = !showMap; Cursor.lockState = showMap ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = showMap; }
        if (Input.GetKeyDown(KeyCode.N) && mode != Mode.Menu) NextWaypoint();
        if (Input.GetKeyDown(KeyCode.L) && mode != Mode.Menu) { torch = !(torch ?? night); Say(torch.Value ? "手电 开" : "手电 关", 1.2f); }
        if (Input.GetKeyDown(KeyCode.Tab) && mode != Mode.Menu) showBag = !showBag;

        if (Input.GetKeyDown(KeyCode.F3)) { quality = (quality + 1) % 3; ApplyQuality(); PlayerPrefs.SetInt("quality_v2", quality); Say("画质：" + new[] { "流畅", "均衡", "高清" }[quality]); }
        if (Input.GetKeyDown(KeyCode.F5)) { Tune.Load(); if (player) { player.ApplyTune(); } Say("已重新读取 tune.json"); }
        if (!paused) TickEnvironment(Time.deltaTime);
        if (mode == Mode.Menu && Camera.main) { float t = Time.time * 0.05f; Camera.main.transform.position = new Vector3(Mathf.Sin(t) * 40, 14, Mathf.Cos(t) * 40); Camera.main.transform.LookAt(new Vector3(0, 2, 0)); }
        if (shotAt > 0 && Time.realtimeSinceStartup > shotAt) { shotAt = -1; if (autoPose != null) ApplyPose(autoPose); StartCoroutine(Shot()); }
    }

    void ApplyPose(string pose) { if (!player) return; var s = pose.Split(','); var inv = System.Globalization.CultureInfo.InvariantCulture; player.GetComponent<CharacterController>().enabled = false; player.transform.position = new Vector3(float.Parse(s[0], inv), float.Parse(s[1], inv), float.Parse(s[2], inv)); player.SendMessage("SetYaw", float.Parse(s[3], inv), SendMessageOptions.DontRequireReceiver); player.GetComponent<CharacterController>().enabled = true; }

    IEnumerator Shot()
    {
        yield return new WaitForSecondsRealtime(1.2f);
        if (autoBoom && player) { Fx.Explosion(player.transform.position + player.transform.forward * 9 + Vector3.up * 0.5f, 6); player.Fire(); yield return new WaitForSecondsRealtime(0.12f); }
        ScreenCapture.CaptureScreenshot(shotPath);
        yield return new WaitForSecondsRealtime(1.5f);
        FrameTimingManager.CaptureFrameTimings(); var ft = new FrameTiming[8]; uint n = FrameTimingManager.GetLatestTimings(8, ft); double cpu = 0, gpu = 0; for (int i = 0; i < n; i++) { cpu += ft[i].cpuFrameTime; gpu += ft[i].gpuFrameTime; } if (n > 0) { cpu /= n; gpu /= n; }
        System.IO.File.WriteAllText(shotPath + ".txt", "cpu=" + cpu.ToString("0.0") + "ms gpu=" + gpu.ToString("0.0") + "ms draw=" + UnityEngine.Rendering.OnDemandRendering.renderFrameInterval + " fps=" + fps.ToString("0.0") + " res=" + Screen.width + "x" + Screen.height + " enemies=" + Enemy.All.Count + " gpu=" + SystemInfo.graphicsDeviceName + " quality=" + quality);
        Application.Quit();
    }

    // ------------------------------------------------------------------ HUD
    GUIStyle big, mid, small, center, btn;
    void Styles()
    {
        if (big != null) return;
        big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold }; mid = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold }; center = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter };
        btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, fixedHeight = 48 };
    }

    void OnGUI()
    {
        Styles(); float W = Screen.width, H = Screen.height;
        if (mode == Mode.Menu) { Menu(W, H); return; }
        var p = player; if (!p) return;
        if (p.AdsK > 0.05f && !p.ScopeOverlay) { GUI.color = new Color(1, 1, 1, p.AdsK * 0.75f); GUI.DrawTexture(new Rect(0, 0, W, H), Vig()); GUI.color = Color.white; }
        if (p.ScopeOverlay) ScopeMask(W, H);
        if (p.hurtFlash > 0) Fill(new Rect(0, 0, W, H), new Color(0.7f, 0.05f, 0.05f, p.hurtFlash * 0.35f));
        if (weatherK > 0.05f)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.72f, 0.55f, 0.32f, weatherK * 0.2f));
            GUI.color = new Color(1, 1, 1, weatherK * 0.5f); GUI.DrawTexture(new Rect(0, 0, W, H), Vig()); GUI.color = Color.white;
        }
        // vitals
        Label(new Rect(24, H - 86, 300, 30), "生命 " + Mathf.CeilToInt(p.hp) + (p.armor > 0 ? "   护甲 " + Mathf.CeilToInt(p.armor) : ""), small);
        Bar(new Rect(24, H - 60, 240, 10), p.hp / 100, p.hp < 30 ? new Color(0.85f, 0.25f, 0.2f) : new Color(0.3f, 0.75f, 0.65f));
        if (p.armor > 0) Bar(new Rect(24, H - 46, 240, 6), p.armor / 100, new Color(0.4f, 0.65f, 0.95f));
        var wd = p.W;
        Label(new Rect(W - 330, H - 100, 300, 50), p.reloading ? "换弹中" : p.ammo[p.weapon] + " / " + p.reserve[p.weapon], new GUIStyle(big) { alignment = TextAnchor.UpperRight });
        Label(new Rect(W - 330, H - 52, 300, 30), wd.name + "   手雷 ×" + p.grenades, new GUIStyle(small) { alignment = TextAnchor.UpperRight });
        // top: mode info + compass
        if (mode == Mode.Online && TdmMatch.I)
        {
            var m2 = TdmMatch.I;
            string line = "蓝队 " + m2.scoreA.Value + "  :  " + m2.scoreB.Value + " 红队    " +
                          Mathf.FloorToInt(m2.timeLeft.Value / 60) + ":" + Mathf.FloorToInt(m2.timeLeft.Value % 60).ToString("00") +
                          "    （先到 " + TdmMatch.KillTarget + " 杀获胜）";
            Label(new Rect(W / 2 - 350, 44, 700, 30), line, new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
            if (m2.over.Value)
                Label(new Rect(0, H * 0.34f, W, 50), m2.winner.Value < 0 ? "平局" : (m2.winner.Value == 0 ? "蓝队获胜" : "红队获胜"), center);
        }
        string top = mode == Mode.Online ? "" : mode == Mode.Endless ? "第 " + wave + " 波 · 敌人 " + Enemy.All.FindAll(e => e && e.alive).Count + " · 得分 " + score + " · 积分 " + points
                    : mode == Mode.Conquest ? "我方 " + (int)scoreP + " : " + (int)scoreE + " 敌方 （先到 300 分获胜）· 击杀 " + kills
                    : "剩余 " + Mathf.FloorToInt(timeLeft / 60) + ":" + Mathf.FloorToInt(timeLeft % 60).ToString("00") + " · 物资 $" + lootValue + " · 击杀 " + kills;
        Label(new Rect(W / 2 - 350, 44, 700, 30), top, new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        Compass(W);
        Minimap(W, H);
        // every enemy inside the warning range gets a distance tag pinned to them;
        // solid when you have line of sight, faint when they are behind cover.
        if (p.cam && !p.dead)
        {
            const float warnRange = 70f;
            var cam = p.cam; var eye = cam.transform.position;
            var tagged = new List<(float d, Vector3 pos, bool seen)>();
            foreach (var e in Enemy.All)
            {
                if (!e || !e.alive) continue;
                var head = e.transform.position + Vector3.up * 1.7f;
                float d = Vector3.Distance(head, eye); if (d > warnRange) continue;
                bool seen = !Physics.Linecast(eye, head, out var oc, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore) || oc.collider.GetComponentInParent<Enemy>() == e;
                tagged.Add((d, head, seen));
            }
            tagged.Sort((u, v2) => u.d.CompareTo(v2.d));
            int shown = 0;
            foreach (var t in tagged)
            {
                if (shown++ >= 8) break;
                var sp = cam.WorldToScreenPoint(t.pos);
                float x = sp.x, y = H - sp.y;
                bool onScreen = sp.z > 1 && x >= 0 && x <= W && y >= 0 && y <= H;
                if (!onScreen)
                {
                    // out of view: pin a small arrow with the distance to the screen edge
                    var d2 = t.pos - p.transform.position; d2.y = 0;
                    float ang = Mathf.DeltaAngle(cam.transform.eulerAngles.y, Mathf.Atan2(d2.x, d2.z) * Mathf.Rad2Deg);
                    var ec = t.d < 20 ? new Color(1f, 0.35f, 0.3f, 0.9f) : new Color(1f, 0.72f, 0.35f, 0.75f);
                    var dir2 = new Vector2(Mathf.Sin(ang * Mathf.Deg2Rad), -Mathf.Cos(ang * Mathf.Deg2Rad));
                    var e2 = new Vector2(W / 2, H / 2) + dir2 * Mathf.Min(W, H) * 0.38f;
                    Marker(e2, ang, ec, 12);
                    var pv = GUI.color; GUI.color = ec;
                    Label(new Rect(e2.x - 40, e2.y + 10, 80, 18), Mathf.RoundToInt(t.d) + " m", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
                    GUI.color = pv;
                    continue;
                }
                var col = t.d < 20 ? new Color(1f, 0.35f, 0.3f) : t.d < 45 ? new Color(1f, 0.72f, 0.35f) : new Color(0.95f, 0.9f, 0.8f);
                col.a = t.seen ? 1f : 0.45f;
                var prev = GUI.color; GUI.color = col;
                if (t.seen) Diamond(new Vector2(x, y - 14), col, 4.5f);
                Label(new Rect(x - 50, y - 6, 100, 20), Mathf.RoundToInt(t.d) + " m",
                      new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = t.d < 25 ? 15 : 13 });
                GUI.color = prev;
            }
        }
        if (wpName != "")
        {
            var d = wp - p.transform.position; float ang = Mathf.DeltaAngle(p.cam ? p.cam.transform.eulerAngles.y : 0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            string arrow = Mathf.Abs(ang) < 12 ? "↑ 正前方" : ang > 0 ? "→ 右转 " + Mathf.RoundToInt(ang) + "°" : "← 左转 " + Mathf.RoundToInt(-ang) + "°";
            Label(new Rect(W / 2 - 250, H * 0.58f, 500, 24), "目标 " + wpName + "  " + Mathf.RoundToInt(new Vector2(d.x, d.z).magnitude) + " m   " + arrow,
                  new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });
        }
        else Label(new Rect(W / 2 - 250, H * 0.58f, 500, 20), "按 N 选择导航目标", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
        // crosshair / red dot / hit marker
        if ((!p.inVehicle || p.leanOut) && !p.ads) { Fill(new Rect(W / 2 - 1, H / 2 - 9, 2, 6), Color.white); Fill(new Rect(W / 2 - 1, H / 2 + 3, 2, 6), Color.white); Fill(new Rect(W / 2 - 9, H / 2 - 1, 6, 2), Color.white); Fill(new Rect(W / 2 + 3, H / 2 - 1, 6, 2), Color.white); }
        if (p.inVehicle && !p.leanOut)
        {   // mounted-gun reticle: open circle with tick marks, never a plain white block
            bool onTarget = false; float tdist = 0;
            if (p.cam && Physics.Raycast(p.cam.transform.position, p.cam.transform.forward, out var vh, 250, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore))
            { tdist = vh.distance; onTarget = vh.collider.GetComponentInParent<Enemy>(); }
            var cc2 = onTarget ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(1f, 0.85f, 0.5f, 0.9f); float rr = onTarget ? 18 : 14;
            for (int i = 0; i < 28; i++) { float aa = i / 28f * Mathf.PI * 2; Fill(new Rect(W / 2 + Mathf.Cos(aa) * rr - 1, H / 2 + Mathf.Sin(aa) * rr - 1, 2, 2), cc2); }
            Fill(new Rect(W / 2 - 1, H / 2 - rr - 7, 2, 6), cc2); Fill(new Rect(W / 2 - 1, H / 2 + rr + 1, 2, 6), cc2);
            Fill(new Rect(W / 2 - rr - 7, H / 2 - 1, 6, 2), cc2); Fill(new Rect(W / 2 + rr + 1, H / 2 - 1, 6, 2), cc2);
            Fill(new Rect(W / 2 - 1, H / 2 - 1, 2, 2), cc2);
            if (tdist > 0) Label(new Rect(W / 2 - 60, H / 2 + rr + 10, 120, 18), Mathf.RoundToInt(tdist) + " m", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
        }
        if (p.RedDot) { Fill(new Rect(W / 2 - 2, H / 2 - 2, 4, 4), new Color(1, 0.15f, 0.1f)); }
        if (p.RpgSight) RocketSight(W, H);
        if (p.hitMarker > 0) { var c = new Color(1, 0.35f, 0.25f, p.hitMarker * 4); Fill(new Rect(W / 2 - 12, H / 2 - 12, 7, 2), c); Fill(new Rect(W / 2 + 5, H / 2 - 12, 7, 2), c); Fill(new Rect(W / 2 - 12, H / 2 + 10, 7, 2), c); Fill(new Rect(W / 2 + 5, H / 2 + 10, 7, 2), c); }
        // damage direction
        if (Time.time - p.lastHurtT < 1.4f && p.cam)
        {
            var d = p.lastHurtFrom - p.transform.position; float a = Vector3.SignedAngle(p.cam.transform.forward, new Vector3(d.x, 0, d.z), Vector3.up);
            float fade = Mathf.Clamp01(1.4f - (Time.time - p.lastHurtT)), r = H * 0.17f;
            for (int i = -4; i <= 4; i++)
            {
                float k = 1 - Mathf.Abs(i) / 5f; var m = GUI.matrix; GUIUtility.RotateAroundPivot(a + i * 4.2f, new Vector2(W / 2, H / 2));
                Fill(new Rect(W / 2 - 4, H / 2 - r - k * 5, 8, 3 + k * 5), new Color(0.95f, 0.25f, 0.18f, fade * k * 0.85f)); GUI.matrix = m;
            }
        }
        if (!string.IsNullOrEmpty(p.hint)) Label(new Rect(W / 2 - 400, H * 0.66f, 800, 30), p.hint, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        if (extractHold > 0 && !gameOver) Label(new Rect(0, H * 0.4f, W, 40), "撤离中… " + (10 - extractHold).ToString("0.0") + " 秒", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter });
        for (int i = 0; i < feed.Count; i++) { float age = Time.time - feed[i].Item2; if (age < 5) Label(new Rect(W - 420, 90 + i * 24, 400, 24), feed[i].Item1, new GUIStyle(small) { alignment = TextAnchor.UpperRight, fontSize = 14 }); }
        if (messageT > 0) Label(new Rect(0, H * 0.26f, W, 50), message, center);
        Label(new Rect(W / 2 - 250, H - 26, 500, 22), Mathf.RoundToInt(fps) + " FPS · " + new[] { "流畅", "均衡", "高清" }[Mathf.Clamp(quality, 0, 2)] + " (F3) · H 操作说明 · M 地图", new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter });
        if (mode == Mode.Conquest) ConquestHud(W, H);
        if (showBag) BagPanel(W, H);
        if (showMap) BigMap(W, H);
        if (showHelp) Help(W, H);
        if (showShop) Shop(W, H);
        else if (paused) Pause(W, H);
        if (gameOver) EndScreen(W, H);
    }

    Texture2D uiBg, uiEndless, uiExtract, uiConquest;
    Texture2D UiTex(ref Texture2D slot, string name) { if (!slot) slot = Resources.Load<Texture2D>("UI/" + name); return slot; }

    // one mode card: artwork, title, blurb, best result; returns true when clicked
    bool ModeCard(Rect r, Texture2D art, string title, string blurb, string best)
    {
        bool hot = r.Contains(Event.current.mousePosition);
        Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), hot ? new Color(0.95f, 0.85f, 0.55f, 0.95f) : new Color(0.75f, 0.72f, 0.6f, 0.35f));
        Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.95f));
        if (art) { GUI.color = hot ? Color.white : new Color(0.82f, 0.82f, 0.82f); GUI.DrawTexture(new Rect(r.x, r.y, r.width, r.height * 0.62f), art, ScaleMode.ScaleAndCrop); GUI.color = Color.white; }
        Fill(new Rect(r.x, r.y + r.height * 0.62f - 44, r.width, 44), new Color(0.04f, 0.04f, 0.05f, 0.72f));
        Label(new Rect(r.x + 16, r.y + r.height * 0.62f - 40, r.width - 32, 34), title, new GUIStyle(mid) { fontSize = 22 });
        Label(new Rect(r.x + 16, r.y + r.height * 0.62f + 12, r.width - 32, 60), blurb, new GUIStyle(small) { fontSize = 14, wordWrap = true });
        Label(new Rect(r.x + 16, r.yMax - 32, r.width - 32, 24), best, new GUIStyle(small) { fontSize = 13, alignment = TextAnchor.MiddleLeft });
        Label(new Rect(r.x, r.yMax - 32, r.width - 16, 24), hot ? "开始 ▶" : "", new GUIStyle(small) { fontSize = 15, alignment = TextAnchor.MiddleRight });
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void Menu(float W, float H)
    {
        var bg = UiTex(ref uiBg, "menu_bg");
        if (bg) { GUI.color = new Color(1, 1, 1, 0.92f); GUI.DrawTexture(new Rect(0, 0, W, H), bg, ScaleMode.ScaleAndCrop); GUI.color = Color.white; }
        Fill(new Rect(0, 0, W, H), new Color(0.04f, 0.04f, 0.06f, 0.3f));
        Fill(new Rect(0, 0, W, H * 0.3f), new Color(0.02f, 0.02f, 0.04f, 0.45f));

        Label(new Rect(0, H * 0.08f, W, 110), "沙 场", new GUIStyle(big) { fontSize = 86, alignment = TextAnchor.MiddleCenter });
        Fill(new Rect(W / 2 - 170, H * 0.08f + 104, 340, 2), new Color(0.9f, 0.82f, 0.55f, 0.8f));
        Label(new Rect(0, H * 0.08f + 112, W, 30), "SHACHANG · 沙漠战术射击 · Unity 客户端", new GUIStyle(small) { fontSize = 16, alignment = TextAnchor.MiddleCenter });

        float cw = Mathf.Min(360, (W - 140) / 3f), ch = cw * 0.92f, gap = 24;
        float x0 = W / 2 - (cw * 3 + gap * 2) / 2, y0 = H * 0.36f;
        if (ModeCard(new Rect(x0, y0, cw, ch), UiTex(ref uiEndless, "card_endless"), "无尽模式",
                     "一波波的敌人、Boss、商店与补给空投，看你能撑到第几波。", "最高得分 " + PlayerPrefs.GetInt("best_endless", 0))) StartMode(Mode.Endless);
        if (ModeCard(new Rect(x0 + cw + gap, y0, cw, ch), UiTex(ref uiExtract, "card_extract"), "撤离模式",
                     "15 分钟内搜刮物资，再活着走到撤离点，死了什么都带不走。", "最高带出 $" + PlayerPrefs.GetInt("best_extract", 0))) StartMode(Mode.Extraction);
        if (ModeCard(new Rect(x0 + (cw + gap) * 2, y0, cw, ch), UiTex(ref uiConquest, "card_conquest"), "据点占领",
                     "五个据点，站圈夺旗、守住计分，先拿 300 分的一方获胜。", "最高得分 " + PlayerPrefs.GetInt("best_conquest", 0))) StartMode(Mode.Conquest);

        float by = y0 + ch + 34;
        if (GUI.Button(new Rect(W / 2 - 250, by - 58, 500, 46), "联机对战 · 团队死斗（测试）", btn)) showJoin = !showJoin;
        if (showJoin)
        {
            var jr = new Rect(W / 2 - 230, H * 0.28f, 460, 340); Fill(jr, new Color(0.05f, 0.05f, 0.07f, 0.95f));
            var lob = LobbyClient.Create();
            Label(new Rect(jr.x, jr.y + 6, jr.width, 22), "房间列表", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });
            lobbyUrl = GUI.TextField(new Rect(jr.x + 16, jr.y + 32, 300, 28), lobbyUrl, 60);
            if (GUI.Button(new Rect(jr.x + 324, jr.y + 32, 100, 28), "刷新"))
            {
                LobbyClient.LobbyUrl = lobbyUrl.Trim(); lob.Refresh();
            }
            float ry = jr.y + 68;
            foreach (var r in lob.rooms)
            {
                if (GUI.Button(new Rect(jr.x + 16, ry, 408, 32), r.Line)) StartOnline(r.host, (ushort)r.port);
                ry += 36;
                if (ry > jr.yMax - 96) break;
            }
            Label(new Rect(jr.x + 16, jr.yMax - 92, 408, 20), lob.status, new GUIStyle(small) { fontSize = 12 });
            Label(new Rect(jr.x + 16, jr.yMax - 70, 408, 20), "或直接输入地址：", new GUIStyle(small) { fontSize = 12 });
            joinIp = GUI.TextField(new Rect(jr.x + 16, jr.yMax - 48, 240, 28), joinIp, 40);
            joinPort = GUI.TextField(new Rect(jr.x + 262, jr.yMax - 48, 70, 28), joinPort, 6);
            if (GUI.Button(new Rect(jr.x + 340, jr.yMax - 48, 84, 28), "连接"))
            {
                ushort.TryParse(joinPort, out ushort pt); if (pt == 0) pt = 7777;
                StartOnline(joinIp.Trim(), pt);
            }
            return;
        }
        if (GUI.Button(new Rect(W / 2 - 250, by, 240, 46), "操作说明 (H)", btn)) showHelp = !showHelp;
        if (GUI.Button(new Rect(W / 2 + 10, by, 240, 46), "退出游戏", btn)) Application.Quit();
        Label(new Rect(0, H - 34, W, 24), "移动 WASD · 冲刺 Shift · 开镜 右键 · 地图 M · 背包 Tab · 画质 F3",
              new GUIStyle(small) { fontSize = 13, alignment = TextAnchor.MiddleCenter });
        if (showHelp) Help(W, H);
    }

    void Help(float W, float H)
    {
        var r = new Rect(W / 2 - 330, H / 2 - 250, 660, 500); Fill(r, new Color(0.06f, 0.05f, 0.08f, 0.93f));
        string t = "移动 WASD · 冲刺 Shift · 跳 空格 · 蹲 C · 冲刺时按 C 滑铲\n探头 Q / E · 开火 左键 · 开镜 右键 · 换弹 R\n武器 1-5 或滚轮 · 手雷 G · 近战 V（背后偷袭一击必杀）\n上下载具 / 拾取 F · 暂停 Esc · 商店 B（无尽模式）大地图 M（点击地名导航）· N 切换导航目标 · Tab 背包 · L 手电\n急救包 X · 护甲板 Z · 弹药箱在换弹时自动拆开\n\n直升机：空格 上升 · C 下降 · WASD 飞 · 鼠标 转向 · 左键 机枪\n\n木箱、木栅栏能被打穿、打碎；红色油桶会爆炸\n敌人会听见枪声和脚步声：装消音器更容易偷袭\n\nF3 切换画质 · M 打开大地图 · H 关闭说明";
        GUI.Label(new Rect(r.x + 28, r.y + 24, r.width - 56, r.height - 48), t, new GUIStyle(small) { fontSize = 17, wordWrap = true });
    }

    void Pause(float W, float H)
    {
        var r = new Rect(W / 2 - 220, H / 2 - 210, 440, 420); Fill(r, new Color(0.06f, 0.05f, 0.08f, 0.92f));
        GUI.Label(new Rect(r.x, r.y + 16, r.width, 40), "已暂停", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = 28 });
        GUI.Label(new Rect(r.x + 30, r.y + 76, 200, 24), "鼠标灵敏度 " + Tune.I.mouseSens.ToString("0.0"), small);
        Tune.I.mouseSens = GUI.HorizontalSlider(new Rect(r.x + 30, r.y + 104, r.width - 60, 20), Tune.I.mouseSens, 0.5f, 5f);
        GUI.Label(new Rect(r.x + 30, r.y + 130, 200, 24), "音量 " + Mathf.RoundToInt(Sfx.volume * 100), small);
        Sfx.volume = GUI.HorizontalSlider(new Rect(r.x + 30, r.y + 158, r.width - 60, 20), Sfx.volume, 0, 1);
        if (GUI.Button(new Rect(r.x + 30, r.y + 190, r.width - 60, 44), "画质：" + new[] { "流畅", "均衡", "高清" }[quality] + "（点击切换）", btn)) { quality = (quality + 1) % 3; ApplyQuality(); PlayerPrefs.SetInt("quality_v2", quality); }
        if (GUI.Button(new Rect(r.x + 30, r.y + 248, r.width - 60, 44), "继续游戏", btn)) { paused = false; Time.timeScale = 1; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; PlayerPrefs.SetFloat("sens", Tune.I.mouseSens); PlayerPrefs.SetFloat("volume", Sfx.volume); }
        if (GUI.Button(new Rect(r.x + 30, r.y + 306, r.width - 60, 44), "回到主菜单", btn)) { Time.timeScale = 1; UnityEngine.SceneManagement.SceneManager.LoadScene(0); }
    }

    void Shop(float W, float H)
    {
        var r = new Rect(W / 2 - 300, H / 2 - 260, 600, 520); Fill(r, new Color(0.06f, 0.05f, 0.08f, 0.94f));
        GUI.Label(new Rect(r.x, r.y + 14, r.width, 40), "商店 · 积分 " + points, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = 26 });
        var lo = player.loadout; float y = r.y + 70;
        Item(ref y, r, "全息瞄准镜（步枪/霰弹/冲锋枪 通用，开镜带红点）", 600, lo.holo, () => { lo.holo = true; player.RefreshLoadout(); });
        Item(ref y, r, "ACOG 倍镜（步枪/霰弹/冲锋枪 通用，开镜 3.5 倍）", 900, lo.acog, () => { lo.acog = true; player.RefreshLoadout(); });
        Item(ref y, r, "消音器（步枪/霰弹/冲锋枪 通用，枪声小、无火光）", 800, lo.silencer, () => { lo.silencer = true; player.RefreshLoadout(); });
        Item(ref y, r, "战术握把（步枪/霰弹/冲锋枪 通用，后坐 -35%）", 500, lo.grip, () => { lo.grip = true; player.RefreshLoadout(); });
        Item(ref y, r, "激光指示器（步枪/霰弹/冲锋枪 通用，腰射散布 -45%）", 400, lo.laser, () => { lo.laser = true; player.RefreshLoadout(); });
        Item(ref y, r, "护甲 +50", 300, false, () => player.armor = Mathf.Min(100, player.armor + 50));
        Item(ref y, r, "手雷 ×2", 250, false, () => player.grenades = Mathf.Min(6, player.grenades + 2));
        Item(ref y, r, "全部武器弹药补满", 200, false, () => player.GiveAmmo(1));
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 60, r.width - 60, 44), "返回（B）", btn)) { showShop = false; paused = false; Time.timeScale = 1; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    void Item(ref float y, Rect r, string label, int cost, bool owned, System.Action buy)
    {
        GUI.Label(new Rect(r.x + 30, y + 8, r.width - 200, 30), label, small);
        GUI.enabled = !owned && points >= cost;
        if (GUI.Button(new Rect(r.x + r.width - 160, y, 130, 40), owned ? "已拥有" : cost + " 积分", new GUIStyle(btn) { fontSize = 16, fixedHeight = 40 })) { points -= cost; buy(); Sfx.Ui(Sfx.Raw("pickup")); }
        GUI.enabled = true; y += 48;
    }

    void EndScreen(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.02f, 0.03f, 0.75f));
        Label(new Rect(0, H * 0.3f, W, 70), won ? "任务完成" : "任务失败", new GUIStyle(big) { fontSize = 60, alignment = TextAnchor.MiddleCenter });
        Label(new Rect(0, H * 0.3f + 80, W, 40), endText, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter });
        if (GUI.Button(new Rect(W / 2 - 180, H * 0.55f, 360, 50), "回到主菜单", btn)) { Time.timeScale = 1; UnityEngine.SceneManagement.SceneManager.LoadScene(0); }
    }

    // PGO-7 style reticle for the launcher: range ladder plus the predicted impact point
    void RocketSight(float W, float H)
    {
        var p = player; var col = new Color(0.95f, 0.35f, 0.2f, 0.95f);
        Fill(new Rect(W / 2 - 90, H / 2, 70, 1.5f), col); Fill(new Rect(W / 2 + 20, H / 2, 70, 1.5f), col);
        for (int i = 1; i <= 4; i++)
        {
            float y = H / 2 + i * H * 0.035f, half = 26 - i * 4;
            Fill(new Rect(W / 2 - half, y, half * 2, 1.2f), col);
            Label(new Rect(W / 2 + half + 4, y - 9, 40, 16), (i * 100).ToString(), new GUIStyle(small) { fontSize = 10 });
        }
        Fill(new Rect(W / 2 - 1, H / 2 - 26, 2, 22), col);
        var hit = p.AimPoint(); float dist = Vector3.Distance(hit, p.transform.position);
        var sp = p.cam.WorldToScreenPoint(hit);
        if (sp.z > 1)
        {
            float x = sp.x, y = H - sp.y, r = Mathf.Clamp(600 / Mathf.Max(8, dist), 8, 40);
            var gc = new Color(0.4f, 1f, 0.5f, 0.9f);
            for (int i = 0; i < 32; i++)
            {
                float a1 = i / 32f * Mathf.PI * 2;
                Fill(new Rect(x + Mathf.Cos(a1) * r - 1, y + Mathf.Sin(a1) * r - 1, 2, 2), gc);
            }
            Label(new Rect(x - 50, y + r + 4, 100, 18), Mathf.RoundToInt(dist) + " m", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
        }
    }

    void ScopeMask(float W, float H)
    {
        float r = H * 0.46f; var c = new Color(0, 0, 0, 1);
        Fill(new Rect(0, 0, W / 2 - r, H), c); Fill(new Rect(W / 2 + r, 0, W / 2 - r, H), c);
        Fill(new Rect(0, 0, W, H / 2 - r), c); Fill(new Rect(0, H / 2 + r, W, H / 2 - r), c);
        // approximate circle with stacked bars
        for (int i = 0; i < 60; i++) { float y0 = H / 2 - r + i * (2 * r / 60), dy = Mathf.Abs(y0 + r / 60 - H / 2), half = Mathf.Sqrt(Mathf.Max(0, r * r - dy * dy)); Fill(new Rect(W / 2 - r, y0, r - half, 2 * r / 60 + 1), c); Fill(new Rect(W / 2 + half, y0, r - half, 2 * r / 60 + 1), c); }
        Fill(new Rect(W / 2 - r, H / 2, 2 * r, 1), new Color(0, 0, 0, 0.8f)); Fill(new Rect(W / 2, H / 2 - r, 1, 2 * r), new Color(0, 0, 0, 0.8f));
        Fill(new Rect(W / 2 - 2, H / 2 - 2, 4, 4), new Color(1, 0.1f, 0.1f));
    }

    void Compass(float W)
    {
        var p = player; if (!p.cam) return; float yaw = p.cam.transform.eulerAngles.y;
        Fill(new Rect(W / 2 - 200, 14, 400, 24), new Color(0, 0, 0, 0.3f));
        string[] names = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
        for (int i = 0; i < 8; i++) { float d = Mathf.DeltaAngle(yaw, i * 45); if (Mathf.Abs(d) < 90) GUI.Label(new Rect(W / 2 + d * 2.2f - 20, 14, 40, 24), names[i], new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 }); }
        var near = new List<(float a, float d, string n)>();
        foreach (var z in World.Marks)
        {
            var d = z.p - p.transform.position; float dist = new Vector2(d.x, d.z).magnitude; if (dist < 30) continue;
            float a = Mathf.DeltaAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg); if (Mathf.Abs(a) > 80) continue;
            near.Add((a, dist, z.n));
        }
        near.Sort((u, v2) => u.d.CompareTo(v2.d));
        if (near.Count > 3) near.RemoveRange(3, near.Count - 3);
        near.Sort((u, v2) => u.a.CompareTo(v2.a));
        var rowEnd = new float[3] { -9999, -9999, -9999 };
        foreach (var it in near)
        {
            float w = 96, x = W / 2 + it.a * 2.2f - w / 2;
            int row = 0; while (row < 2 && x < rowEnd[row] + 6) row++;          // first row this label fits in
            rowEnd[row] = x + w;
            Label(new Rect(x, 78 + row * 17, w, 18), it.n + " " + Mathf.RoundToInt(it.d) + "m",
                  new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 11 });
        }
        if (mode == Mode.Extraction) foreach (var e in World.I.extracts) { var d = e - p.transform.position; float a = Mathf.DeltaAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg); if (Mathf.Abs(a) < 90) Fill(new Rect(W / 2 + a * 2.2f - 4, 88, 8, 8), new Color(0.3f, 1, 0.4f)); }
    }

    // ------------------------------------------------------------------ map
    // one top-down render of the finished world, used by both the minimap and the big map
    void RenderMap()
    {
        // a headless server has no graphics device and no use for the map picture
        if (NetBoot.IsServerBuild || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        // the map shot is taken from 400 m up: fog and the terrain's low-res basemap would smear it
        // into one flat colour, so both are turned off for the single frame it takes.
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        float bm = 0; int pe = 0;
        if (World.terrain) { bm = World.terrain.basemapDistance; pe = (int)World.terrain.heightmapPixelError; World.terrain.basemapDistance = 4000; World.terrain.heightmapPixelError = 1; }
        // and light it from straight above, so the map is not covered in long dawn shadows
        Quaternion sunRot = Quaternion.identity; var sunShadows = LightShadows.None; float sunI = 0;
        if (sun) { sunRot = sun.transform.rotation; sunShadows = sun.shadows; sunI = sun.intensity;
                   sun.transform.rotation = Quaternion.Euler(89, 0, 0); sun.shadows = LightShadows.None; sun.intensity = 1.1f; }

        int R = 1024;
        var rt = new RenderTexture(R, R, 16); var go = new GameObject("MapCam"); var mc = go.AddComponent<Camera>();
        mc.orthographic = true; mc.orthographicSize = World.HALF; mc.transform.position = new Vector3(0, 400, 0); mc.transform.rotation = Quaternion.Euler(90, 0, 0);
        mc.nearClipPlane = 1; mc.farClipPlane = 800; mc.clearFlags = CameraClearFlags.SolidColor; mc.backgroundColor = new Color(0.16f, 0.13f, 0.1f);
        mc.cullingMask = ~0; mc.targetTexture = rt; mc.enabled = false; mc.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        mapTex = new Texture2D(R, R, TextureFormat.RGB24, false); mapTex.ReadPixels(new Rect(0, 0, R, R), 0, 0);
        var mp = mapTex.GetPixels(); for (int i = 0; i < mp.Length; i++) mp[i] = new Color(Mathf.Clamp01(mp[i].r * 1.12f + 0.03f), Mathf.Clamp01(mp[i].g * 1.12f + 0.03f), Mathf.Clamp01(mp[i].b * 1.1f + 0.03f));
        mapTex.SetPixels(mp); mapTex.Apply();
        RenderTexture.active = prev; mc.targetTexture = null; Destroy(go); rt.Release();

        RenderSettings.fog = fog;
        if (World.terrain) { World.terrain.basemapDistance = bm; World.terrain.heightmapPixelError = pe; }
        if (sun) { sun.transform.rotation = sunRot; sun.shadows = sunShadows; sun.intensity = sunI; }
    }

    // dark ring that turns the square minimap into a round one
    Texture2D Ring()
    {
        if (ringTex) return ringTex;
        int N = 128; ringTex = new Texture2D(N, N, TextureFormat.ARGB32, false); var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f, N / 2f)) / (N / 2f);
            Color c = d > 1.0f ? new Color(0, 0, 0, 0) : d > 0.93f ? new Color(0.08f, 0.09f, 0.1f, 1) : d > 0.90f ? new Color(0.75f, 0.72f, 0.6f, 0.9f) : new Color(0, 0, 0, 0);
            px[y * N + x] = c;
        }
        ringTex.SetPixels(px); ringTex.Apply(); return ringTex;
    }

    Texture2D Vig()
    {
        if (vigTex) return vigTex;
        int N = 64; vigTex = new Texture2D(N, N, TextureFormat.ARGB32, false); var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f, N / 2f)) / (N / 2f);
            px[y * N + x] = new Color(0, 0, 0, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 1.15f, d)) * 0.9f);
        }
        vigTex.SetPixels(px); vigTex.Apply(); return vigTex;
    }

    static Vector2 MapUV(Vector3 w) => new Vector2((w.x + World.HALF) / World.SIZE, (World.HALF - w.z) / World.SIZE);

    // small triangle marker, rotated to face a direction
    void Marker(Vector2 pos, float ang, Color col, float size)
    {
        var m = GUI.matrix; GUIUtility.RotateAroundPivot(ang, pos);
        Fill(new Rect(pos.x - size * 0.5f, pos.y - size * 0.6f, size, size * 0.35f), col);
        Fill(new Rect(pos.x - size * 0.32f, pos.y - size * 0.25f, size * 0.64f, size * 0.3f), col);
        Fill(new Rect(pos.x - size * 0.14f, pos.y + size * 0.05f, size * 0.28f, size * 0.3f), col);
        GUI.matrix = m;
    }

    void Diamond(Vector2 p, Color col, float s)
    {
        for (int i = 0; i < 4; i++) { float k = (i + 0.5f) / 4f, w = s * (1 - Mathf.Abs(k * 2 - 1)); Fill(new Rect(p.x - w, p.y - s + i * s / 2, w * 2, s / 2 + 1), col); }
    }

    void Minimap(float W, float H)
    {
        var p = player; float S = 190, x0 = 20, y0 = 20, view = 170f, scale = S / view; var c = new Vector2(x0 + S / 2, y0 + S / 2);
        float yawDeg = p.cam ? p.cam.transform.eulerAngles.y : 0;
        Fill(new Rect(x0, y0, S, S), new Color(0.08f, 0.08f, 0.09f, 0.85f));
        // rotated slice of the world map
        // north-up slice of the world map (rotating it would break IMGUI clipping)
        if (mapTex)
        {
            GUI.BeginGroup(new Rect(x0, y0, S, S));
            float px = World.SIZE * scale; var uv = MapUV(p.transform.position);
            GUI.color = new Color(0.8f, 0.78f, 0.74f, 1f);      // slightly dim the terrain so the markers stand out
            GUI.DrawTexture(new Rect(S / 2 - uv.x * px, S / 2 - uv.y * px, px, px), mapTex);
            GUI.color = Color.white;
            GUI.EndGroup();
        }
        System.Func<Vector3, Vector2> M = w => { var d = w - p.transform.position; return c + new Vector2(d.x, -d.z) * scale; };
        bool In(Vector2 m) => Vector2.Distance(m, c) < S / 2 - 6;
        // named places: icon inside the map, an arrow with the name pinned to the rim when outside
        var lab = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        var marks = new List<(Vector3 p, string n)>(World.Marks); marks.Sort((u, v2) => Vector3.Distance(u.p, p.transform.position).CompareTo(Vector3.Distance(v2.p, p.transform.position)));
        for (int mi = 0; mi < marks.Count; mi++)
        {
            var z = marks[mi]; var m = M(z.p); float dist = Vector3.Distance(z.p, p.transform.position);
            if (!In(m) && mi >= 4) continue;
            if (In(m)) { Diamond(m, new Color(0.95f, 0.9f, 0.72f, 0.75f), 4); Label(new Rect(m.x - 40, m.y + 4, 80, 16), z.n, lab); }
            else
            {
                var dir = (m - c).normalized; var e = c + dir * (S / 2 - 16);
                Marker(e, Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg, new Color(1f, 0.9f, 0.6f, 0.8f), 11);
                Label(new Rect(e.x - 34, e.y + 6, 68, 16), z.n + " " + Mathf.RoundToInt(dist) + "m", lab);
            }
        }
        foreach (var cp in CapturePoint.All)
        {
            var m = M(cp.transform.position); if (!In(m)) continue;
            for (int i = 0; i < 14; i++) { float a = i / 14f * Mathf.PI * 2; Fill(new Rect(m.x + Mathf.Cos(a) * 8 - 1, m.y + Mathf.Sin(a) * 8 - 1, 2.5f, 2.5f), cp.Tint); }
            Diamond(m, cp.Tint, 4); Label(new Rect(m.x - 40, m.y + 8, 80, 16), cp.label, lab);
        }
        foreach (var v in Vehicle.All) if (v && !v.dead) { var m = M(v.transform.position); if (In(m)) Diamond(m, v.enemy ? new Color(1f, 0.3f, 0.15f) : new Color(0.3f, 0.95f, 0.45f), 5); }
        foreach (var l in Loot.All) if (l && (l.kind == LootKind.Valuable || l.kind == LootKind.Attachment)) { var m = M(l.transform.position); if (In(m)) Diamond(m, new Color(1f, 0.85f, 0.1f), 4.5f); }
        foreach (var e in Enemy.All) if (e && e.alive && e.squad.alerted && Time.time - e.squad.lastSeenT < 8)
        { var m = M(e.transform.position); if (In(m)) Marker(m, e.transform.eulerAngles.y, new Color(1, 0.25f, 0.2f), 9); }
        if (mode == Mode.Extraction) foreach (var e in World.I.extracts) { var m = M(e); if (In(m)) { Diamond(m, new Color(0.35f, 1f, 0.45f), 6); Label(new Rect(m.x - 30, m.y + 5, 60, 16), "撤离", lab); } }
        // facing cone, so you can see which way you are pointing at a glance
        {
            float half = 32f * Mathf.Deg2Rad, fy = yawDeg * Mathf.Deg2Rad;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float a = fy + sgn * half;
                var d2 = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                for (int i = 3; i < 26; i++) { var q = c + d2 * i * 1.6f; Fill(new Rect(q.x - 1, q.y - 1, 2, 2), new Color(0.35f, 0.95f, 1f, 0.35f)); }
            }
        }
        if (wpName != "")
        {
            var m = M(wp); var dir = (m - c); float dd = dir.magnitude; var e2 = dd < S / 2 - 10 ? m : c + dir / dd * (S / 2 - 10);
            int steps = Mathf.Clamp(Mathf.RoundToInt(Vector2.Distance(c, e2) / 8), 1, 14);
            for (int i = 1; i <= steps; i++) { var q = Vector2.Lerp(c, e2, i / (float)steps); Fill(new Rect(q.x - 1.5f, q.y - 1.5f, 3, 3), new Color(0.35f, 1f, 0.5f, 0.85f)); }
            Diamond(e2, new Color(0.35f, 1f, 0.5f), 7); Label(new Rect(e2.x - 40, e2.y + 6, 80, 16), wpName, lab);
        }
        Marker(c, yawDeg, new Color(0, 0, 0, 0.85f), 19);                 // outline
        Marker(c, yawDeg, new Color(0.2f, 1f, 1f), 14);                   // the player: bright cyan arrow
        GUI.DrawTexture(new Rect(x0 - 3, y0 - 3, S + 6, S + 6), Ring());
        string[] dirs = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
        Label(new Rect(x0, y0 + S + 4, S, 20), "朝向 " + dirs[Mathf.RoundToInt(yawDeg / 45f) % 8] + "  " + Mathf.RoundToInt(yawDeg) + "°",
              new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 13 });
        Label(new Rect(x0, y0 - 2, S, 16), "北", new GUIStyle(lab) { fontSize = 12 });
    }

    void ConquestHud(float W, float H)
    {
        // flag strip: one chip per point, coloured by owner
        float cw = 108, total = CapturePoint.All.Count * (cw + 6), x0 = W / 2 - total / 2, y0 = H - 120;
        for (int i = 0; i < CapturePoint.All.Count; i++)
        {
            var cp = CapturePoint.All[i]; var r = new Rect(x0 + i * (cw + 6), y0, cw, 26);
            Fill(r, new Color(0, 0, 0, 0.45f));
            Fill(new Rect(r.x, r.y, r.width * (cp.owner == -1 ? 0.06f : 1f), r.height), new Color(cp.Tint.r, cp.Tint.g, cp.Tint.b, 0.55f));
            if (cp.progress > 0) Fill(new Rect(r.x, r.yMax - 4, r.width * cp.progress, 4), cp.contender == 0 ? new Color(0.35f, 0.8f, 1f) : new Color(1f, 0.45f, 0.3f));
            Label(r, cp.label, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 13 });
        }
        Bar(new Rect(W / 2 - 200, y0 - 16, 195, 8), scoreP / 300f, new Color(0.35f, 0.7f, 1f));
        Bar(new Rect(W / 2 + 5, y0 - 16, 195, 8), scoreE / 300f, new Color(1f, 0.4f, 0.3f));

        // standing inside a point: show what is happening
        var p = player; if (!p) return;
        foreach (var cp in CapturePoint.All)
        {
            if (!cp.Inside(p.transform.position)) continue;
            string t = cp.contested ? "争夺中！清掉圈内的敌人" : cp.owner == 0 && cp.progress <= 0 ? cp.label + "（我方据点）" : "占领 " + cp.label + " " + Mathf.RoundToInt(cp.progress * 100) + "%";
            Label(new Rect(0, H * 0.44f, W, 30), t, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter });
            Bar(new Rect(W / 2 - 150, H * 0.44f + 34, 300, 10), cp.progress, cp.contested ? new Color(1f, 0.8f, 0.2f) : new Color(0.4f, 0.85f, 1f));
            break;
        }
        if (respawnT > 0) Label(new Rect(0, H * 0.5f, W, 40), "阵亡 · " + respawnT.ToString("0") + " 秒后在己方据点重生", new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = 26 });
    }

    void BagPanel(float W, float H)
    {
        var p = player; if (!p) return;
        var r = new Rect(W / 2 - 250, H / 2 - 190, 500, 380);
        Fill(r, new Color(0.06f, 0.06f, 0.08f, 0.92f)); Fill(new Rect(r.x, r.y, r.width, 2), new Color(0.8f, 0.76f, 0.6f));
        Label(new Rect(r.x, r.y + 10, r.width, 30), "背 包", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter });
        var line = new GUIStyle(small) { fontSize = 16 };
        float y = r.y + 52;
        Label(new Rect(r.x + 28, y, 440, 24), "急救包 ×" + p.medkits + "      （X 使用，+45 生命）", line); y += 28;
        Label(new Rect(r.x + 28, y, 440, 24), "护甲板 ×" + p.armorPlates + "      （Z 使用，+50 护甲）", line); y += 28;
        Label(new Rect(r.x + 28, y, 440, 24), "弹药箱 ×" + p.ammoBoxes + "      （弹药打光后按 R 自动拆箱）", line); y += 28;
        Label(new Rect(r.x + 28, y, 440, 24), "手雷 ×" + p.grenades + "        （G 投掷）", line); y += 34;
        Label(new Rect(r.x + 28, y, 440, 24), "配件：" + (p.attachments.Count == 0 ? "无" : string.Join("、", p.attachments.ConvertAll(Loot.AttLabel))), line); y += 34;
        Label(new Rect(r.x + 28, y, 440, 24), "贵重品（" + p.valuables.Count + " 件，合计 $" + lootValue + "）", line); y += 26;
        int n = 0;
        foreach (var it in p.valuables)
        {
            if (n++ >= 6) { Label(new Rect(r.x + 44, y, 420, 22), "…… 其余 " + (p.valuables.Count - 6) + " 件", new GUIStyle(line) { fontSize = 14 }); break; }
            Label(new Rect(r.x + 44, y, 420, 22), "· " + it.name + "  $" + it.value, new GUIStyle(line) { fontSize = 14 }); y += 22;
        }
        Label(new Rect(r.x, r.yMax - 32, r.width, 24), "Tab 关闭背包", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 13 });
    }

    void BigMap(float W, float H)
    {
        var p = player; float S = Mathf.Min(W, H) * 0.86f, x0 = (W - S) / 2, y0 = (H - S) / 2;
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.72f));
        if (mapTex) GUI.DrawTexture(new Rect(x0, y0, S, S), mapTex);
        Fill(new Rect(x0 - 2, y0 - 2, S + 4, 2), new Color(0.8f, 0.76f, 0.6f)); Fill(new Rect(x0 - 2, y0 + S, S + 4, 2), new Color(0.8f, 0.76f, 0.6f));
        Fill(new Rect(x0 - 2, y0, 2, S), new Color(0.8f, 0.76f, 0.6f)); Fill(new Rect(x0 + S, y0, 2, S), new Color(0.8f, 0.76f, 0.6f));
        System.Func<Vector3, Vector2> M = w => { var uv = MapUV(w); return new Vector2(x0 + uv.x * S, y0 + uv.y * S); };
        var lab = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        foreach (var z in World.Marks) { var m = M(z.p); Diamond(m, new Color(1f, 0.93f, 0.75f), 7); Label(new Rect(m.x - 60, m.y + 6, 120, 20), z.n, lab); }
        foreach (var cp in CapturePoint.All) { var m = M(cp.transform.position); Diamond(m, cp.Tint, 10); Label(new Rect(m.x - 60, m.y + 8, 120, 20), cp.label, lab); }
        foreach (var v in Vehicle.All) if (v && !v.dead) Diamond(M(v.transform.position), v.enemy ? new Color(1, 0.35f, 0.2f) : new Color(0.3f, 0.95f, 0.45f), 5);
        if (mode == Mode.Extraction) foreach (var e in World.I.extracts) { var m = M(e); Diamond(m, new Color(0.35f, 1f, 0.45f), 8); Label(new Rect(m.x - 50, m.y + 8, 100, 20), "撤离点", lab); }
        foreach (var e in Enemy.All) if (e && e.alive && e.squad.alerted && Time.time - e.squad.lastSeenT < 8) Diamond(M(e.transform.position), new Color(1, 0.25f, 0.2f), 4);
        if (wpName != "") { var m = M(wp); Diamond(m, new Color(0.35f, 1f, 0.5f), 10); }
        if (p) Marker(M(p.transform.position), p.cam ? p.cam.transform.eulerAngles.y : 0, Color.white, 18);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            var mp = Event.current.mousePosition; float best = 60; int bi = -1;
            for (int i = 0; i < World.Marks.Length; i++) { float dd = Vector2.Distance(M(World.Marks[i].p), mp); if (dd < best) { best = dd; bi = i; } }
            if (bi >= 0) { SetWaypoint(World.Marks[bi].p, World.Marks[bi].n); showMap = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
        Label(new Rect(0, y0 - 34, W, 28), "地图 —— 点击地名设为导航目标 · 按 M 或 Esc 关闭", new GUIStyle(lab) { fontSize = 18 });
    }

    static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
    static void Bar(Rect r, float k, Color c) { Fill(r, new Color(0, 0, 0, 0.5f)); Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), c); }
    static void Label(Rect r, string t, GUIStyle s) { var c = GUI.color; GUI.color = new Color(0, 0, 0, 0.75f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, s); GUI.color = c; GUI.Label(r, t, s); }
}











