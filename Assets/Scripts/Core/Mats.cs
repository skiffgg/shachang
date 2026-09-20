using UnityEngine;

// Material helper that survives a dedicated-server build, where every shader is stripped and
// `new Material(Shader.Find(...))` would throw while the world is still being generated.
// On a server the game only needs colliders, terrain heights and the navmesh, so a null material
// is perfectly fine — nothing renders there.
public static class Mats
{
    public static Material New(string shader)
    {
        var s = Shader.Find(shader);
        return s ? new Material(s) : null;
    }

    public static Material New(string shader, Color color)
    {
        var m = New(shader);
        if (m) m.color = color;
        return m;
    }

    // chainable, null-safe setters so call sites stay one-liners
    public static Material F(this Material m, string prop, float v) { if (m && m.HasProperty(prop)) m.SetFloat(prop, v); return m; }
    public static Material C(this Material m, string prop, Color v) { if (m && m.HasProperty(prop)) m.SetColor(prop, v); return m; }
    public static Material T(this Material m, string prop, Texture v) { if (m && m.HasProperty(prop)) m.SetTexture(prop, v); return m; }
    public static Material Tex(this Material m, Texture v) { if (m) m.mainTexture = v; return m; }
    public static Material Tile(this Material m, Vector2 v) { if (m) m.mainTextureScale = v; return m; }
}
