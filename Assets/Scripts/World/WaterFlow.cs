using UnityEngine;

// Moving water: two ripple layers scrolling against each other on a generated normal map, plus a slow
// swell on the surface itself. Enough to read as living water without a custom shader.
public class WaterFlow : MonoBehaviour
{
    static Texture2D ripple;
    Material mat;
    float t;

    static Texture2D Ripple()
    {
        if (ripple) return ripple;
        int N = 128; ripple = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "RippleNormal" };
        var px = new Color[N * N];
        // a tileable height field of crossing wavelets, converted to a normal map
        System.Func<int, int, float> h = (x, y) =>
        {
            float u = x / (float)N * Mathf.PI * 2, v = y / (float)N * Mathf.PI * 2;
            return Mathf.Sin(u * 3 + Mathf.Cos(v * 2) * 0.6f) * 0.5f
                 + Mathf.Sin(v * 5 - Mathf.Sin(u * 3) * 0.4f) * 0.3f
                 + Mathf.Sin((u + v) * 7) * 0.12f;
        };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float dx = h((x + 1) % N, y) - h((x - 1 + N) % N, y);
            float dy = h(x, (y + 1) % N) - h(x, (y - 1 + N) % N);
            var n = new Vector3(-dx * 1.6f, -dy * 1.6f, 1f).normalized;
            px[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }
        ripple.SetPixels(px); ripple.Apply(); return ripple;
    }

    static Texture2D caustics;

    // soft bright cells that drift across the surface, so the water is not one flat colour
    static Texture2D Caustics()
    {
        if (caustics) return caustics;
        int N = 128; caustics = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "WaterCaustics" };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float u = x / (float)N * Mathf.PI * 2, v = y / (float)N * Mathf.PI * 2;
            float c = Mathf.Abs(Mathf.Sin(u * 2 + Mathf.Sin(v * 3) * 0.8f)) * Mathf.Abs(Mathf.Sin(v * 2 + Mathf.Cos(u * 3) * 0.8f));
            c = Mathf.Pow(c, 2.2f);
            float g = 0.72f + c * 0.5f;
            px[y * N + x] = new Color(g * 0.8f, g, g * 1.05f, 1f);
        }
        caustics.SetPixels(px); caustics.Apply(); return caustics;
    }

    void Start()
    {
        var r = GetComponent<Renderer>(); if (!r || Game.Headless) { enabled = false; return; }
        mat = r.material; if (!mat) { enabled = false; return; }   // own instance, so each body of water drifts on its own
        mat.mainTexture = Caustics();
        if (mat.HasProperty("_BumpMap")) { mat.SetTexture("_BumpMap", Ripple()); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 0.9f); }
        mat.mainTextureScale = new Vector2(transform.localScale.x * 0.12f, transform.localScale.z * 0.12f);
        mat.SetTextureScale("_BumpMap", mat.mainTextureScale);
        t = Random.value * 10f;
    }

    void Update()
    {
        if (!mat) return;
        t += Time.deltaTime;
        // two layers drifting in different directions read as a current
        var a = new Vector2(t * 0.035f, t * 0.021f);
        var b = new Vector2(-t * 0.017f, t * 0.033f);
        mat.SetTextureOffset("_BumpMap", a);
        mat.mainTextureOffset = b;
        // gentle swell, and a colour that breathes with the light
        var s = transform.localScale;
        transform.localScale = new Vector3(s.x, s.y, s.z);
        float k = 0.5f + 0.5f * Mathf.Sin(t * 0.6f);
        mat.color = Color.Lerp(new Color(0.10f, 0.30f, 0.35f, 0.86f), new Color(0.14f, 0.36f, 0.42f, 0.9f), k);
    }
}
