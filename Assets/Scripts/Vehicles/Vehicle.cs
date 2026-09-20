using System.Collections.Generic;
using UnityEngine;

// Drivable vehicles (jeep, bike, armed pickup, helicopter) and the enemy tank. Arcade physics on terrain.
public class Vehicle : MonoBehaviour
{
    public static readonly List<Vehicle> All = new List<Vehicle>();
    public bool occupied, enemy, flying, dead, armed; public string kind, displayName;
    public float hp, maxHp;
    float speed, steer, vy, gunCool, cannonCool; public float rpm;
    Transform mgYaw, mgPitch, mgMuzzle; float mgKick;
    BoxCollider box; AudioSource engine; Transform rotor, tail, vis; Vector3 vel;
    public float Altitude => transform.position.y - World.Height(transform.position);

    public static Vehicle Spawn(string kind, Vector3 pos, float yaw)
    {
        var go = new GameObject("Vehicle_" + kind); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var v = go.AddComponent<Vehicle>(); v.Init(kind); return v;
    }

    void Init(string k)
    {
        kind = k; All.Add(this);
        string model = k == "bike" ? "v_bike" : k == "heli" ? "v_heli" : k == "tank" ? "v_tank" : "v_jeep";
        vis = Instantiate(Game.Model(model), transform).transform; vis.localRotation = Quaternion.Euler(0, Tune.I.vehicleYaw, 0);
        foreach (var r in vis.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        box = Game.AddBoundsCollider(gameObject, 0.95f);
        maxHp = hp = k == "bike" ? 80 : k == "heli" ? 400 : k == "tank" ? 900 : k == "pickup" ? 320 : 250;
        displayName = k == "bike" ? "越野摩托" : k == "heli" ? "直升机" : k == "pickup" ? "武装皮卡" : k == "tank" ? "坦克" : "悍马";
        armed = k == "pickup" || k == "heli";
        enemy = k == "tank";
        if (k == "heli")
        {
            var ts = new List<Transform>(vis.GetComponentsInChildren<Transform>()).FindAll(t => t.name.StartsWith("Rotor") && !t.parent.name.StartsWith("Rotor"));
            ts.Sort((a, b) => b.position.y.CompareTo(a.position.y)); if (ts.Count > 0) rotor = ts[0]; if (ts.Count > 1) tail = ts[1];
        }
        if (k == "pickup") BuildMG(new Vector3(0, 1.98f, 0.15f));
        // the imported vehicle meshes carry a plain white untextured sub-material that reads as a
        // glowing box on the roof; darken it so it looks like painted metal
        foreach (var r in vis.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)
            {
                bool tex = (m.HasProperty("_MainTex") && m.mainTexture) || (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture"));
                if (tex) continue;
                var col = m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : m.HasProperty("_Color") ? m.color : Color.black;
                if (col.r < 0.7f || col.g < 0.7f || col.b < 0.7f) continue;
                var dark = new Color(0.22f, 0.22f, 0.2f);
                if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", dark);
                if (m.HasProperty("_Color")) m.color = dark;
            }
        engine = Sfx.Loop(transform, k == "bike" ? "motorbike" : k == "heli" ? "heli" : "jeep", 0, true);
    }

    // pintle-mounted .50 cal: base, shield, receiver, barrel, ammo box and belt — built from primitives
    void BuildMG(Vector3 local)
    {
        var metal = Mats.New("Standard", new Color(0.11f, 0.11f, 0.12f)).F("_Metallic", 0.3f).F("_Glossiness", 0.18f);
        var olive = Mats.New("Standard", new Color(0.21f, 0.23f, 0.16f)).F("_Metallic", 0.2f).F("_Glossiness", 0.2f);

        System.Func<PrimitiveType, Transform, Vector3, Vector3, Vector3, Material, Transform> Part = (t, par, pos, rot, sc, m) =>
        {
            var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>()); g.name = "MG";
            g.transform.SetParent(par, false); g.transform.localPosition = pos; g.transform.localEulerAngles = rot; g.transform.localScale = sc;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return g.transform;
        };

        mgYaw = new GameObject("MGYaw").transform; mgYaw.SetParent(transform, false); mgYaw.localPosition = local;
        Part(PrimitiveType.Cube, mgYaw, new Vector3(0, 0.03f, 0), Vector3.zero, new Vector3(0.62f, 0.14f, 0.62f), olive);   // hatch cover over the roof ring
        // stowage crate: also hides the white box the Humvee model has painted on its roof
        var crate = new GameObject("MGStow").transform; crate.SetParent(transform, false); crate.localPosition = new Vector3(-0.26f, 2.4f, 0.12f);
        Part(PrimitiveType.Cube, crate, Vector3.zero, Vector3.zero, new Vector3(0.42f, 0.36f, 0.5f), olive);
        Part(PrimitiveType.Cube, crate, new Vector3(0, 0.2f, 0), Vector3.zero, new Vector3(0.44f, 0.05f, 0.52f), metal);
        Part(PrimitiveType.Cylinder, mgYaw, new Vector3(0, 0.12f, 0), Vector3.zero, new Vector3(0.14f, 0.3f, 0.14f), metal);      // pintle post
        Part(PrimitiveType.Cylinder, mgYaw, new Vector3(0, 0.44f, 0), Vector3.zero, new Vector3(0.1f, 0.06f, 0.1f), metal);       // swivel
        Part(PrimitiveType.Cylinder, mgYaw, new Vector3(0, -0.02f, 0), Vector3.zero, new Vector3(0.5f, 0.03f, 0.5f), metal);      // ring base
        Part(PrimitiveType.Cube, mgYaw, new Vector3(0, 0.62f, 0.34f), new Vector3(-10, 0, 0), new Vector3(0.5f, 0.4f, 0.03f), olive);  // gun shield
        Part(PrimitiveType.Cube, mgYaw, new Vector3(-0.38f, 0.58f, 0.32f), new Vector3(-10, 22, 0), new Vector3(0.2f, 0.36f, 0.035f), olive);
        Part(PrimitiveType.Cube, mgYaw, new Vector3(0.38f, 0.58f, 0.32f), new Vector3(-10, -22, 0), new Vector3(0.2f, 0.36f, 0.035f), olive);

        mgPitch = new GameObject("MGPitch").transform; mgPitch.SetParent(mgYaw, false); mgPitch.localPosition = new Vector3(0, 0.5f, 0);
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0, 0, -0.1f), Vector3.zero, new Vector3(0.14f, 0.16f, 0.8f), metal);          // receiver
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0, 0.1f, -0.16f), Vector3.zero, new Vector3(0.11f, 0.05f, 0.6f), metal);       // top cover
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0, -0.02f, -0.52f), new Vector3(0, 0, 0), new Vector3(0.1f, 0.1f, 0.22f), metal); // back plate
        Part(PrimitiveType.Cylinder, mgPitch, new Vector3(0, 0.005f, 0.44f), new Vector3(90, 0, 0), new Vector3(0.075f, 0.16f, 0.075f), metal); // jacket
        Part(PrimitiveType.Cylinder, mgPitch, new Vector3(0, 0.005f, 0.86f), new Vector3(90, 0, 0), new Vector3(0.045f, 0.3f, 0.045f), metal);  // barrel
        Part(PrimitiveType.Cylinder, mgPitch, new Vector3(0, 0.005f, 1.2f), new Vector3(90, 0, 0), new Vector3(0.075f, 0.07f, 0.075f), metal);  // flash hider
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0, 0.1f, 0.55f), Vector3.zero, new Vector3(0.015f, 0.07f, 0.015f), metal);     // front sight
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0, 0.11f, -0.3f), Vector3.zero, new Vector3(0.05f, 0.05f, 0.02f), metal);      // rear sight
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0.19f, -0.07f, -0.2f), Vector3.zero, new Vector3(0.22f, 0.18f, 0.28f), olive); // ammo can
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0.1f, -0.12f, -0.58f), new Vector3(-18, 0, 0), new Vector3(0.045f, 0.2f, 0.045f), metal);  // spade grips
        Part(PrimitiveType.Cube, mgPitch, new Vector3(-0.1f, -0.12f, -0.58f), new Vector3(-18, 0, 0), new Vector3(0.045f, 0.2f, 0.045f), metal);
        Part(PrimitiveType.Cube, mgPitch, new Vector3(0, 0.0f, -0.6f), Vector3.zero, new Vector3(0.25f, 0.045f, 0.045f), metal);
        mgMuzzle = new GameObject("MGMuzzle").transform; mgMuzzle.SetParent(mgPitch, false); mgMuzzle.localPosition = new Vector3(0, 0.005f, 1.3f);
    }

    void AimMG(Player p, float dt)
    {
        if (!mgYaw || !p || !p.CamT) return;
        var cam = p.CamT;
        mgYaw.rotation = Quaternion.Slerp(mgYaw.rotation, Quaternion.Euler(0, cam.eulerAngles.y, 0), dt * 14);
        float pitch = Mathf.Clamp(Mathf.DeltaAngle(0, cam.eulerAngles.x), -50, 35);
        mgPitch.localRotation = Quaternion.Slerp(mgPitch.localRotation, Quaternion.Euler(pitch, 0, 0), dt * 14);
        mgKick = Mathf.MoveTowards(mgKick, 0, dt * 1.2f);
        mgPitch.localPosition = new Vector3(0, 0.5f, -mgKick * 0.07f);
    }

    void OnDestroy() { All.Remove(this); }

    bool netSilent;                                   // set while a wreck relayed from the network is applied
    public void NetKill() { if (dead) return; netSilent = true; Damage(999999); netSilent = false; }

    public void Damage(float d)
    {
        if (dead) return; hp -= d;
        if (hp <= 0)
        {
            dead = true; var p = Game.I.player; if (p && p.inVehicle && occupied) p.ForceExit();
            Explode.At(transform.position + Vector3.up, 7, 140, !enemy);
            foreach (var r in vis.GetComponentsInChildren<Renderer>()) foreach (var m in r.materials) { if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", new Color(0.08f, 0.07f, 0.06f)); else m.color = new Color(0.08f, 0.07f, 0.06f); }
            if (enemy) Game.I.OnTankDestroyed(this);
            if (engine) engine.Stop(); enabled = false; Destroy(gameObject, 30);
            if (!netSilent) NetVehicles.ReportDead(this);
        }
    }

    void Update()
    {
        if (dead) return;
        if (enemy) { TankAI(); return; }
        if (!occupied && engine) { engine.volume = Mathf.MoveTowards(engine.volume, 0, Time.deltaTime); if (rotor) rotor.Rotate(0, rpm * 900 * Time.deltaTime, 0, Space.Self); rpm = Mathf.MoveTowards(rpm, 0, Time.deltaTime * 0.3f); }
        if (flying && !occupied) { transform.position += Vector3.down * 4 * Time.deltaTime; if (Altitude <= 0.05f) { flying = false; transform.position = new Vector3(transform.position.x, World.Height(transform.position), transform.position.z); } }
    }

    // called every frame by the player while seated
    public void Drive(Player p, float yaw, float pitch, bool leanOut = false)
    {
        float dt = Time.deltaTime;
        float thr = Input.GetAxisRaw("Vertical"), st = Input.GetAxisRaw("Horizontal");
        AimMG(p, dt);
        if (kind == "heli") FlyHeli(p, yaw, thr, st, dt);
        else
        {
            float top = (Input.GetKey(KeyCode.LeftShift) ? 1.35f : 1) * (kind == "bike" ? 22 : 17);
            if (thr > 0.1f) speed += thr * (speed < 0 ? 22 : kind == "bike" ? 13 : 9) * dt;
            else if (thr < -0.1f) speed += thr * (speed > 0 ? 22 : 7) * dt;
            else speed = Mathf.MoveTowards(speed, 0, 6 * dt);
            speed = Mathf.Clamp(speed, -6, top);
            steer = Mathf.Lerp(steer, st, dt * 5);
            transform.Rotate(0, steer * (kind == "bike" ? 75 : 55) * dt * Mathf.Clamp(speed / 6, -1, 1), 0);
            MoveBlocked(transform.forward * speed * dt, true);
            GroundAlign(dt);
            engine.volume = 0.35f * Sfx.volume; engine.pitch = 0.7f + Mathf.Abs(speed) / 16f;
        }
        // mounted gun
        gunCool -= dt;
        if (armed && Input.GetMouseButton(0) && gunCool <= 0) { gunCool = 0.09f; MountedFire(p); }
        p.transform.position = transform.position;
        var cam = p.CamT; cam.SetParent(null);
        var back = Quaternion.Euler(Mathf.Clamp(pitch + 12, -20, 60), yaw, 0);
        float dist = kind == "heli" ? 12 : kind == "bike" ? 5.5f : 8.5f;
        var target = transform.position + Vector3.up * (kind == "heli" ? 2.5f : 1.6f);
        if (leanOut) return;                                          // first-person seat view is driven by Player
        if (armed) target += Vector3.up * 1.5f;                      // look over the gun, not at the roof
        var want = target - back * Vector3.forward * dist + Vector3.up * (armed ? 2.1f : 1.2f) + (armed ? back * Vector3.right * 1.1f : Vector3.zero);
        if (Physics.Linecast(target, want, out var h, ~(1 << 2), QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(transform)) want = h.point + (target - want).normalized * 0.3f;
        cam.position = Vector3.Lerp(cam.position, want, dt * 10); cam.rotation = Quaternion.LookRotation(target + back * Vector3.forward * 10 - cam.position);
        p.hint = kind == "heli" ? (rpm < 0.85f ? "旋翼加速中 " + Mathf.RoundToInt(rpm * 100) + "% —— 按住 空格 起飞" : "空格 上升 · C 下降 · WASD 飞行 · 鼠标 转向 · 左键 机枪 · F 下机（需落地）" + p.VehicleHintExtra) + "  高度 " + Mathf.RoundToInt(Altitude) + " m"
               : "WASD 驾驶 · Shift 加速 · F 下车" + (armed ? " · 左键 车顶机枪（鼠标 360° 转向）" : "") + p.VehicleHintExtra;
    }

    void FlyHeli(Player p, float yaw, float thr, float st, float dt)
    {
        rpm = Mathf.MoveTowards(rpm, 1, dt * 0.9f);
        bool up = Input.GetKey(KeyCode.Space) || Game.I.autoHeliUp, down = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
        float tvy = (up ? 7 : 0) - (down ? 6 : 0); if (rpm < 0.8f) tvy = Mathf.Min(tvy, 0);
        vy = Mathf.Lerp(vy, tvy, dt * 2.5f);
        flying = Altitude > 0.6f || vy > 0.1f;
        var fwd = Quaternion.Euler(0, yaw, 0);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, yaw, 0), dt * 1.8f);
        var tv = flying ? fwd * new Vector3(st, 0, thr) * (Input.GetKey(KeyCode.LeftShift) ? 30 : 22) : Vector3.zero;
        vel = Vector3.Lerp(vel, tv, dt * 1.4f);
        // horizontal movement can be blocked by buildings; vertical movement never is, otherwise the
        // collision box clips the ground on take-off and the helicopter can never leave the pad
        var pos = transform.position + vel * dt;
        box.enabled = false;
        bool blocked = flying && Physics.CheckBox(pos + transform.rotation * box.center + Vector3.up * 1.2f, box.size * 0.35f, transform.rotation, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore);
        box.enabled = true;
        if (blocked) { vel = -vel * 0.3f; if (vel.magnitude > 8) Damage(20); pos = transform.position; }
        pos.y += vy * dt;
        float g = World.Height(pos); pos.y = Mathf.Clamp(pos.y, g, g + 70);
        transform.position = pos;
        var local = transform.InverseTransformDirection(vel);
        vis.localRotation = Quaternion.Euler(local.z * 0.8f, Tune.I.vehicleYaw, -local.x * 0.9f);
        if (rotor) rotor.Rotate(0, (400 + 800 * rpm) * dt, 0, Space.Self); if (tail) tail.Rotate(1500 * rpm * dt, 0, 0, Space.Self);
        engine.volume = (0.25f + 0.35f * rpm) * Sfx.volume; engine.pitch = 0.8f + rpm * 0.3f;
    }

    void MoveBlocked(Vector3 step, bool player)
    {
        var half = box.size * 0.5f; half.y *= 0.7f;
        if (step.sqrMagnitude > 0 && Physics.BoxCast(transform.position + transform.rotation * box.center + Vector3.up * 0.3f, half, step.normalized, out var hit, transform.rotation, step.magnitude + 0.1f, ~(1 << 2), QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
        {
            var e = hit.collider.GetComponentInParent<Enemy>(); var d = hit.collider.GetComponentInParent<Destructible>();
            if (e && Mathf.Abs(speed) > 5) { e.Damage(999, false, transform.position, player); return; }
            if (d && Mathf.Abs(speed) > 6) { d.Break(); speed *= 0.7f; return; }
            if (Mathf.Abs(speed) > 9) { Damage(Mathf.Abs(speed) * 1.5f); Sfx.At(Sfx.Pick("impact_metal", 0.5f, 3), transform.position, 1, 60, 0.6f); }
            speed *= -0.25f; return;
        }
        transform.position += step;
    }

    void GroundAlign(float dt)
    {
        var p = transform.position; var f = transform.forward * 1.5f; var r = transform.right;
        float h0 = World.Height(p), hf = World.Height(p + f), hb = World.Height(p - f), hr = World.Height(p + r), hl = World.Height(p - r);
        transform.position = new Vector3(p.x, h0, p.z);
        float pitch = Mathf.Atan2(hb - hf, 3) * Mathf.Rad2Deg, roll = Mathf.Atan2(hl - hr, 2) * Mathf.Rad2Deg;
        vis.localRotation = Quaternion.Slerp(vis.localRotation, Quaternion.Euler(pitch, Tune.I.vehicleYaw, roll + (kind == "bike" ? -steer * 12 : 0)), dt * 8);
    }

    void MountedFire(Player p)
    {
        var cam = p.CamT;
        var from = mgMuzzle ? mgMuzzle.position : transform.position + Vector3.up * (kind == "heli" ? 0.8f : 2.7f) + transform.forward * (kind == "heli" ? 2.2f : 0.4f);
        mgKick = 1;
        var aimDir = (cam.forward + Random.insideUnitSphere * 0.012f).normalized;
        Vector3 aimPoint = Physics.Raycast(cam.position, aimDir, out var ah, 260, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore) && !ah.collider.transform.IsChildOf(transform)
                           ? ah.point : cam.position + aimDir * 260;
        var dir = (aimPoint - from).normalized; Vector3 end = from + dir * 260;
        if (Physics.Raycast(from, dir, out var h, 260, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(transform))
        {
            end = h.point;
            var e = h.collider.GetComponentInParent<Enemy>(); if (e) { e.Damage(40, false, transform.position, true); Fx.Blood(h.point, -dir); p.hitMarker = 0.2f; }
            else { var v = h.collider.GetComponentInParent<Vehicle>(); if (v) v.Damage(18); var d = h.collider.GetComponentInParent<Destructible>(); if (d) d.Damage(40, h.point); Fx.Impact(h.point, h.normal, v); }
        }
        Fx.Bullet(from, end, new Color(1f, 0.9f, 0.5f), 420f, 9f); Fx.Flash(from, dir, 0.45f);
        Fx.Casing(from - cam.forward * 0.5f + cam.right * 0.25f, cam.right + Vector3.up * 0.5f);
        Sfx.At(Sfx.Pick("smg", 0.3f), from, 0.8f, 150, 0.8f); Game.I.Noise(from, 90);
    }

    // enemy tank: approach to ~35 m, keep hull toward the player, fire the cannon with a lead time
    void TankAI()
    {
        var p = Game.I.player; if (!p || p.dead) return;
        float dt = Time.deltaTime; var to = p.transform.position - transform.position; to.y = 0; float d = to.magnitude;
        var want = Quaternion.LookRotation(to);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 25 * dt);
        speed = Mathf.MoveTowards(speed, d > 38 ? 7 : d < 22 ? -3 : 0, dt * 3);
        MoveBlocked(transform.forward * speed * dt, false); GroundAlign(dt);
        engine.volume = 0.5f * Sfx.volume; engine.pitch = 0.5f + Mathf.Abs(speed) * 0.05f;
        cannonCool -= dt;
        var muzzle = transform.position + Vector3.up * 2.1f + transform.forward * 3.5f;
        if (cannonCool <= 0 && d < 80 && !Physics.Linecast(muzzle, p.CamT.position, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            cannonCool = Random.Range(4f, 6f);
            var aim = p.CamT.position + Random.insideUnitSphere * Mathf.Lerp(1, 5, d / 80f) - Vector3.up * 0.8f;
            Projectile.Rocket(muzzle, (aim - muzzle).normalized, false); Fx.Flash(muzzle, transform.forward, 1.2f);
            Sfx.At(Sfx.Pick("explosion", 0.8f, 2), muzzle, 1, 250, 1.3f); Game.I.Feed("！ 坦克开炮！");
        }
    }
}


