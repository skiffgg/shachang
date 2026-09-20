using System.Collections.Generic;
using UnityEngine;

// Recorded (Freesound CC0) sounds. Long recordings are cut into single hits by onset detection at load:
// one gunshot out of a burst, one footstep out of a walk, one casing bounce out of a series.
public static class Sfx
{
    public static bool Disabled;

    static readonly Dictionary<string, AudioClip> raw = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, List<AudioClip>> cuts = new Dictionary<string, List<AudioClip>>();
    static AudioSource ui;
    public static float volume = 0.9f;

    public static AudioClip Raw(string name)
    {
        if (!raw.TryGetValue(name, out var c)) { c = Resources.Load<AudioClip>("Audio/" + name); raw[name] = c; if (!c) Debug.LogWarning("missing audio " + name); }
        return c;
    }

    // mono float samples of a clip
    static float[] Mono(AudioClip c, out int rate)
    {
        rate = c.frequency; var d = new float[c.samples * c.channels]; c.GetData(d, 0);
        if (c.channels == 1) return d;
        var m = new float[c.samples]; for (int i = 0; i < c.samples; i++) { float s = 0; for (int k = 0; k < c.channels; k++) s += d[i * c.channels + k]; m[i] = s / c.channels; }
        return m;
    }

    // cut a recording into individual hits: find energy onsets, keep `len` seconds after each (with short fade)
    public static List<AudioClip> Cuts(string name, float len, int max = 8, float minGap = 0.12f, float thresh = 0.35f)
    {
        string key = name + len;
        if (cuts.TryGetValue(key, out var list)) return list;
        list = new List<AudioClip>(); cuts[key] = list;
        var c = Raw(name); if (!c) return list;
        var m = Mono(c, out int rate);
        int win = rate / 100; float peak = 0;
        var env = new float[m.Length / win];
        for (int w = 0; w < env.Length; w++) { float s = 0; for (int i = 0; i < win; i++) s += Mathf.Abs(m[w * win + i]); env[w] = s / win; peak = Mathf.Max(peak, env[w]); }
        int gap = Mathf.RoundToInt(minGap * 100), last = -9999;
        for (int w = 1; w < env.Length && list.Count < max; w++)
        {
            if (env[w] > peak * thresh && env[w] > env[w - 1] * 1.8f && w - last > gap)
            {
                last = w; int start = Mathf.Max(0, w * win - win), n = Mathf.Min(Mathf.RoundToInt(len * rate), m.Length - start);
                if (n < rate / 20) continue;
                var seg = new float[n]; float g = 0;
                for (int i = 0; i < n; i++) { seg[i] = m[start + i]; g = Mathf.Max(g, Mathf.Abs(seg[i])); }
                float norm = g > 0 ? 0.9f / g : 1;
                for (int i = 0; i < n; i++) { float t = i / (float)n; float fade = t < 0.01f ? t / 0.01f : t > 0.8f ? (1 - t) / 0.2f : 1; seg[i] *= norm * fade; }
                var clip = AudioClip.Create(name + "_" + list.Count, n, 1, rate, false); clip.SetData(seg, 0); list.Add(clip);
            }
        }
        if (list.Count == 0) list.Add(c);
        return list;
    }

    public static AudioClip Pick(string name, float len, int max = 8) { var l = Cuts(name, len, max); return l[Random.Range(0, l.Count)]; }

    // positional one-shot with pitch variation
    public static void At(AudioClip c, Vector3 pos, float vol = 1, float maxDist = 120, float pitch = 1)
    {
        if (Disabled) return;
        if (!c) return;
        var go = new GameObject("sfx"); go.transform.position = pos;
        var s = go.AddComponent<AudioSource>(); s.clip = c; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2; s.maxDistance = maxDist;
        s.volume = vol * volume; s.pitch = pitch * Random.Range(0.95f, 1.05f); s.dopplerLevel = 0; s.Play();
        Object.Destroy(go, c.length / Mathf.Max(0.3f, s.pitch) + 0.1f);
    }

    public static void Ui(AudioClip c, float vol = 1, float pitch = 1)
    {
        if (Disabled) return;
        if (!c) return;
        if (!ui) { var go = new GameObject("ui-audio"); Object.DontDestroyOnLoad(go); ui = go.AddComponent<AudioSource>(); }
        ui.pitch = pitch; ui.PlayOneShot(c, vol * volume);
    }

    // looping ambience / engines
    public static AudioSource Loop(Transform parent, string name, float vol, bool spatial)
    {
        var s = parent.gameObject.AddComponent<AudioSource>(); s.clip = Raw(name); s.loop = true; s.volume = vol * volume; s.spatialBlend = spatial ? 1 : 0;
        s.rolloffMode = AudioRolloffMode.Linear; s.maxDistance = 90; if (s.clip) s.Play(); return s;
    }

    public static AudioClip Shot(string weapon, bool silenced)
    {
        if (silenced) return Pick("silenced", 0.5f);
        switch (weapon)
        {
            case "shotgun": return Pick("shotgun", 0.9f, 2);
            case "sniper": return Pick("sniper", 1.2f, 2);
            case "smg": return Pick("smg", 0.35f);
            case "rpg": return Raw("rpg");
            default: return Pick("rifle2", 0.45f);
        }
    }
}
