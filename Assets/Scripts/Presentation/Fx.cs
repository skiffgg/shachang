using UnityEngine;

// Lightweight effects: tracers, impact puffs, blood, explosions, muzzle flashes.
// Every sprite uses a generated soft texture — an untextured quad renders as a white square in front of the camera.
public static class Fx
{
    static Material add, puffMat, smokeMat, flashMat;
    static Texture2D softTex, starTex;

    // headless builds strip shaders, so every effect turns into a no-op there
    public static bool Disabled;
    static Material Sprite()
    {
        var sh = Shader.Find("Sprites/Default");
        return sh ? new Material(sh) : null;
    }

    static Material Add => add ? add : (add = Sprite());

    // radial falloff, optionally shaped into a soft star for muzzle flashes
    static Texture2D Radial(float power, bool star)
    {
        int N = 64; var t = new Texture2D(N, N, TextureFormat.ARGB32, false); var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            var d = new Vector2(x - (N - 1) / 2f, y - (N - 1) / 2f) / (N / 2f);
            float r = d.magnitude, a = Mathf.Pow(Mathf.Clamp01(1 - r), power);
            if (star)
            {
                float ang = Mathf.Atan2(d.y, d.x), spikes = 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(ang * 2));
                a = Mathf.Pow(Mathf.Clamp01(1 - r / spikes), 1.6f) + Mathf.Pow(Mathf.Clamp01(1 - r * 2.6f), 2f);
            }
            px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp; return t;
    }

    static Texture2D Soft => softTex ? softTex : (softTex = Radial(1.7f, false));
    static Texture2D Star => starTex ? starTex : (starTex = Radial(2f, true));
    static Material Puff { get { if (puffMat) return puffMat; puffMat = Sprite(); if (puffMat) puffMat.mainTexture = Soft; return puffMat; } }
    static Material Smoke { get { if (smokeMat) return smokeMat; smokeMat = Sprite(); if (smokeMat) smokeMat.mainTexture = Soft; return smokeMat; } }
    static Material FlashMat { get { if (flashMat) return flashMat; flashMat = Sprite(); if (flashMat) flashMat.mainTexture = Star; return flashMat; } }

    public static void Tracer(Vector3 a, Vector3 b) => Tracer(a, b, new Color(1f, 0.9f, 0.6f));
    public static void Tracer(Vector3 a, Vector3 b, Color c)
    {
        if (Disabled) return;
        var go = new GameObject("Tracer"); var lr = go.AddComponent<LineRenderer>();
        lr.material = Add; lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
        lr.startWidth = 0.055f; lr.endWidth = 0.02f; lr.startColor = c; lr.endColor = new Color(c.r, c.g, c.b, 0.25f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go, 0.075f);
    }

    // A bullet streak that actually flies: a short bright segment travelling from the muzzle to the
    // impact point, the way modern shooters draw tracers, instead of a full-length line flashed for a frame.
    public static void Bullet(Vector3 from, Vector3 to, Color c, float speed = 380f, float streak = 7f)
    {
        if (Disabled) return;
        var go = new GameObject("Tracer"); var lr = go.AddComponent<LineRenderer>();
        lr.material = Add; lr.positionCount = 2; lr.numCapVertices = 2;
        lr.startWidth = 0.035f; lr.endWidth = 0.012f; lr.startColor = c; lr.endColor = new Color(c.r, c.g, c.b, 0f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<TracerFly>().Init(from, to, speed, streak, lr);
    }

    static ParticleSystem Burst(Vector3 p, Vector3 n, Color col, int count, float speed, float size, float life, float grav, float spread = 30, Material mat = null)
    {
        if (Disabled) return null;
        var go = new GameObject("fx"); go.transform.position = p + n * 0.02f; go.transform.rotation = Quaternion.LookRotation(n == Vector3.zero ? Vector3.up : n);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop();
        var main = ps.main; main.duration = 0.3f; main.loop = false; main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life); main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size); main.startColor = col; main.gravityModifier = grav; main.maxParticles = count * 2; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = spread; sh.radius = 0.03f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 1.7f));
        var co = ps.colorOverLifetime; co.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0, 1) }); co.color = g;
        var pr = go.GetComponent<ParticleSystemRenderer>(); pr.material = mat ? mat : Puff; pr.maxParticleSize = 0.16f; pr.minParticleSize = 0f;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
        ps.Play(); Object.Destroy(go, life + 0.5f); return ps;
    }

    public static void Impact(Vector3 p, Vector3 n, bool metal = false)
    {
        Burst(p, n, metal ? new Color(1f, 0.85f, 0.5f, 1f) : new Color(0.72f, 0.6f, 0.45f, 0.8f), metal ? 5 : 8, metal ? 4 : 1.8f, metal ? 0.06f : 0.22f, metal ? 0.25f : 0.6f, 0.5f);
        Sfx.At(Sfx.Pick(metal ? "impact_metal" : "impact_dirt", 0.4f, 4), p, 0.5f, 40);
    }

    public static void Blood(Vector3 p, Vector3 n) => Burst(p, n, new Color(0.45f, 0.03f, 0.02f, 0.9f), 10, 2.2f, 0.13f, 0.5f, 1f, 25);

    public static void Splinters(Vector3 p) => Burst(p, Vector3.up, new Color(0.55f, 0.4f, 0.25f, 1f), 14, 5, 0.1f, 1.1f, 1.5f, 70);

    public static void Explosion(Vector3 p, float radius)
    {
        if (Disabled) { if (Game.I && Game.I.player) Game.I.player.Shake(0); return; }
        // bright core, then dust and slow smoke; all soft sprites so nothing reads as a square
        Burst(p, Vector3.up, new Color(1f, 0.78f, 0.35f, 1f), 12, radius * 1.6f, radius * 0.3f, 0.28f, -0.3f, 90);
        Burst(p, Vector3.up, new Color(1f, 0.45f, 0.12f, 0.9f), 10, radius * 2.4f, radius * 0.22f, 0.4f, -0.1f, 80);
        Burst(p, Vector3.up, new Color(0.7f, 0.6f, 0.45f, 0.55f), 10, radius * 1.2f, radius * 0.3f, 1.3f, 0.25f, 85);
        Burst(p + Vector3.up * 0.5f, Vector3.up, new Color(0.22f, 0.2f, 0.19f, 0.45f), 8, radius * 0.5f, radius * 0.38f, 2.2f, -0.06f, 55, Smoke);
        Fireball(p + Vector3.up * 0.5f, radius * 1.25f);
        var lg = new GameObject("boomlight"); lg.transform.position = p + Vector3.up; var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = radius * 5; l.intensity = 6; l.color = new Color(1, 0.6f, 0.3f);
        Object.Destroy(lg, 0.18f);
        Sfx.At(Sfx.Pick("explosion", 2.5f, 2), p, 1f, 250);
        if (Game.I && Game.I.player) Game.I.player.Shake(Mathf.Clamp01(1.5f - Vector3.Distance(p, Game.I.player.transform.position) / 25f));
    }

    // short-lived billboard that expands and fades: the bright core of a blast
    static void Fireball(Vector3 p, float size)
    {
        if (Disabled) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = p;
        var r = go.GetComponent<Renderer>(); r.material = new Material(FlashMat) { color = new Color(1f, 0.82f, 0.45f, 1f) };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        go.AddComponent<Billboard>().Init(size, 0.45f);
    }

    // soft sprite material for other systems (weather dust)
    public static Material SoftSprite => Puff;

    // small brass casing tossed out of a mounted gun
    public static void Casing(Vector3 p, Vector3 dir)
    {
        if (Disabled) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = p; go.transform.localScale = new Vector3(0.015f, 0.015f, 0.04f); go.transform.rotation = Random.rotation;
        var r = go.GetComponent<Renderer>(); r.material.color = new Color(0.72f, 0.58f, 0.24f); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.02f; rb.linearVelocity = dir.normalized * Random.Range(2f, 3.5f); rb.angularVelocity = Random.insideUnitSphere * 12;
        Object.Destroy(go, 2.5f);
    }

    public static Light MuzzleLight(Transform parent, Vector3 local)
    {
        if (Disabled) return null;
        var mg = new GameObject("MuzzleLight"); mg.transform.SetParent(parent, false); mg.transform.localPosition = local;
        var l = mg.AddComponent<Light>(); l.type = LightType.Point; l.range = 7; l.color = new Color(1, 0.72f, 0.38f); l.intensity = 0; return l;
    }

    // soft star at the muzzle, visible for a couple of frames
    public static void Flash(Vector3 p, Vector3 fwd, float size = 0.25f)
    {
        if (Disabled) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = p; go.transform.rotation = Quaternion.LookRotation(Camera.main ? Camera.main.transform.position - p : -fwd) * Quaternion.Euler(0, 0, Random.Range(0, 360));
        go.transform.localScale = Vector3.one * size * Random.Range(1.4f, 2.1f);
        var r = go.GetComponent<Renderer>(); r.material = new Material(FlashMat) { color = new Color(1f, 0.82f, 0.5f, 0.85f) };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        Object.Destroy(go, 0.05f);
    }
}

// expanding, fading, camera-facing sprite
public class Billboard : MonoBehaviour
{
    float t, life, size; Renderer r; Color c0;
    public void Init(float s, float l) { size = s; life = l; r = GetComponent<Renderer>(); c0 = r.material.color; transform.localScale = Vector3.one * s * 0.4f; }
    void LateUpdate()
    {
        t += Time.deltaTime; float k = t / life;
        if (k >= 1) { Destroy(gameObject); return; }
        if (Camera.main) transform.rotation = Quaternion.LookRotation(Camera.main.transform.position - transform.position);
        transform.localScale = Vector3.one * size * Mathf.Lerp(0.45f, 1.5f, k);
        r.material.color = new Color(c0.r, c0.g, c0.b, c0.a * (1 - k) * (1 - k));
    }
}

// moves a tracer streak from muzzle to target and fades it out
public class TracerFly : MonoBehaviour
{
    Vector3 from, dir; float dist, travelled, speed, streak; LineRenderer lr;
    public void Init(Vector3 a, Vector3 b, float sp, float st, LineRenderer l)
    {
        from = a; dir = (b - a); dist = dir.magnitude; dir = dist > 0.001f ? dir / dist : Vector3.forward;
        speed = sp; streak = st; lr = l; Apply();
    }
    void Apply()
    {
        float head = Mathf.Min(travelled, dist), tail = Mathf.Max(0, head - streak);
        lr.SetPosition(0, from + dir * tail); lr.SetPosition(1, from + dir * head);
    }
    void Update()
    {
        travelled += speed * Time.deltaTime; Apply();
        if (travelled - streak > dist) Destroy(gameObject);
    }
}
