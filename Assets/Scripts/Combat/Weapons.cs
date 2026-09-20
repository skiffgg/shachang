using System.Collections.Generic;
using UnityEngine;

public class WeaponDef
{
    public string id, name; public int mag, reserve; public float rate, dmg, hipSpread, adsSpread, reload, recoil, adsFov; public int pellets = 1; public bool auto, rocket, scope;
}

public static class Weapons
{
    public static readonly WeaponDef[] All =
    {
        new WeaponDef { id = "rifle", name = "M16 突击步枪", mag = 30, reserve = 150, rate = 0.095f, dmg = 34, hipSpread = 0.028f, adsSpread = 0.004f, reload = 2.0f, recoil = 0.9f, adsFov = 44, auto = true },
        new WeaponDef { id = "shotgun", name = "M4 霰弹枪", mag = 8, reserve = 40, rate = 0.8f, dmg = 16, pellets = 9, hipSpread = 0.07f, adsSpread = 0.05f, reload = 2.4f, recoil = 2.6f, adsFov = 54 },
        new WeaponDef { id = "sniper", name = "巴雷特狙击枪", mag = 5, reserve = 25, rate = 1.25f, dmg = 170, hipSpread = 0.07f, adsSpread = 0f, reload = 2.8f, recoil = 3.5f, adsFov = 14, scope = true },
        new WeaponDef { id = "smg", name = "冲锋枪", mag = 35, reserve = 175, rate = 0.066f, dmg = 22, hipSpread = 0.035f, adsSpread = 0.01f, reload = 1.7f, recoil = 0.6f, adsFov = 47, auto = true },
        new WeaponDef { id = "rpg", name = "RPG-7 火箭筒", mag = 1, reserve = 6, rate = 1f, dmg = 0, hipSpread = 0.01f, adsSpread = 0.003f, reload = 2.8f, recoil = 3f, adsFov = 40, rocket = true },
    };
}

// Attachments for the M16 (B). Unlocked in the shop / picked up; each changes handling.
[System.Serializable]
public class Loadout { public bool holo, acog, silencer, grip, laser; }

// ---------------------------------------------------------------- view models
public abstract class ViewModel
{
    public Transform root; public Vector3 muzzleLocal; public float animBusy;
    public virtual void ApplyLoadout(Loadout lo) { }
    public virtual bool Optics => false;
    public abstract void Tick(float dt, Transform cam);
    public virtual void OnReload(float dur) { }
    public virtual void OnEquip() { }
    public virtual void OnFire() { }
    public Vector3 Muzzle => root.TransformPoint(muzzleLocal);
    public void Show(bool on) { root.gameObject.SetActive(on); }
}

// Animated M16 pack (arms + rifle authored together): idle / reload / equip windows of one timeline
public class PackView : ViewModel
{
    Animation anim; AnimationState st; string mode = "equip"; float clock, reloadDur = 2;
    const float IDLE_A = 5.9f, IDLE_B = 6.8f, RELOAD_A = 0f, RELOAD_B = 2.3f, EQUIP_A = 6.8f, EQUIP_B = 7.6f;
    public Transform baseNode; readonly Dictionary<string, GameObject> atts = new Dictionary<string, GameObject>();

    public PackView(Transform parent)
    {
        root = new GameObject("PackView").transform; root.SetParent(parent, false);
        var src = Game.Model("m16fp/m16fp"); if (!src) return;
        var fp = Object.Instantiate(src, root).transform;
        fp.localRotation = Quaternion.Euler(Tune.I.fpRot); fp.localPosition = Tune.I.fpPos; fp.localScale = Vector3.one * Tune.I.fpScale;
        foreach (var r in fp.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var smr in fp.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
        anim = fp.GetComponentInChildren<Animation>();
        if (anim && anim.clip) { st = anim[anim.clip.name]; st.enabled = true; st.weight = 1; st.speed = 0; }
        foreach (var t in fp.GetComponentsInChildren<Transform>()) if (t.name == "m16a2_base") baseNode = t;
        muzzleLocal = Tune.I.fpPos + new Vector3(0, 0.1f, 0.95f);
        Sample(IDLE_A);
    }

    void Sample(float t) { if (st == null) return; st.time = t; anim.Sample(); }

    public override void OnReload(float dur) { mode = "reload"; clock = 0; reloadDur = dur; animBusy = dur; }
    public override void OnEquip() { mode = "equip"; clock = 0; animBusy = 0.55f; }

    public override void Tick(float dt, Transform cam)
    {
        clock += dt; animBusy -= dt; float t;
        if (mode == "reload") { float k = clock / reloadDur; t = Mathf.Lerp(RELOAD_A, RELOAD_B, Mathf.Clamp01(k)); if (k >= 1) { mode = "idle"; clock = 0; } }
        else if (mode == "equip") { float k = clock / 0.55f; t = Mathf.Lerp(EQUIP_A, EQUIP_B, Mathf.Clamp01(k)); if (k >= 1) { mode = "idle"; clock = 0; } }
        else t = IDLE_A + Mathf.PingPong(clock * 0.5f, IDLE_B - IDLE_A);
        Sample(t);
    }

    // mount attachments once, in the idle pose, then parent them to the animated receiver so they follow reloads
    public override bool Optics => opticsOn;
    bool opticsOn;

    public override void ApplyLoadout(Loadout lo)
    {
        opticsOn = lo.holo || lo.acog;
        if (!baseNode) return;
        Sample(IDLE_A); root.gameObject.SetActive(true);
        var mr = baseNode.GetComponentInChildren<Renderer>(); if (!mr) return;
        // gun bounds in the view root's space
        var b = mr.bounds; var c = root.InverseTransformPoint(b.center);
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++) { var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)); var l = root.InverseTransformPoint(corner); min = Vector3.Min(min, l); max = Vector3.Max(max, l); }
        var T = Tune.I;
        Set("holo", lo.holo && !lo.acog, "att_holo", new Vector3(c.x, max.y, Mathf.Lerp(min.z, max.z, 0.55f)) + T.attHolo);
        Set("acog", lo.acog, "att_acog", new Vector3(c.x, max.y, Mathf.Lerp(min.z, max.z, 0.55f)) + T.attAcog);
        Set("silencer", lo.silencer, "att_silencer", new Vector3(c.x, Mathf.Lerp(min.y, max.y, 0.62f), max.z) + T.attSilencer);
        Set("grip", lo.grip, "att_grip", new Vector3(c.x, min.y, Mathf.Lerp(min.z, max.z, 0.75f)) + T.attGrip);
        Set("laser", lo.laser, "att_laser", new Vector3(max.x, Mathf.Lerp(min.y, max.y, 0.55f), Mathf.Lerp(min.z, max.z, 0.8f)) + T.attLaser);
        muzzleLocal = new Vector3(c.x, Mathf.Lerp(min.y, max.y, 0.62f), max.z + (lo.silencer ? 0.17f : 0.02f));
    }

    void Set(string key, bool on, string model, Vector3 localPos)
    {
        if (atts.TryGetValue(key, out var old)) { Object.Destroy(old); atts.Remove(key); }
        if (!on) return;
        var src = Game.Model(model); if (!src) return;
        var g = Object.Instantiate(src); g.name = key;
        g.transform.SetParent(root, false); g.transform.localPosition = localPos; g.transform.localRotation = Quaternion.Euler(0, 180, 0);
        foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.transform.SetParent(baseNode, true); atts[key] = g;
    }
}

// Other weapons: real weapon model + FPS arms posed every frame by two-bone IK onto the grips
public class ArmsView : ViewModel
{
    readonly Transform model, arms; readonly WeaponTune wt;
    readonly Dictionary<string, Transform> B = new Dictionary<string, Transform>();
    readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
    Transform shR, elR, wrR, midR, thR, shL, elL, wrL, midL, thL; readonly List<Transform> fingersR = new List<Transform>(), fingersL = new List<Transform>();
    float reloadT, reloadDur = 1, equipT;

    public ArmsView(Transform parent, string id)
    {
        wt = Tune.I.Weapon(id);
        root = new GameObject("ArmsView_" + id).transform; root.SetParent(parent, false);
        root.localPosition = Tune.I.holderPos;
        var src = Game.Model("w_" + id);
        if (src)
        {
            model = Object.Instantiate(src, root).transform; model.localPosition = wt.modelPos; model.localRotation = Quaternion.Euler(0, 180, 0); model.localScale = Vector3.one * wt.scale;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (id == "smg") foreach (var r in model.GetComponentsInChildren<Renderer>()) foreach (var m in r.materials) { m.color = new Color(0.12f, 0.12f, 0.13f); }
        }
        muzzleLocal = wt.muzzle;
        var asrc = Game.Model("w_arms");
        if (asrc)
        {
            arms = Object.Instantiate(asrc, parent).transform; arms.name = "Arms";
            foreach (var r in arms.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var smr in arms.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            foreach (var t in arms.GetComponentsInChildren<Transform>()) { B[t.name] = t; rest[t] = t.localRotation; }
            PlaceArms(parent);
            arms.SetParent(root, true);
        }
    }

    Transform Bn(string n) { B.TryGetValue(n, out var t); return t; }

    // orient/position the rig so both shoulders sit just below and behind the camera; decide which arm is right by position
    void PlaceArms(Transform cam)
    {
        var a = Bn("L_arm"); var b = Bn("R_arm"); if (!a || !b) return;
        arms.localScale = Vector3.one * Tune.I.armsScale;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var wA = Bn(a.name == "L_arm" ? "L_wrist" : "R_wrist"); var wB = Bn(b.name == "R_arm" ? "R_wrist" : "L_wrist");
            var mid = (a.position + b.position) / 2; var reach = ((wA.position + wB.position) / 2 - mid); reach.y = 0;
            var want = cam.forward; want.y = 0;
            arms.rotation = Quaternion.FromToRotation(reach.normalized, want.normalized) * arms.rotation;
        }
        var mid2 = (a.position + b.position) / 2;
        arms.position += cam.TransformPoint(Tune.I.shoulderPos) - mid2;
        bool aRight = cam.InverseTransformPoint(a.position).x > 0;
        string R = aRight ? "L_" : "R_", L = aRight ? "R_" : "L_";
        shR = Bn(R + "arm"); elR = Bn(R + "elbow"); wrR = Bn(R + "wrist"); midR = Bn(R + "middle1"); thR = Bn(R + "thumb1");
        shL = Bn(L + "arm"); elL = Bn(L + "elbow"); wrL = Bn(L + "wrist"); midL = Bn(L + "middle1"); thL = Bn(L + "thumb1");
        foreach (var f in new[] { "point", "middle", "ring", "pink" }) for (int k = 1; k <= 3; k++) { var r = Bn(R + f + k); if (r) fingersR.Add(r); var l = Bn(L + f + k); if (l) fingersL.Add(l); }
        foreach (var kv in B) rest[kv.Value] = kv.Value.localRotation;
    }

    public override void OnReload(float dur) { reloadT = dur; reloadDur = dur; animBusy = dur; }
    public override void OnEquip() { equipT = 0.45f; animBusy = 0.45f; }

    // ---- attachments (shotgun / SMG get optics, silencer and laser too).
    // Sizes and rail positions are derived from each weapon's own bounds, so the same parts fit any gun.
    readonly Dictionary<string, GameObject> atts = new Dictionary<string, GameObject>();
    bool opticsOn;
    public override bool Optics => opticsOn;

    static System.Collections.Generic.List<string> rs2(Transform t)
    {
        var l = new System.Collections.Generic.List<string>();
        foreach (var r in t.GetComponentsInChildren<Renderer>()) l.Add(" [" + r.name + " " + r.bounds.size.ToString("0.00") + "]");
        return l;
    }

    // align: per axis, +1 = the part's near side sits on the anchor, -1 = its far side, 0 = centred.
    // Sights bolt their base onto the rail, the silencer butts against the muzzle, the laser hugs the side.
    void Set(string key, bool on, string modelName, Vector3 anchor, Vector3 align, float targetSize)
    {
        if (atts.TryGetValue(key, out var old)) { Object.Destroy(old); atts.Remove(key); }
        if (!on || !model) return;
        var src = Game.Model(modelName); if (!src) return;
        var g = Object.Instantiate(src, model); g.name = key;
        g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
        foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var wb = Game.WorldBounds(g); float cur = Mathf.Max(wb.size.x, Mathf.Max(wb.size.y, wb.size.z));
        if (cur < 0.0005f) { atts[key] = g; return; }
        g.transform.localScale = Vector3.one * Mathf.Clamp(targetSize * model.lossyScale.x / cur, 0.05f, 20f);

        wb = Game.WorldBounds(g);
        Vector3 a1 = model.InverseTransformPoint(wb.min), a2 = model.InverseTransformPoint(wb.max);
        Vector3 lo2 = Vector3.Min(a1, a2), hi = Vector3.Max(a1, a2), centre = (lo2 + hi) / 2, ext = (hi - lo2) / 2;
        var want = anchor + new Vector3(align.x * ext.x, align.y * ext.y, align.z * ext.z);
        g.transform.localPosition += want - centre;
        atts[key] = g;
    }

    public override void ApplyLoadout(Loadout lo)
    {
        opticsOn = lo.holo || lo.acog;
        if (!model) return;
        var rs = model.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { muzzleLocal = wt.muzzle; return; }
        var wb = rs[0].bounds; foreach (var r in rs) wb.Encapsulate(r.bounds);
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var l = model.InverseTransformPoint(corner); min = Vector3.Min(min, l); max = Vector3.Max(max, l);
        }
        float len = Mathf.Max(0.05f, max.z - min.z), cx = (min.x + max.x) / 2;
        float rail = max.y, barrel = Mathf.Lerp(min.y, max.y, 0.55f);
        if (Game.I && Game.I.attDebug)
        {
            string dbg = ""; foreach (var r in rs2(model)) dbg += r;
            Debug.LogError("ATT " + wt.id + " min=" + min.ToString("0.000") + " max=" + max.ToString("0.000") + " len=" + len.ToString("0.000") +
                           " modelLocalScale=" + model.localScale.ToString("0.00") + " lossy=" + model.lossyScale.ToString("0.00") + " renderers:" + dbg);
        }
        var sight = new Vector3(cx, rail - len * 0.01f, Mathf.Lerp(min.z, max.z, 0.42f)) + wt.attSight;
        Set("holo", lo.holo && !lo.acog, "att_holo", sight, new Vector3(0, 1, 0), len * 0.115f);
        Set("acog", lo.acog, "att_acog", sight, new Vector3(0, 1, 0), len * 0.18f);
        Set("silencer", lo.silencer, "att_silencer", new Vector3(cx, barrel, max.z - len * 0.02f) + wt.attSilencer, new Vector3(0, 0, 1), len * 0.2f);
        Set("laser", lo.laser, "att_laser", new Vector3(max.x - len * 0.005f, barrel, Mathf.Lerp(min.z, max.z, 0.72f)) + wt.attLaser, new Vector3(1, 0, 0), len * 0.075f);
        muzzleLocal = new Vector3(wt.muzzle.x, wt.muzzle.y, wt.muzzle.z + (lo.silencer ? len * 0.2f : 0f));
    }

    public override void Tick(float dt, Transform cam)
    {
        // procedural reload: dip and roll the weapon; equip: raise from below
        reloadT = Mathf.Max(0, reloadT - dt); equipT = Mathf.Max(0, equipT - dt); animBusy -= dt;
        float rk = reloadT > 0 ? Mathf.Sin((1 - reloadT / reloadDur) * Mathf.PI) : 0, ek = equipT / 0.45f;
        root.localPosition = Tune.I.holderPos + new Vector3(0, -0.12f * rk - 0.3f * ek, 0);
        root.localRotation = Quaternion.Euler(18 * rk + 40 * ek, 0, -25 * rk);
        if (!shR) return;
        foreach (var kv in rest) kv.Key.localRotation = kv.Value;
        IK(shR, elR, wrR, root.TransformPoint(wt.R), root.TransformPoint(wt.R + Tune.I.poleR));
        Hand(wrR, midR, thR, root.TransformDirection(Tune.I.handRf), root.TransformDirection(Tune.I.handRt));
        IK(shL, elL, wrL, root.TransformPoint(wt.L), root.TransformPoint(wt.L + Tune.I.poleL));
        Hand(wrL, midL, thL, root.TransformDirection(Tune.I.handLf), root.TransformDirection(Tune.I.handLt));
        foreach (var f in fingersR) f.localRotation *= Quaternion.AngleAxis(Tune.I.curlR, Tune.I.curlAxis);
        foreach (var f in fingersL) f.localRotation *= Quaternion.AngleAxis(Tune.I.curlL, Tune.I.curlAxis);
    }

    static void Aim(Transform bone, Transform child, Vector3 to)
    {
        var cur = (child.position - bone.position).normalized; var want = (to - bone.position).normalized;
        if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
    }

    static void IK(Transform sh, Transform el, Transform wr, Vector3 T, Vector3 pole)
    {
        if (!sh || !el || !wr) return;
        var A = sh.position; float la = Vector3.Distance(A, el.position), lb = Vector3.Distance(el.position, wr.position);
        float d = Mathf.Min(Vector3.Distance(A, T), la + lb - 1e-4f); var u = (T - A).normalized;
        float ca = Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1, 1), sa = Mathf.Sqrt(1 - ca * ca);
        var v = pole - A; v -= u * Vector3.Dot(v, u); v.Normalize();
        Aim(sh, el, A + u * la * ca + v * la * sa); Aim(el, wr, A + u * d);
    }

    static float Roll(Transform wr, Transform th, Vector3 f, Vector3 tdir)
    {
        var tc = th.position - wr.position; tc -= f * Vector3.Dot(tc, f); var tw = tdir - f * Vector3.Dot(tdir, f);
        return Vector3.SignedAngle(tc, tw, f);
    }

    static void Hand(Transform wr, Transform mid, Transform th, Vector3 fwd, Vector3 tdir)
    {
        if (!wr || !mid || !th) return; var f = fwd.normalized; var el = wr.parent; var fa = (wr.position - el.position).normalized;
        Aim(wr, mid, wr.position + f);
        el.rotation = Quaternion.AngleAxis(Roll(wr, th, f, tdir) * 0.7f, fa) * el.rotation;
        Aim(wr, mid, wr.position + f);
        wr.rotation = Quaternion.AngleAxis(Roll(wr, th, f, tdir), f) * wr.rotation;
    }
}
