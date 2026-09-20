using System.Collections.Generic;
using UnityEngine;

// The map: a 500 m terrain with a central base, a town with enterable houses, an oasis lake, ruins, a canyon and hills,
// real scanned props, destructible cover, sandbag walls, loot spots and extraction points.
public class World : MonoBehaviour
{
    public static World I; public static Terrain terrain;
    public const float SIZE = 500, HALF = 250;
    public readonly List<Vector3> extracts = new List<Vector3>(), lootSpots = new List<Vector3>(), spawnSpots = new List<Vector3>();
    public static readonly Vector3 Town = new Vector3(115, 0, 95), Oasis = new Vector3(-110, 0, 105), Ruins = new Vector3(-115, 0, -100), Canyon = new Vector3(110, 0, -105);
    public static readonly Vector3 Village1 = new Vector3(-48, 0, 52), Village2 = new Vector3(62, 0, -42);
    public static readonly Vector3 Jungle = new Vector3(48, 0, 146), Peak = new Vector3(128, 0, 192);
    // every named place on the map, for the minimap and the big map
    public static readonly (Vector3 p, string n)[] Marks =
    {
        (Vector3.zero, "基地"), (Town, "城镇"), (Oasis, "绿洲"), (Ruins, "遗迹"), (Canyon, "峡谷"),
        (Village1, "北村"), (Village2, "南村"), (new Vector3(-150, 0, 145), "河谷"), (Jungle, "丛林"), (Peak, "高山"), (new Vector3(-150, 0, -62), "西湖"), (new Vector3(152, 0, 62), "东湖"),
    };
    System.Random rng = new System.Random(20260920);
    Material plaster, wood, stone, bags, concrete, glassDark;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    public static float Height(Vector3 p) => terrain ? terrain.SampleHeight(p) + terrain.transform.position.y : 0;
    public static Vector3 OnGround(Vector3 p) { p.y = Height(p); return p; }

    public void Build()
    {
        I = this;
        plaster = Mat("Plaster"); wood = Mat("Wood"); stone = Mat("Stone"); bags = Mat("Bags"); concrete = Mat("Road");
        glassDark = Mats.New("Standard", new Color(0.06f, 0.07f, 0.08f)).F("_Glossiness", 0.85f);
        BuildTerrain();
        Base(); TownZone(); OasisZone(); RuinsZone(); CanyonZone(); JungleZone(); Lakes(); Village(Village1, 7); Village(Village2, 6); RiverZone(); Wilderness(); Roads();
        Optimize();
        extracts.Add(OnGround(new Vector3(-200, 0, 20))); extracts.Add(OnGround(new Vector3(195, 0, -30))); extracts.Add(OnGround(new Vector3(10, 0, 205)));
    }

    // layer 8 = small props (culled at 80 m), 9 = medium (150 m); everything that never moves is static-batched
    void Optimize()
    {
        var statics = new List<GameObject>();
        foreach (Transform t in transform)
        {
            if (t.GetComponentInChildren<Destructible>() || t.GetComponent<Terrain>() || t.name == "GrassField") continue;
            var b = Game.WorldBounds(t.gameObject); float sz = b.size.magnitude;
            int layer = sz < 2.5f ? 8 : sz < 7 ? 9 : 0;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { r.gameObject.layer = layer; if (sz < 1.5f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            statics.Add(t.gameObject);
        }
        StaticBatchingUtility.Combine(statics.ToArray(), gameObject);
    }

    static Material Mat(string n) { var m = Resources.Load<Material>("Mats/" + n); return m ? m : Mats.New("Standard"); }

    // ------------------------------------------------------------------ terrain
    public const float BASE = 20f, MAXH = 120f;   // plain level, and the terrain's vertical range

    float HeightFn(float x, float z)
    {
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) / HALF;                      // rim mountains
        float rim = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.78f, 1f, edge)) * 38;
        float hills = 0;
        var hc = new Vector2(-40, -160); float hd = Vector2.Distance(new Vector2(x, z), hc);   // southern hills
        hills += Mathf.SmoothStep(1, 0, hd / 90) * 16;
        var hc2 = new Vector2(-175, -5); hills += Mathf.SmoothStep(1, 0, Vector2.Distance(new Vector2(x, z), hc2) / 60) * 12;
        hills += Mathf.SmoothStep(1, 0, Vector2.Distance(new Vector2(x, z), new Vector2(Peak.x, Peak.z)) / 58) * 36;   // northern mountain
        hills += Mathf.SmoothStep(1, 0, Vector2.Distance(new Vector2(x, z), new Vector2(160, 30)) / 55) * 18;   // eastern ridge
        hills += Mathf.SmoothStep(1, 0, Vector2.Distance(new Vector2(x, z), new Vector2(-30, -75)) / 45) * 9;
        float n = Mathf.PerlinNoise(x * 0.012f + 50, z * 0.012f + 50) * 3 + Mathf.PerlinNoise(x * 0.05f, z * 0.05f) * 0.6f;
        // keep zones and roads flat
        float flat = 1;
        foreach (var c in new[] { Vector3.zero, Town, Oasis, Ruins, Village1, Village2, Jungle }) flat = Mathf.Min(flat, Mathf.SmoothStep(0, 1, (Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.z)) - 45) / 30));
        float road = Mathf.Min(RoadDist(x, z) / 10f, 1);
        float river = Mathf.SmoothStep(1, 0, RiverDist(x, z) / 16f) * 4.5f;              // carved valleys
        float creek = Mathf.SmoothStep(1, 0, CreekDist(x, z) / 13f) * 3.5f;
        // a real canyon: flat floor, steep walls, raised rim
        float cd = CanyonDist(x, z);
        float floorK = Mathf.SmoothStep(1, 0, (cd - 15) / 22f);
        float rimK = Mathf.SmoothStep(0, 1, (cd - 24) / 26f) * Mathf.SmoothStep(1, 0, (cd - 50) / 32f);
        float canyon = rimK * 8f - floorK * 9f;          // gentler walls: the player can walk in and out
        return BASE + rim + (hills + n) * Mathf.Min(flat, road) - river - creek - LakeBowl(x, z) + canyon;
    }

    // still water: two lakes plus the oasis, each sitting in a carved bowl
    public static readonly Vector3[] LakeCentres = { new Vector3(-150, 0, -62), new Vector3(152, 0, 62) };
    public const float LakeRadius = 30f, LakeDepth = 5.5f;
    public static float LakeBowl(float x, float z)
    {
        float d = 0;
        foreach (var l in LakeCentres) d = Mathf.Max(d, Mathf.SmoothStep(1, 0, (Vector2.Distance(new Vector2(x, z), new Vector2(l.x, l.z)) - LakeRadius * 0.55f) / (LakeRadius * 0.8f)));
        return d * LakeDepth;
    }

    public static readonly Vector2[] River = { new Vector2(-208, 152), new Vector2(-172, 160), new Vector2(-140, 136), new Vector2(-116, 112) };
    // second river: runs off the northern mountain, past the jungle, down the east side
    public static readonly Vector2[] Creek = { new Vector2(103, 196), new Vector2(140, 156), new Vector2(168, 104), new Vector2(172, 44), new Vector2(150, -18) };
    // canyon floor line, through the canyon zone
    public static readonly Vector2[] CanyonLine = { new Vector2(52, -152), new Vector2(84, -128), new Vector2(110, -105), new Vector2(140, -78), new Vector2(170, -44) };

    static float PolyDist(Vector2[] pts, float x, float z) { var p = new Vector2(x, z); float d = 999; for (int i = 0; i < pts.Length - 1; i++) d = Mathf.Min(d, Seg(p, pts[i], pts[i + 1])); return d; }
    public static float RiverDist(float x, float z) => PolyDist(River, x, z);
    public static float CreekDist(float x, float z) => PolyDist(Creek, x, z);
    public static float CanyonDist(float x, float z) => PolyDist(CanyonLine, x, z);

    static float Seg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return Vector2.Distance(p, a + ab * t); }
    // every road on the map: four spokes from the base plus a ring that links the zones
    public static Vector2[][] RoadLines()
    {
        Vector2 t = new Vector2(Town.x, Town.z), o = new Vector2(Oasis.x, Oasis.z), r = new Vector2(Ruins.x, Ruins.z), c = new Vector2(Canyon.x, Canyon.z);
        Vector2 v1 = new Vector2(Village1.x, Village1.z), v2 = new Vector2(Village2.x, Village2.z), j = new Vector2(Jungle.x, Jungle.z);
        return new[]
        {
            new[] { Vector2.zero, t }, new[] { Vector2.zero, o }, new[] { Vector2.zero, r }, new[] { Vector2.zero, c },
            new[] { t, new Vector2(20, 150), o },              // north ring, past the jungle turn-off
            new[] { o, new Vector2(-150, 10), r },             // west ring
            new[] { r, new Vector2(0, -150), c },              // south ring
            new[] { c, new Vector2(150, -10), t },             // east ring
            new[] { v1, o }, new[] { v2, c }, new[] { new Vector2(20, 150), j }
        };
    }

    static float RoadDist(float x, float z)
    {
        var p = new Vector2(x, z); float d = 999;
        foreach (var line in RoadLines()) for (int i = 0; i < line.Length - 1; i++) d = Mathf.Min(d, Seg(p, line[i], line[i + 1]));
        return d;
    }

    void BuildTerrain()
    {
        var td = new TerrainData(); int res = 257; td.heightmapResolution = res; td.size = new Vector3(SIZE, MAXH, SIZE);
        var h = new float[res, res];
        for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) { float wx = x / (float)(res - 1) * SIZE - HALF, wz = y / (float)(res - 1) * SIZE - HALF; h[y, x] = Mathf.Clamp01(HeightFn(wx, wz) / MAXH); }
        td.SetHeights(0, 0, h);
        td.SyncHeightmap();      // without this the GPU copy stays flat and only the collider is sculpted
        var sand = Layer("dense_sand", 7); var cracked = Layer("cracked_red_ground", 9); var rock = Layer("sandstone_blocks_05", 5);
        var grass = new TerrainLayer { diffuseTexture = GrassGroundTex(), tileSize = new Vector2(6, 6), normalScale = 1 };
        int ar = 128; td.alphamapResolution = ar;
        td.terrainLayers = new[] { sand, cracked, rock, grass };
        var a = new float[ar, ar, 4];
        for (int y = 0; y < ar; y++) for (int x = 0; x < ar; x++)
        {
            float nx = x / (float)(ar - 1), ny = y / (float)(ar - 1); float ht = td.GetInterpolatedHeight(nx, ny), steep = td.GetSteepness(nx, ny);
            float wx = nx * SIZE - HALF, wz = ny * SIZE - HALF;
            float r = Mathf.Clamp01((steep - 22) / 12), c = Mathf.Clamp01((ht - BASE - 3) / 8) * (1 - r) + Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.25f;
            // green banks: close to the river, around the oasis, never on the base
            float g = Mathf.Max(Mathf.SmoothStep(1, 0, (RiverDist(wx, wz) - 5) / 22f),
                                Mathf.SmoothStep(1, 0, (Vector2.Distance(new Vector2(wx, wz), new Vector2(Oasis.x, Oasis.z)) - 17) / 22f));
            g *= Mathf.Clamp01(1 - r) * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f));
            if (new Vector2(wx, wz).magnitude < 34) g = 0;
            float rest = 1 - g;
            a[y, x, 0] = Mathf.Max(0, 1 - c - r) * rest; a[y, x, 1] = c * rest; a[y, x, 2] = r * rest; a[y, x, 3] = g;
        }
        td.SetAlphamaps(0, 0, a);
        var go = Terrain.CreateTerrainGameObject(td); go.name = "Terrain"; go.transform.SetParent(transform); go.transform.position = new Vector3(-HALF, 0, -HALF);
        terrain = go.GetComponent<Terrain>(); terrain.heightmapPixelError = 6; terrain.basemapDistance = 60; terrain.drawInstanced = false;   // instanced terrain renders flat here 
        

    }

    // ------------------------------------------------------------------ grass
    static Texture2D GrassTex()
    {
        int N = 32; var t = new Texture2D(N, N, TextureFormat.ARGB32, false);
        var px = new Color[N * N]; for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
        var rr = new System.Random(7);
        for (int b = 0; b < 8; b++)
        {
            float x0 = 4 + (float)rr.NextDouble() * (N - 8), lean = ((float)rr.NextDouble() - 0.5f) * 9, h = N * (0.55f + (float)rr.NextDouble() * 0.45f);
            for (int y = 0; y < h; y++)
            {
                float k = y / h; int xc = Mathf.RoundToInt(x0 + lean * k * k), w = Mathf.RoundToInt(1.4f * (1 - k));
                var c = Color.Lerp(new Color(0.33f, 0.35f, 0.15f), new Color(0.66f, 0.68f, 0.33f), k);
                for (int dx = -w; dx <= w; dx++) { int xx = xc + dx; if (xx < 0 || xx >= N) continue; px[(N - 1 - y) * N + xx] = new Color(c.r, c.g, c.b, 1); }
            }
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D GrassGroundTex()
    {
        int N = 128; var t = new Texture2D(N, N); var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.6f + Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.4f;
            px[y * N + x] = Color.Lerp(new Color(0.24f, 0.30f, 0.12f), new Color(0.55f, 0.58f, 0.26f), n);
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    static TerrainLayer Layer(string tex, float tile)
    {
        var l = new TerrainLayer { diffuseTexture = Resources.Load<Texture2D>("Tex/" + tex + "_diff"), normalMapTexture = Resources.Load<Texture2D>("Tex/" + tex + "_nor"), tileSize = new Vector2(tile, tile), normalScale = 1 };
        return l;
    }

    // ------------------------------------------------------------------ placement helpers
    // sink: fraction of the prop's height to bury, so big rocks sit in the ground instead of hovering
    GameObject Put(string name, Vector3 pos, float yaw, float scale, bool collider = true, float sink = 0f)
    {
        var src = Game.Model(name); if (!src) return null;
        pos.y = Height(pos);
        var go = Instantiate(src, pos, Quaternion.Euler(0, yaw, 0), transform); go.transform.localScale = Vector3.one * scale; go.name = name;
        bool foliage = IsFoliage(name);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = foliage ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            if (foliage) r.receiveShadows = false;
        }
        if (sink > 0)
        {
            var bb = Game.WorldBounds(go);
            float low = float.MaxValue;
            for (int cx = -1; cx <= 1; cx++) for (int cz = -1; cz <= 1; cz++)
                low = Mathf.Min(low, Height(new Vector3(bb.center.x + cx * bb.extents.x * 0.9f, 0, bb.center.z + cz * bb.extents.z * 0.9f)));
            go.transform.position += Vector3.up * (low - pos.y) - Vector3.up * bb.size.y * sink * 0.35f;
        }
        if (collider) Game.AddBoundsCollider(go);
        return go;
    }

    static readonly string[] FoliageNames =
    {
        "island_tree", "quiver_tree", "pine_tree", "dead_tree", "searsia", "fern_", "pachira",
        "shrub_", "grass_medium", "rooibos", "root_cluster"
    };
    static bool IsFoliage(string n) { foreach (var k in FoliageNames) if (n.StartsWith(k)) return true; return false; }

    // a thin slab fitted to the terrain: sampled at its four corners, then tilted to match the slope
    GameObject GroundSlab(Vector3 c, float sx, float sz, Material m, string name, float yaw, float lift = 0.04f)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Vector3 ex = rot * new Vector3(sx / 2, 0, 0), ez = rot * new Vector3(0, 0, sz / 2);
        float h00 = Height(c - ex - ez), h10 = Height(c + ex - ez), h01 = Height(c - ex + ez), h11 = Height(c + ex + ez);
        var dx = 2 * ex + Vector3.up * ((h10 + h11) - (h00 + h01)) / 2f;
        var dz = 2 * ez + Vector3.up * ((h01 + h11) - (h00 + h10)) / 2f;
        var n = Vector3.Cross(dz, dx).normalized; if (n.y < 0) n = -n;
        var go = Box(new Vector3(c.x, (h00 + h10 + h01 + h11) / 4f + lift, c.z), new Vector3(sx, 0.07f, sz), m, name, null, yaw);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0, yaw, 0);
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    bool Clear(Vector3 p, float r) => !Physics.CheckBox(OnGround(p) + Vector3.up * 2, new Vector3(r, 1.9f, r), Quaternion.identity, ~(1 << 2), QueryTriggerInteraction.Ignore);
    Vector3 Around(Vector3 c, float min, float max) { float a = R(0, Mathf.PI * 2), d = R(min, max); return c + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); }

    GameObject Box(Vector3 center, Vector3 size, Material m, string name, Transform parent = null, float yaw = 0)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube); b.name = name; b.transform.SetParent(parent ? parent : transform);
        b.transform.position = center; b.transform.rotation = Quaternion.Euler(0, yaw, 0); b.transform.localScale = size;
        var r = b.GetComponent<Renderer>(); r.sharedMaterial = m;
        // world-scale UVs so tiled textures keep their size on long walls
        var mf = b.GetComponent<MeshFilter>(); var mesh = Instantiate(mf.sharedMesh); var uv = mesh.uv; var n = mesh.normals;
        for (int i = 0; i < uv.Length; i++) { var an = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z)); var s = an.x > 0.5f ? new Vector2(size.z, size.y) : an.y > 0.5f ? new Vector2(size.x, size.z) : new Vector2(size.x, size.y); uv[i] = Vector2.Scale(uv[i], s / 2.5f); }
        mesh.uv = uv; mf.sharedMesh = mesh;
        return b;
    }

    void Sandbags(Vector3 p, float len, float yaw)
    {
        p = OnGround(p);
        var w = Box(p + Vector3.up * 0.5f, new Vector3(len, 1.0f, 0.7f), bags, "Sandbags", null, yaw);
        for (int i = 0; i < 2; i++) { var top = Box(p + Vector3.up * (1.05f), new Vector3(len * 0.95f, 0.14f, 0.6f), bags, "Sandbags", w.transform.parent, yaw); top.transform.position += Quaternion.Euler(0, yaw, 0) * Vector3.right * R(-0.1f, 0.1f); }
    }

    void CrateStack(Vector3 p, float yaw, int nx, int ny)
    {
        p = OnGround(p);
        var root = new GameObject("CrateStack").transform; root.SetParent(transform); root.position = p; root.rotation = Quaternion.Euler(0, yaw, 0);
        var src = Game.Model("wooden_military_crate"); if (!src) return; var sz = Game.WorldBounds(src).size;
        for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < 2; z++)
        {
            if (y == ny - 1 && rng.NextDouble() < 0.2) continue;
            var c = Instantiate(Game.Model(rng.NextDouble() < 0.2 ? "old_military_crate" : "wooden_military_crate"), root);
            c.transform.localPosition = new Vector3((x - (nx - 1) / 2f) * sz.x, y * sz.y, (z - 0.5f) * sz.z);
            c.transform.localRotation = Quaternion.Euler(0, rng.NextDouble() < 0.5 ? 0 : 180, 0);
            if (c.name.StartsWith("old")) c.transform.localScale = new Vector3(sz.x / 1.81f, sz.y / 0.30f, sz.z / 0.97f);
        }
        var col = root.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, ny * sz.y / 2, 0); col.size = new Vector3(nx * sz.x, ny * sz.y, 2 * sz.z);
        var d = root.gameObject.AddComponent<Destructible>(); d.hp = 60 * nx * ny; d.penetrable = true;
    }

    void RedBarrel(Vector3 p)
    {
        var b = Put("Barrel_01", p, R(0, 360), 1.35f); if (!b) return; b.name = "Barrel_red";
        foreach (var r in b.GetComponentsInChildren<Renderer>()) foreach (var m in r.materials) { if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", new Color(0.75f, 0.12f, 0.08f)); }
        var d = b.AddComponent<Destructible>(); d.hp = 25; d.explosive = true; d.penetrable = false;
    }

    void Tree(string n, Vector3 p, float s, float r, float h) { var t = Put(n, p, R(0, 360), s, false, 0.05f); if (!t) return; var c = t.AddComponent<CapsuleCollider>(); c.radius = r / s; c.height = h / s; c.center = new Vector3(0, h / 2 / s, 0); }

    // enterable house: walls with a door gap and windows, floor, walkable roof with parapet, exterior stairs
    void House(Vector3 c, float w, float d, float yaw, bool twoFloors = false)
    {
        c = OnGround(c);
        var root = new GameObject("House").transform; root.SetParent(transform); root.position = c; root.rotation = Quaternion.Euler(0, yaw, 0);
        float H = 3.2f, t = 0.3f; int floors = twoFloors ? 2 : 1;
        System.Action<Vector3, Vector3, Material, string> B = (lc, sz, m, n) => { var b = Box(root.TransformPoint(lc), sz, m, n, root, yaw); };
        for (int f = 0; f < floors; f++)
        {
            float y0 = f * H;
            // back & sides with window gaps
            WallWithWindows(root, new Vector3(0, y0, -d / 2 + t / 2), w, H, t, 0, yaw);
            WallWithWindows(root, new Vector3(-w / 2 + t / 2, y0, 0), d - 2 * t, H, t, 90, yaw);
            WallWithWindows(root, new Vector3(w / 2 - t / 2, y0, 0), d - 2 * t, H, t, 90, yaw);
            // front with door (ground floor) or window
            float dg = 1.4f, dc = w * 0.15f;
            float la = (dc - dg / 2) + w / 2, rb = w / 2 - (dc + dg / 2);
            B(new Vector3(-w / 2 + la / 2, y0 + H / 2, d / 2 - t / 2), new Vector3(la, H, t), plaster, "Wall");
            B(new Vector3(w / 2 - rb / 2, y0 + H / 2, d / 2 - t / 2), new Vector3(rb, H, t), plaster, "Wall");
            B(new Vector3(dc, y0 + (f == 0 ? 2.3f : 1f) + (H - (f == 0 ? 2.3f : 1f)) / 2, d / 2 - t / 2), new Vector3(dg, H - (f == 0 ? 2.3f : 1f), t), plaster, "Wall");
            if (f > 0) B(new Vector3(dc, y0 + 0.5f, d / 2 - t / 2), new Vector3(dg, 1f, t), plaster, "Wall");
            B(new Vector3(0, y0 + 0.05f, 0), new Vector3(w - 0.2f, 0.1f, d - 0.2f), wood, "Floor");
        }
        float top = floors * H;
        B(new Vector3(0, top + 0.1f, 0), new Vector3(w + 0.3f, 0.2f, d + 0.3f), concrete, "Floor_roof");
        float gap = 1.6f;
        B(new Vector3(0, top + 0.55f, -d / 2), new Vector3(w, 0.7f, 0.2f), plaster, "Parapet");
        B(new Vector3(0, top + 0.55f, d / 2), new Vector3(w, 0.7f, 0.2f), plaster, "Parapet");
        B(new Vector3(w / 2, top + 0.55f, 0), new Vector3(0.2f, 0.7f, d), plaster, "Parapet");
        B(new Vector3(-w / 2, top + 0.55f, gap / 2), new Vector3(0.2f, 0.7f, d - gap), plaster, "Parapet");
        // exterior stairs up the -x side
        int steps = Mathf.RoundToInt(top / 0.3f); float sd = (d - 0.4f) / steps;
        for (int k = 0; k < steps; k++) B(new Vector3(-w / 2 - 0.75f, (k + 1) * 0.3f / 2, d / 2 - 0.2f - sd * (k + 0.5f)), new Vector3(1.2f, (k + 1) * 0.3f, sd), stone, "Stairs");
        // AC unit + water tank for detail
        var ac = Put("utility_box_01", root.TransformPoint(new Vector3(w / 2 + 0.4f, 0, 0.5f)), yaw + 90, 1.1f);
        var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder); tank.name = "WaterTank"; tank.transform.SetParent(root); tank.transform.position = root.TransformPoint(new Vector3(w * 0.25f, top + 0.9f, -d * 0.2f)); tank.transform.localScale = new Vector3(1.1f, 0.7f, 1.1f); tank.GetComponent<Renderer>().sharedMaterial = concrete;
        lootSpots.Add(root.TransformPoint(new Vector3(-w * 0.2f, 0.2f, -d * 0.2f)));
        if (twoFloors) lootSpots.Add(root.TransformPoint(new Vector3(w * 0.2f, H + 0.2f, 0)));
    }

    void WallWithWindows(Transform root, Vector3 lc, float len, float H, float t, float localYaw, float yaw)
    {
        int n = Mathf.Max(1, Mathf.FloorToInt(len / 3)); float seg = len / n, ww = 1.0f, wy = 1.2f, wh = 1.0f;
        var rot = Quaternion.Euler(0, localYaw, 0);
        for (int i = 0; i < n; i++)
        {
            float o = -len / 2 + seg * (i + 0.5f);
            System.Action<float, float, float, float> P = (x, y, sx, sy) => Box(root.TransformPoint(lc + rot * new Vector3(x, 0, 0) + Vector3.up * y), new Vector3(sx, sy, t), plaster, "Wall", root, yaw + localYaw);
            P(o - seg / 2 + (seg - ww) / 4, H / 2, (seg - ww) / 2, H);
            P(o + seg / 2 - (seg - ww) / 4, H / 2, (seg - ww) / 2, H);
            P(o, wy / 2, ww, wy);
            P(o, wy + wh + (H - wy - wh) / 2, ww, H - wy - wh);
        }
    }

    // ------------------------------------------------------------------ zones
    void Base()
    {
        for (int i = -9; i <= 9; i++) { if (Mathf.Abs(i) < 2) continue; Put("concrete_road_barrier", new Vector3(i * 3.2f, 0, -30), 0, 1.6f); Put("concrete_road_barrier", new Vector3(i * 3.2f, 0, 30), 0, 1.6f); Put("concrete_road_barrier", new Vector3(-30, 0, i * 3.2f), 90, 1.6f); Put("concrete_road_barrier", new Vector3(30, 0, i * 3.2f), 90, 1.6f); }
        foreach (var q in new[] { new Vector3(-4, 0, -28), new Vector3(4, 0, -28), new Vector3(-4, 0, 28), new Vector3(4, 0, 28) }) Put("concrete_road_barrier", q, 0, 1.1f);
        Sandbags(new Vector3(-8, 0, -12), 6, 0); Sandbags(new Vector3(10, 0, 9), 5, 0); Sandbags(new Vector3(14, 0, -6), 5, 90); Sandbags(new Vector3(-12, 0, 6), 5, 90);
        for (int i = 0; i < 6; i++) { var p = Around(Vector3.zero, 8, 25); if (Clear(p, 2.5f)) CrateStack(p, R(0, 360), rng.Next(2, 4), rng.Next(1, 3)); }
        for (int i = 0; i < 6; i++) { var p = Around(Vector3.zero, 6, 26); if (Clear(p, 0.6f)) { if (i % 3 == 0) RedBarrel(p); else Put(i % 2 == 0 ? "Barrel_01" : "barrel_03", p, R(0, 360), 1.35f); } }
        for (int i = 0; i < 6; i++) Put("metal_jerrycan", Around(Vector3.zero, 5, 25), R(0, 360), 1.1f, false);
        foreach (var q in new[] { new Vector3(-9, 0, -7), new Vector3(9, 0, 7), new Vector3(9, 0, -9), new Vector3(-9, 0, 9) }) Put("street_lamp_01", q, R(0, 360), 1.3f);
        House(new Vector3(-18, 0, 18), 8, 6, 0); House(new Vector3(18, 0, -18), 7, 6, 180);
        var pad = Box(OnGround(new Vector3(-20, 0, -12)) + Vector3.up * 0.03f, new Vector3(9, 0.06f, 9), concrete, "Helipad"); Destroy(pad.GetComponent<Collider>());
        spawnSpots.Add(new Vector3(0, 0, -5));
    }

    void TownZone()
    {
        var c = Town;
        for (int gx = -2; gx <= 2; gx++) for (int gz = -2; gz <= 1; gz++)
        {
            if (gx == 0 && gz == 0) continue;
            var p = c + new Vector3(gx * 24 + R(-2.5f, 2.5f), 0, gz * 23 + R(-2.5f, 2.5f));
            if (rng.NextDouble() < 0.15) Put(rng.NextDouble() < 0.6 ? "b_desert" : "b_ancient", p, rng.Next(0, 4) * 90, 0.8f);
            else House(p, R(7, 10), R(6, 9), rng.Next(0, 4) * 90, rng.NextDouble() < 0.7);
        }
        for (int t = -30; t <= 30; t += 12) { Put("street_lamp_01", c + new Vector3(t, 0, 3.5f), 180, 1.3f); Put("street_lamp_01", c + new Vector3(3.5f, 0, t + 6), -90, 1.3f); }
        for (int i = 0; i < 6; i++) { var p = Around(c, 4, 35); if (Clear(p, 2f)) CrateStack(p, R(0, 360), 2, rng.Next(1, 3)); }
        for (int i = 0; i < 5; i++) { var p = Around(c, 4, 35); if (Clear(p, 1f)) Put("concrete_road_barrier", p, R(0, 360), 1.1f); }
        for (int i = 0; i < 4; i++) { var p = Around(c, 4, 35); if (Clear(p, 0.6f)) RedBarrel(p); }
        for (int i = 0; i < 4; i++) { var p = Around(c, 6, 30); if (Clear(p, 2f)) Sandbags(p, 4, R(0, 180)); }
        // wooden fences that bullets go through
        for (int i = 0; i < 6; i++) { var p = Around(c, 10, 38); if (!Clear(p, 2f)) continue; var f = Box(OnGround(p) + Vector3.up * 0.9f, new Vector3(4, 1.8f, 0.12f), wood, "WoodFence", null, R(0, 180)); var d = f.AddComponent<Destructible>(); d.hp = 90; }
        spawnSpots.Add(c);
    }

    void OasisZone()
    {
        var c = Oasis;
        var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder); water.name = "Water"; Destroy(water.GetComponent<Collider>());
        water.transform.position = OnGround(c) + Vector3.up * 0.08f; water.transform.localScale = new Vector3(34, 0.01f, 26);
        water.GetComponent<Renderer>().sharedMaterial = Mat("Water");
        var block = new GameObject("WaterBlock"); block.transform.SetParent(transform); block.transform.position = OnGround(c); var bc = block.AddComponent<BoxCollider>(); bc.size = new Vector3(26, 3, 18); bc.center = Vector3.up * 1.5f;
        for (int i = 0; i < 12; i++) { float a = i / 12f * Mathf.PI * 2; var p = c + new Vector3(Mathf.Cos(a) * R(20, 30), 0, Mathf.Sin(a) * R(16, 24)); Tree(i % 3 == 0 ? "quiver_tree_01" : "island_tree_01", p, i % 3 == 0 ? 1.7f : R(0.8f, 1.05f), 0.4f, 3); }
        for (int i = 0; i < 16; i++) Put("sand_rocks_small_01", Around(c, 18, 38), R(0, 360), R(1.2f, 2f), false);
        House(c + new Vector3(-30, 0, 18), 7, 6, 90);
        for (int i = 0; i < 3; i++) { var p = Around(c, 22, 36); if (Clear(p, 2f)) CrateStack(p, R(0, 360), 2, 1); }
        spawnSpots.Add(c + new Vector3(0, 0, -30)); lootSpots.Add(c + new Vector3(24, 0.2f, 0));
    }

    void RuinsZone()
    {
        var c = Ruins;
        for (int i = 0; i < 16; i++)
        {
            var p = Around(c, 3, 34); if (!Clear(p, 2f)) continue; float len = R(3, 8), h = R(1.2f, 4.5f);
            Box(OnGround(p) + Vector3.up * h / 2, new Vector3(len, h, 0.8f), stone, "RuinWall", null, rng.Next(0, 2) * 90 + R(-8, 8));
        }
        for (int i = 0; i < 10; i++)
        {
            var p = Around(c, 5, 30); if (!Clear(p, 1f)) continue; float h = rng.NextDouble() < 0.5 ? R(1.5f, 3) : R(5, 7);
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder); col.name = "Column"; col.transform.SetParent(transform); col.transform.position = OnGround(p) + Vector3.up * h / 2; col.transform.localScale = new Vector3(1.1f, h / 2, 1.1f); col.GetComponent<Renderer>().sharedMaterial = stone;
        }
        for (int i = 0; i < 5; i++) Put("sand_rocks_small_01", Around(c, 5, 35), R(0, 360), R(0.8f, 1.2f), false);
        for (int i = 0; i < 6; i++) { var p = Around(c, 5, 30); lootSpots.Add(OnGround(p) + Vector3.up * 0.2f); }
        spawnSpots.Add(c);
    }

    // real gorge: the terrain is carved by HeightFn, this dresses the walls and the floor
    void CanyonZone()
    {
        var c = Canyon;
        for (int i = 0; i < CanyonLine.Length - 1; i++)
        {
            Vector2 a = CanyonLine[i], b = CanyonLine[i + 1];
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / 17f);
            for (int s = 0; s <= steps; s++)
            {
                var m = Vector2.Lerp(a, b, s / (float)steps);
                var dir = (b - a).normalized; var nrm = new Vector2(-dir.y, dir.x);
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                // cliff faces on both rims
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = new Vector3(m.x + nrm.x * R(15, 23) * side, 0, m.y + nrm.y * R(15, 23) * side);
                    double pick = rng.NextDouble();
                    string mdl = pick < 0.45 ? "namaqualand_cliff_01" : pick < 0.75 ? "namaqualand_cliff_02" : "rock_face_01";
                    float sc = mdl == "namaqualand_cliff_02" ? R(0.9f, 1.5f) : mdl == "rock_face_01" ? R(1.6f, 2.6f) : R(1.4f, 2.4f);
                    Put(mdl, p, yaw + (side > 0 ? 0 : 180) + R(-14, 14), sc, true, 0.3f);
                    var lowP = new Vector3(m.x + nrm.x * R(10, 14) * side, 0, m.y + nrm.y * R(10, 14) * side);
                    if (rng.NextDouble() < 0.6) Put(rng.NextDouble() < 0.5 ? "namaqualand_boulder_02" : "namaqualand_boulder_04", lowP, R(0, 360), R(1.2f, 2.4f), true, 0.15f);
                }
                if (s % 2 == 0)
                {
                    var f = new Vector3(m.x + nrm.x * R(-7, 7), 0, m.y + nrm.y * R(-7, 7));
                    if (Clear(f, 2f)) { bool small = rng.NextDouble() < 0.4; Put(small ? "namaqualand_boulders_01" : "namaqualand_boulder_04", f, R(0, 360), small ? R(2f, 4f) : R(0.9f, 2f)); }
                }
            }
        }
        // a holdable position on the canyon floor
        Put("v_tank", c + new Vector3(8, 0, 6), 40, 1);
        for (int i = 0; i < 6; i++) { var p = Around(c, 6, 26); if (Clear(p, 2f)) Sandbags(p, 5, R(0, 180)); }
        for (int i = 0; i < 4; i++) { var p = Around(c, 8, 26); if (Clear(p, 2f)) CrateStack(p, R(0, 360), 2, rng.Next(1, 3)); }
        for (int i = 0; i < 3; i++) { var p = Around(c, 8, 24); if (Clear(p, 0.6f)) RedBarrel(p); }
        for (int i = 0; i < 6; i++) lootSpots.Add(OnGround(Around(c, 8, 30)) + Vector3.up * 0.2f);
        House(c + new Vector3(-16, 0, 10), 8, 6, 20, true);
        spawnSpots.Add(c);
    }

    // the old "high mountain" corner is now wilderness jungle at the foot of the peak
    void JungleZone()
    {
        var c = Jungle;
        var gm = Mats.New("Standard").Tex(GrassGroundTex());
        gm.F("_Glossiness", 0.08f).Tile(new Vector2(3, 3));
        for (int i = 0; i < 70; i++)
        {
            var p = Around(c, 0, 52);
            GroundSlab(p, R(9, 18), R(9, 18), gm, "GrassPatch", R(0, 360));
        }
        string[] bigTrees = { "island_tree_02", "island_tree_03", "island_tree_01", "island_tree_02" };
        for (int i = 0; i < 40; i++)
        {
            var p = Around(c, 4, 55); if (!Clear(p, 1.5f)) continue;
            Tree(bigTrees[rng.Next(bigTrees.Length)], p, R(0.9f, 1.5f), 0.45f, 4);
        }
        for (int i = 0; i < 26; i++) Put(rng.NextDouble() < 0.5 ? "searsia_lucida" : "quiver_tree_01", Around(c, 4, 58), R(0, 360), R(0.8f, 1.3f), false);
        for (int i = 0; i < 70; i++)
        {
            double q2 = rng.NextDouble();
            string s = q2 < 0.35 ? "fern_02" : q2 < 0.6 ? "shrub_02" : q2 < 0.85 ? "shrub_04" : "pachira_aquatica_01";
            Put(s, Around(c, 3, 60), R(0, 360), s == "pachira_aquatica_01" ? R(0.5f, 0.8f) : s == "shrub_04" ? R(1.5f, 2.6f) : R(0.9f, 1.6f), false);
        }
        for (int i = 0; i < 85; i++) { bool wide = rng.NextDouble() < 0.5; Put(wide ? "grass_medium_01" : "grass_medium_02", Around(c, 2, 62), R(0, 360), wide ? R(0.5f, 0.9f) : R(1.2f, 2f), false); }
        for (int i = 0; i < 8; i++) Put("root_cluster_01", Around(c, 5, 55), R(0, 360), R(0.8f, 1.4f), false);
        // ranger camp in a clearing
        House(c + new Vector3(12, 0, -6), 8, 7, 25, true);
        House(c + new Vector3(-14, 0, 8), 7, 6, -40, false);
        for (int i = 0; i < 4; i++) { var p = Around(c + new Vector3(0, 0, 2), 8, 20); if (Clear(p, 2f)) CrateStack(p, R(0, 360), 2, rng.Next(1, 3)); }
        for (int i = 0; i < 3; i++) { var p = Around(c, 8, 22); if (Clear(p, 2f)) Sandbags(p, 4, R(0, 180)); }
        for (int i = 0; i < 4; i++) lootSpots.Add(OnGround(Around(c, 6, 30)) + Vector3.up * 0.2f);
        // cliffs and boulders climbing toward the peak
        for (int i = 0; i < 9; i++)
        {
            var p = Vector3.Lerp(c, Peak, R(0.25f, 0.6f)) + new Vector3(R(-40, 40), 0, R(-30, 30));
            if (!Clear(p, 4f)) continue;
            double pk = rng.NextDouble();
            string m2 = pk < 0.45 ? "mountainside" : pk < 0.75 ? "namaqualand_cliff_02" : "namaqualand_cliff_01";
            Put(m2, p, R(0, 360), m2 == "namaqualand_cliff_02" ? R(0.9f, 1.4f) : R(1.2f, 2f), true, 0.35f);
        }
        spawnSpots.Add(c);
    }

    // open water with a shoreline: reeds, rocks, a few trees and a fishing hut
    void Lakes()
    {
        var wm = Mat("Water");
        var gm = Mats.New("Standard").Tex(GrassGroundTex());
        gm.F("_Glossiness", 0.08f).Tile(new Vector2(3, 3));
        foreach (var c in LakeCentres)
        {
            float bed = Height(c);                       // deepest point of the bowl
            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder); water.name = "LakeWater"; Destroy(water.GetComponent<Collider>());
            water.transform.position = new Vector3(c.x, bed + LakeDepth * 0.78f, c.z);
            water.transform.localScale = new Vector3(LakeRadius * 1.9f, 0.02f, LakeRadius * 1.75f);
            water.GetComponent<Renderer>().sharedMaterial = wm;
            water.AddComponent<WaterFlow>();
            // you cannot swim: a block keeps you on the shore
            var block = new GameObject("LakeBlock"); block.transform.SetParent(transform); block.transform.position = new Vector3(c.x, bed, c.z);
            var bc = block.AddComponent<BoxCollider>(); bc.size = new Vector3(LakeRadius * 1.5f, 6, LakeRadius * 1.4f); bc.center = Vector3.up * 3f;
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * Mathf.PI * 2 + R(-0.1f, 0.1f);
                var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R(LakeRadius * 1.02f, LakeRadius * 1.5f);
                GroundSlab(p, R(10, 18), R(10, 18), gm, "GrassPatch", R(0, 360));
                if (i % 3 == 0) Tree(rng.NextDouble() < 0.5 ? "island_tree_01" : "island_tree_02", p, R(0.9f, 1.3f), 0.4f, 4);
                else if (i % 3 == 1) Put(rng.NextDouble() < 0.5 ? "shrub_02" : "grass_medium_02", p, R(0, 360), R(1f, 1.7f), false);
                else Put("sand_rocks_small_01", p, R(0, 360), R(0.9f, 1.4f), false);
            }
            House(c + new Vector3(LakeRadius * 1.35f, 0, LakeRadius * 0.5f), 7, 6, 210, false);
            for (int i = 0; i < 3; i++) { var p = Around(c, LakeRadius * 1.2f, LakeRadius * 1.7f); if (Clear(p, 2f)) CrateStack(p, R(0, 360), 2, 1); }
            lootSpots.Add(OnGround(Around(c, LakeRadius * 1.1f, LakeRadius * 1.6f)) + Vector3.up * 0.2f);
            spawnSpots.Add(c + new Vector3(LakeRadius * 1.4f, 0, 0));
        }
    }

    // a handful of enterable houses around a well, closer to the base than the big zones
    void Village(Vector3 c, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2 + 0.4f; var p = c + new Vector3(Mathf.Cos(a) * R(12, 20), 0, Mathf.Sin(a) * R(12, 20));
            House(p, R(7, 9), R(6, 8), Mathf.Atan2(c.x - p.x, c.z - p.z) * Mathf.Rad2Deg, rng.NextDouble() < 0.6);
        }
        var well = GameObject.CreatePrimitive(PrimitiveType.Cylinder); well.name = "Well"; well.transform.SetParent(transform);
        well.transform.position = OnGround(c) + Vector3.up * 0.5f; well.transform.localScale = new Vector3(2.4f, 0.5f, 2.4f); well.GetComponent<Renderer>().sharedMaterial = stone;
        for (int i = 0; i < 4; i++) { var p = Around(c, 6, 20); if (Clear(p, 2f)) CrateStack(p, R(0, 360), 2, rng.Next(1, 3)); }
        for (int i = 0; i < 4; i++) { var p = Around(c, 6, 24); if (Clear(p, 2f)) Sandbags(p, 4, R(0, 180)); }
        for (int i = 0; i < 3; i++) { var p = Around(c, 6, 22); if (Clear(p, 0.6f)) RedBarrel(p); }
        for (int i = 0; i < 6; i++) Put("sand_rocks_small_01", Around(c, 8, 26), R(0, 360), R(1.2f, 2f), false);
        Put("street_lamp_01", c + new Vector3(4, 0, 4), R(0, 360), 1.3f);
        lootSpots.Add(OnGround(c + new Vector3(3, 0, -3)) + Vector3.up * 0.2f);
        spawnSpots.Add(c);
    }

    // water running down from the north-west rim into the oasis, with green banks
    void RiverZone()
    {
        var w = Mat("Water");
        for (int i = 0; i < River.Length - 1; i++)
        {
            Vector2 a = River[i], b = River[i + 1]; float len = Vector2.Distance(a, b), yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
            int steps = Mathf.CeilToInt(len / 12);
            for (int s = 0; s < steps; s++)
            {
                Vector2 m0 = Vector2.Lerp(a, b, s / (float)steps), m1 = Vector2.Lerp(a, b, (s + 1) / (float)steps), mm = (m0 + m1) / 2;
                var pos = OnGround(new Vector3(mm.x, 0, mm.y)) + Vector3.up * 0.9f;
                var q = Box(pos, new Vector3(9.5f, 0.05f, len / steps + 1.5f), w, "Water", null, yaw); Destroy(q.GetComponent<Collider>());
            }
        }
        for (int i = 0; i < 40; i++)
        {
            float t = (float)rng.NextDouble() * (River.Length - 1); int k = Mathf.Min(River.Length - 2, (int)t);
            var m = Vector2.Lerp(River[k], River[k + 1], t - k); var n = (River[k + 1] - River[k]).normalized; n = new Vector2(-n.y, n.x);
            var p = new Vector3(m.x, 0, m.y) + new Vector3(n.x, 0, n.y) * R(7, 16) * (rng.NextDouble() < 0.5 ? 1 : -1);
            if (rng.NextDouble() < 0.6) Put("sand_rocks_small_01", p, R(0, 360), R(1.4f, 2.4f), false);
            else if (rng.NextDouble() < 0.5) Tree("island_tree_01", p, R(0.9f, 1.3f), 0.35f, 3);
            else Put("sand_rocks_small_01", p, R(0, 360), R(0.9f, 1.4f), false);
        }
        // green ground patches along the banks (terrain splat layers do not take in the player build)
        var gm = Mats.New("Standard").Tex(GrassGroundTex()).F("_Glossiness", 0.08f);
        gm.Tile(new Vector2(3, 3));
        for (int i = 0; i < 90; i++)
        {
            float t = (float)rng.NextDouble() * (River.Length - 1); int k = Mathf.Min(River.Length - 2, (int)t);
            var m2 = Vector2.Lerp(River[k], River[k + 1], t - k);
            var p = new Vector3(m2.x + R(-18, 18), 0, m2.y + R(-18, 18));
            GroundSlab(p, R(8, 16), R(8, 16), gm, "GrassPatch", R(0, 360));
        }
        for (int i = 0; i < 40; i++)
            GroundSlab(Oasis + new Vector3(R(-38, 38), 0, R(-32, 32)), R(7, 14), R(7, 14), gm, "GrassPatch", R(0, 360));
        House(OnGround(new Vector3(-170, 0, 178)), 8, 6, 30, true);
        // the eastern creek: water, banks and reeds
        for (int i = 0; i < Creek.Length - 1; i++)
        {
            Vector2 a2 = Creek[i], b2 = Creek[i + 1]; float len2 = Vector2.Distance(a2, b2);
            float yaw2 = Mathf.Atan2(b2.x - a2.x, b2.y - a2.y) * Mathf.Rad2Deg; int st = Mathf.CeilToInt(len2 / 12);
            for (int s = 0; s < st; s++)
            {
                Vector2 m0 = Vector2.Lerp(a2, b2, s / (float)st), m1 = Vector2.Lerp(a2, b2, (s + 1) / (float)st), mm = (m0 + m1) / 2;
                var q = Box(OnGround(new Vector3(mm.x, 0, mm.y)) + Vector3.up * 0.55f, new Vector3(7, 0.05f, len2 / st + 1.5f), w, "Water", null, yaw2);
                Destroy(q.GetComponent<Collider>());
                GroundSlab(new Vector3(mm.x, 0, mm.y), R(14, 22), len2 / st + 4f, gm, "GrassPatch", yaw2);
                if (s % 2 == 0) { bool wide2 = rng.NextDouble() < 0.5; Put(wide2 ? "grass_medium_01" : "shrub_02", new Vector3(mm.x + R(-12, 12), 0, mm.y + R(-12, 12)), R(0, 360), wide2 ? R(0.5f, 0.9f) : R(1f, 1.7f), false); }
            }
        }
        for (int i = 0; i < 3; i++) lootSpots.Add(OnGround(new Vector3(River[1].x + R(-14, 14), 0, River[1].y + R(-14, 14))) + Vector3.up * 0.2f);
    }

    void Wilderness()
    {
        for (int i = 0; i < 70; i++) { var p = new Vector3(R(-215, 215), 0, R(-215, 215)); if (p.magnitude < 40 || !Clear(p, 2f)) continue; Put(rng.NextDouble() < 0.5 ? "namaqualand_boulder_04" : "namaqualand_boulder_02", p, R(0, 360), R(0.8f, 2.6f), true, 0.12f); }
        for (int i = 0; i < 50; i++) { var p = new Vector3(R(-215, 215), 0, R(-215, 215)); if (p.magnitude < 40 || !Clear(p, 1f)) continue; Tree("quiver_tree_01", p, R(1.3f, 2f), 0.25f, 3); }
        for (int i = 0; i < 26; i++) { var p = new Vector3(R(-215, 215), 0, R(-215, 215)); if (p.magnitude < 40) continue; Put("dead_tree_trunk", p, R(0, 360), 1.2f); }
        for (int i = 0; i < 110; i++) { var p = new Vector3(R(-220, 220), 0, R(-220, 220)); if (p.magnitude < 34) continue; Put("sand_rocks_small_01", p, R(0, 360), R(1.2f, 2f), false); }
        for (int i = 0; i < 40; i++) { var p = new Vector3(R(-200, 200), 0, R(-200, 200)); if (p.magnitude < 40 || !Clear(p, 3f)) continue; Put("sand_rocks_small_01", p, R(0, 360), R(0.8f, 1.3f), false); }
        // lonely landmarks between the zones so travelling is not empty
        for (int i = 0; i < 7; i++) { var p = new Vector3(R(-180, 180), 0, R(-180, 180)); if (p.magnitude < 55 || !Clear(p, 7f)) continue; House(p, R(7, 9), R(6, 8), R(0, 360), rng.NextDouble() < 0.5); }
        for (int i = 0; i < 10; i++) { var p = new Vector3(R(-190, 190), 0, R(-190, 190)); if (p.magnitude < 50 || !Clear(p, 3f)) continue; CrateStack(p, R(0, 360), 2, rng.Next(1, 3)); Sandbags(p + new Vector3(4, 0, 0), 4, R(0, 180)); }
        for (int i = 0; i < 14; i++) { var p = new Vector3(R(-200, 200), 0, R(-200, 200)); if (p.magnitude < 45 || !Clear(p, 1f)) continue; Put("concrete_road_barrier", p, R(0, 360), 1.2f); }
    }

    void Roads()
    {
        foreach (var line in RoadLines())
            for (int i = 0; i < line.Length - 1; i++) RoadSegment(line[i], line[i + 1]);
    }

    // lay a road as short chunks so it follows the terrain instead of floating over it
    void RoadSegment(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b); int steps = Mathf.Max(1, Mathf.CeilToInt(len / 14f));
        float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
        for (int s = 0; s < steps; s++)
        {
            Vector2 p0 = Vector2.Lerp(a, b, s / (float)steps), p1 = Vector2.Lerp(a, b, (s + 1) / (float)steps), m = (p0 + p1) / 2;
            float h0 = Height(new Vector3(p0.x, 0, p0.y)), h1 = Height(new Vector3(p1.x, 0, p1.y)), seg = len / steps;
            var mid = new Vector3(m.x, (h0 + h1) / 2 + 0.12f, m.y);
            var road = Box(mid, new Vector3(6.5f, 0.12f, seg + 1.2f), concrete, "Road", null, yaw);
            // tilt the slab along the slope so it does not bury itself on hills
            road.transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-Mathf.Atan2(h1 - h0, seg) * Mathf.Rad2Deg, 0, 0);
            Destroy(road.GetComponent<Collider>());
        }
    }
}


