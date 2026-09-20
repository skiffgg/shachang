using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// One networked combatant. The owning client drives it with the normal first-person controller and
// streams its pose; every other client sees a soldier avatar. Damage, score and death are decided by
// the server - clients only report where they aimed.
//
// Hit detection is lag compensated: the server keeps a short history of everybody's position and
// rewinds the world to what the shooter actually saw on his screen before testing the shot. Without
// it a 200 ms connection makes every moving target feel like it eats the bullet and shrugs it off.
public class NetPlayer : NetworkBehaviour
{
    public static readonly List<NetPlayer> All = new List<NetPlayer>();

    public readonly SyncVar<int> team = new SyncVar<int>();
    public readonly SyncVar<int> kills = new SyncVar<int>();
    public readonly SyncVar<int> deaths = new SyncVar<int>();
    public readonly SyncVar<float> hp = new SyncVar<float>(100f);
    public readonly SyncVar<bool> dead = new SyncVar<bool>();

    const float SendHz = 20f;
    const float InterpDelay = 0.1f;    // remote players are drawn this far in the past, so they glide
    const float MaxRewind = 0.4f;      // never trust a client claiming more lag than this
    const float HistorySeconds = 1.2f;

    // one streamed pose: the client's interpolation buffer and the server's rewind history both use it
    struct Snap { public float t; public Vector3 pos; public float yaw, pitch; public bool moving, crouch; }
    readonly List<Snap> hist = new List<Snap>(48);

    struct TintTarget { public Material m; public Color baseColor; public bool gltf; }
    readonly List<TintTarget> tintTargets = new List<TintTarget>();
    Transform avatar, rh, lh, gun, hips, spine;
    Animation anim; string clip = ""; Vector3 hipsRest;
    float sendT, shownPitch; static float poseLogT;
    CapsuleCollider serverHitbox;

    public bool Alive => !dead.Value && hp.Value > 0;
    public Vector3 Eye => transform.position + Vector3.up * 1.65f;

    bool live;                         // network properties are only safe once this is true

    void Awake() { All.Add(this); }
    void OnDestroy() { All.Remove(this); }
    public override void OnStartNetwork() { base.OnStartNetwork(); live = true; }
    public override void OnStopNetwork() { base.OnStopNetwork(); live = false; }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Push(transform.position, transform.eulerAngles.y, 0, false, false);
        Debug.Log("[Net] player ready object=" + ObjectId + " owner=" + IsOwner);
        if (IsOwner)
        {
            // the local player is the existing Player controller; this object just mirrors it
            if (Game.I && Game.I.player) { transform.position = Game.I.player.transform.position; Game.I.player.net = this; }
        }
        else
        {
            BuildAvatar();
            team.OnChange += (prev, next, asServer) => Tint();
            dead.OnChange += (prev, next, asServer) => { if (next) PlayDeath(); else clip = ""; };
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        hp.Value = 100f;
        // Dedicated servers do not create the visual avatar. Give the authority its own hitbox.
        serverHitbox = GetComponent<CapsuleCollider>();
        if (!serverHitbox) serverHitbox = gameObject.AddComponent<CapsuleCollider>();
        serverHitbox.height = 1.8f;
        serverHitbox.radius = 0.35f;
        serverHitbox.center = Vector3.up * 0.9f;
        serverHitbox.isTrigger = false;
        Push(transform.position, transform.eulerAngles.y, 0, false, false);
        Debug.Log("[Net] server player ready object=" + ObjectId + " hitbox=" + serverHitbox.enabled);
    }

    // ---------------------------------------------------------------- avatar
    void BuildAvatar()
    {
        var src = Game.Model("enemy"); if (!src) return;
        avatar = Instantiate(src, transform).transform;
        avatar.localPosition = Vector3.zero; avatar.localRotation = Quaternion.identity;
        anim = avatar.GetComponentInChildren<Animation>();
        if (anim)
        {
            anim.cullingType = AnimationCullingType.BasedOnRenderers;
            foreach (AnimationState st in anim) st.wrapMode = WrapMode.Loop;
            foreach (var n in new[] { "Death", "DeathHead", "DeathBack" }) if (anim[n]) anim[n].wrapMode = WrapMode.ClampForever;
        }
        foreach (var tr in avatar.GetComponentsInChildren<Transform>())
        {
            if (tr.name.EndsWith("RightHand") && !rh) rh = tr;
            if (tr.name.EndsWith("LeftHand") && !lh) lh = tr;
            if (tr.name.EndsWith("Hips") && !hips) hips = tr;
            if ((tr.name.EndsWith("Spine1") || tr.name.EndsWith("Spine2")) && !spine) spine = tr;
        }
        if (hips) hipsRest = hips.localPosition;
        var gsrc = Game.Model("m4lo");
        if (gsrc) gun = Instantiate(gsrc, transform).transform;
        Tint();
    }

    void Tint()
    {
        if (!avatar) return;
        if (tintTargets.Count == 0)                          // remember the original colours once
            foreach (var r in avatar.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("baseColorFactor")) tintTargets.Add(new TintTarget { m = m, baseColor = m.GetColor("baseColorFactor"), gltf = true });
                    else if (m.HasProperty("_Color")) tintTargets.Add(new TintTarget { m = m, baseColor = m.color });
                }
        // tint the uniform instead of painting over it, so the kit still reads as cloth and webbing
        Color c = team.Value == 0 ? new Color(0.62f, 0.8f, 1.3f) : new Color(1.3f, 0.66f, 0.55f);
        foreach (var t in tintTargets)
        {
            if (!t.m) continue;
            if (t.gltf) t.m.SetColor("baseColorFactor", t.baseColor * c); else t.m.color = t.baseColor * c;
        }
    }

    void PlayDeath()
    {
        if (gun) gun.gameObject.SetActive(false);
        if (!anim) return;
        anim.Stop();
        string n = anim["Death"] ? "Death" : anim["DeathBack"] ? "DeathBack" : null;
        if (n != null) { anim.Play(n); clip = n; }
    }

    // ---------------------------------------------------------------- pose streaming
    void Update()
    {
        if (!live) return;
        if (serverHitbox) serverHitbox.enabled = Alive;
        if (IsOwner) { OwnerTick(); return; }
        if (!IsClientInitialized || IsServerInitialized) return;    // a dedicated server keeps the raw pose

        // remote: replay the buffer a fixed slice in the past, so packet jitter never shows
        var s = SampleAt(Time.time - InterpDelay);
        if ((transform.position - s.pos).sqrMagnitude > 36f) transform.position = s.pos;   // teleport / respawn
        else transform.position = Vector3.Lerp(transform.position, s.pos, 1f - Mathf.Exp(-20f * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, s.yaw, 0), 1f - Mathf.Exp(-14f * Time.deltaTime));
        shownPitch = Mathf.LerpAngle(shownPitch, s.pitch > 180 ? s.pitch - 360 : s.pitch, Time.deltaTime * 10f);
        bool riding = NetVehicles.Riding(OwnerId);           // a seated player is drawn by his own vehicle
        if (avatar && avatar.gameObject.activeSelf == riding) avatar.gameObject.SetActive(!riding);
        if (gun && gun.gameObject.activeSelf == riding && !dead.Value) gun.gameObject.SetActive(!riding);
        if (riding) return;
        Animate(s);
    }

    void Animate(Snap s)
    {
        if (!anim || dead.Value) return;
        // velocity read back out of the buffer, in the avatar's own frame, so strafing reads correctly
        var prev = SampleAt(Time.time - InterpDelay - 0.12f);
        Vector3 v = Quaternion.Euler(0, -s.yaw, 0) * ((s.pos - prev.pos) / 0.12f);
        float sp = new Vector2(v.x, v.z).magnitude;
        string n;
        if (sp < 0.4f) n = s.crouch ? (anim["Cover"] ? "Cover" : "Idle") : "Idle";
        else if (s.crouch && anim["CrouchWalk"]) n = "CrouchWalk";
        else if (v.z < -0.5f && Mathf.Abs(v.z) > Mathf.Abs(v.x) && anim["Backpedal"]) n = "Backpedal";
        else if (Mathf.Abs(v.x) > Mathf.Abs(v.z) && anim["StrafeL"]) n = v.x < 0 ? "StrafeL" : "StrafeR";
        else n = sp > 3.2f ? "Run" : "Walk";
        if (!anim[n]) n = anim["Walk"] ? "Walk" : "Idle";
        if (clip != n) { anim.CrossFade(n, 0.18f); clip = n; }
    }

    void LateUpdate()
    {
        if (!live || !avatar || IsOwner) return;
        // the clips carry the root sideways; pin the hips so the soldier stays under his own collider
        if (!dead.Value && hips) { var lp = hips.localPosition; hips.localPosition = new Vector3(hipsRest.x, lp.y, hipsRest.z); }
        // lean the torso with the aim, so someone shooting uphill does not look like he stares ahead
        if (!dead.Value && spine) spine.rotation = Quaternion.AngleAxis(Mathf.Clamp(shownPitch, -55f, 55f) * 0.55f, transform.right) * spine.rotation;
        if (!gun || !rh || !lh || dead.Value) return;
        var a = rh.position; var dir = (lh.position - a).normalized;
        gun.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 180, 0);
        gun.position = a + dir * 0.12f + gun.rotation * Tune.I.enemyGunPos;
    }

    void OwnerTick()
    {
        var p = Game.I ? Game.I.player : null; if (!p) return;
        transform.position = p.transform.position;
        transform.rotation = Quaternion.Euler(0, p.transform.eulerAngles.y, 0);
        sendT -= Time.deltaTime;
        if (sendT > 0) return;
        sendT = 1f / SendHz;                                 // 20 state updates a second
        var cc = p.GetComponent<CharacterController>();
        bool moving = cc && new Vector2(cc.velocity.x, cc.velocity.z).magnitude > 0.6f;
        SendState(p.transform.position, p.transform.eulerAngles.y, p.CamT ? p.CamT.eulerAngles.x : 0, moving, p.Crouching);
    }

    [ServerRpc(RunLocally = false)]
    void SendState(Vector3 pos, float yaw, float pitch, bool moving, bool crouch)
    {
        transform.position = pos; transform.rotation = Quaternion.Euler(0, yaw, 0);
        Push(pos, yaw, pitch, moving, crouch);               // the server's rewind history
        Broadcast(pos, yaw, pitch, moving, crouch);
    }

    [ObserversRpc]
    void Broadcast(Vector3 pos, float yaw, float pitch, bool moving, bool crouch)
    {
        if (IsOwner) return;                                 // the owner already moved locally
        Push(pos, yaw, pitch, moving, crouch);               // the client's interpolation buffer
    }

    void Push(Vector3 pos, float yaw, float pitch, bool moving, bool crouch)
    {
        hist.Add(new Snap { t = Time.time, pos = pos, yaw = yaw, pitch = pitch, moving = moving, crouch = crouch });
        while (hist.Count > 2 && Time.time - hist[0].t > HistorySeconds) hist.RemoveAt(0);
    }

    Snap SampleAt(float time)
    {
        if (hist.Count == 0) return new Snap { t = time, pos = transform.position, yaw = transform.eulerAngles.y };
        if (time >= hist[hist.Count - 1].t) return hist[hist.Count - 1];
        if (time <= hist[0].t) return hist[0];
        for (int i = hist.Count - 1; i > 0; i--)
        {
            if (hist[i - 1].t > time) continue;
            var a = hist[i - 1]; var b = hist[i];
            float k = b.t - a.t > 0.0001f ? Mathf.InverseLerp(a.t, b.t, time) : 1f;
            a.pos = Vector3.Lerp(a.pos, b.pos, k);
            a.yaw = Mathf.LerpAngle(a.yaw, b.yaw, k);
            a.pitch = Mathf.LerpAngle(a.pitch, b.pitch, k);
            a.crouch = k > 0.5f ? b.crouch : a.crouch;
            a.moving = b.moving;
            return a;
        }
        return hist[hist.Count - 1];
    }

    // ---------------------------------------------------------------- shooting
    public void LocalFire(Vector3 origin, Vector3 dir, float damage, float range)
    {
        if (!IsOwner) return;
        float rtt = base.TimeManager != null ? (float)base.TimeManager.RoundTripTime : 0f;
        FireServer(origin, dir, damage, range, rtt);
    }

    [ServerRpc]
    void FireServer(Vector3 origin, Vector3 dir, float damage, float range, float rttMs)
    {
        dir = dir.normalized;
        // wind the world back to the frame the shooter was looking at: half the round trip for the
        // packet that carried that view, plus the slice everyone is deliberately rendered behind
        float rewind = Mathf.Clamp(rttMs * 0.0005f + InterpDelay, 0f, MaxRewind);
        float when = Time.time - rewind;

        // the level does not move, so it is not rewound; find where the bullet would stop
        float wall = range; Vector3 point = origin + dir * range;
        var hits = Physics.RaycastAll(origin, dir, range, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<NetPlayer>()) continue;    // players are tested below, rewound
            wall = h.distance; point = h.point; break;
        }

        NetPlayer victim = null; float best = wall; bool headshot = false;
        for (int i = 0; i < All.Count; i++)
        {
            var np = All[i];
            if (!np || np == this || !np.IsSpawned || !np.Alive || np.team.Value == team.Value) continue;
            float d; bool head;
            if (!BodyHit(origin, dir, best, np.SampleAt(when), out d, out head)) continue;
            victim = np; best = d; headshot = head; point = origin + dir * d;
        }

        if (victim)
        {
            victim.ServerDamage(headshot ? damage * 2.2f : damage, this);
            TargetHitMark(Owner, headshot);
        }
        if (Game.NetDebug)
            Debug.Log("[Net] shot by " + ObjectId + " rewind=" + (int)(rewind * 1000) + "ms rtt=" + (int)rttMs +
                      " -> " + (victim ? (headshot ? "HEAD " : "BODY ") + victim.ObjectId + " at " + best.ToString("0.0") + "m" : "miss"));
        FireFx(origin, point, victim != null);
    }

    // editor smoke test hook: the same geometry the server uses, against a standing/crouching body
    public static bool TestHit(Vector3 o, Vector3 dir, float maxD, Vector3 feet, bool crouch, out float dist, out bool head)
    {
        return BodyHit(o, dir.normalized, maxD, new Snap { pos = feet, crouch = crouch }, out dist, out head);
    }

    // ---------------------------------------------------------------- vehicles
    // The seat table is authoritative on the server; the relay rides on this object because observer
    // rpcs from the shared match object never reach the other clients.
    static readonly Dictionary<int, int> serverSeats = new Dictionary<int, int>();

    public void ClaimVehicle(int idx, bool take) { if (IsOwner) SeatServer(idx, take); }
    public void StreamVehicle(int idx, Vector3 pos, Vector3 euler, float rpm) { if (IsOwner) VehiclePoseServer(idx, pos, euler, rpm); }
    public void ReportVehicleDead(int idx) { if (IsOwner) VehicleDeadServer(idx); }

    [ServerRpc]
    void SeatServer(int idx, bool take)
    {
        int who;
        bool held = serverSeats.TryGetValue(idx, out who);
        if (take)
        {
            if (held && who != OwnerId) { TargetSeatDenied(Owner); return; }
            serverSeats[idx] = OwnerId;
            SeatAll(idx, OwnerId);
        }
        else
        {
            if (held && who != OwnerId) return;
            serverSeats.Remove(idx);
            SeatAll(idx, -1);
        }
        if (Game.NetDebug) Debug.Log("[Net] seat " + idx + (take ? " taken by " : " freed by ") + OwnerId);
    }

    [ObserversRpc]
    void SeatAll(int idx, int clientId)
    {
        NetVehicles.ApplySeat(idx, clientId, IsOwner);
    }

    [TargetRpc]
    void TargetSeatDenied(FishNet.Connection.NetworkConnection c)
    {
        var p = Game.I ? Game.I.player : null;
        if (p && p.inVehicle) p.ForceExit();
        if (Game.I) Game.I.Say("这辆车已经有人了", 1.4f);
    }

    [ServerRpc]
    void VehiclePoseServer(int idx, Vector3 pos, Vector3 euler, float rpm)
    {
        int who;
        if (!serverSeats.TryGetValue(idx, out who) || who != OwnerId) return;
        var v = NetVehicles.At(idx);
        if (v)
        {
            if (Game.NetDebug && Time.time > poseLogT) { poseLogT = Time.time + 3f; Debug.Log("[Net] vehicle " + idx + " pose from " + OwnerId + " at " + pos.ToString("0")); }
            v.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler)); v.occupied = true;          // the server needs it for shots
        }
        VehiclePoseAll(idx, pos, euler, rpm);
    }

    [ObserversRpc]
    void VehiclePoseAll(int idx, Vector3 pos, Vector3 euler, float rpm)
    {
        if (IsOwner) return;                                   // the driver already has the real thing
        NetVehicles.ApplyPose(idx, pos, euler, rpm);
    }

    [ServerRpc]
    void VehicleDeadServer(int idx)
    {
        serverSeats.Remove(idx);
        var v = NetVehicles.At(idx); if (v && !v.dead) v.NetKill();
        VehicleDeadAll(idx);
    }

    [ObserversRpc]
    void VehicleDeadAll(int idx) { if (!IsOwner) NetVehicles.ApplyDead(idx); }

    // Rewound hitbox: a body capsule plus a head sphere, both sized from the streamed stance.
    static bool BodyHit(Vector3 o, Vector3 d, float maxD, Snap s, out float dist, out bool head)
    {
        head = false; dist = 0;
        float top = s.crouch ? 1.15f : 1.5f, hy = s.crouch ? 1.28f : 1.68f;
        float bodyD, headD;
        bool hitBody = RayCapsule(o, d, maxD, s.pos + Vector3.up * 0.35f, s.pos + Vector3.up * top, 0.36f, out bodyD);
        bool hitHead = RaySphere(o, d, maxD, s.pos + Vector3.up * hy, 0.22f, out headD);
        if (!hitBody && !hitHead) return false;
        // the capsule cap overlaps the skull, so anything that passes through the head sphere at
        // roughly the same range counts as a head shot - otherwise a clean head shot scores as a chest hit
        if (hitHead && (!hitBody || headD <= bodyD + 0.3f)) { dist = headD; head = true; return true; }
        dist = bodyD; return true;
    }

    static bool RaySphere(Vector3 o, Vector3 d, float maxD, Vector3 c, float r, out float t)
    {
        t = 0;
        Vector3 m = o - c;
        float b = Vector3.Dot(m, d), cc = Vector3.Dot(m, m) - r * r;
        if (cc > 0 && b > 0) return false;
        float disc = b * b - cc;
        if (disc < 0) return false;
        t = Mathf.Max(0, -b - Mathf.Sqrt(disc));
        return t <= maxD;
    }

    // closest approach between the shot and the body segment; anything inside the radius is a hit
    static bool RayCapsule(Vector3 o, Vector3 d, float maxD, Vector3 a, Vector3 b, float r, out float t)
    {
        t = 0;
        Vector3 ab = b - a, ao = o - a;
        float abab = Vector3.Dot(ab, ab), abd = Vector3.Dot(ab, d), abao = Vector3.Dot(ab, ao), aod = Vector3.Dot(ao, d);
        float den = abab - abd * abd;                        // d is normalised, so dot(d, d) == 1
        float tr = den > 0.0001f ? (abd * abao - aod * abab) / den : -aod;
        tr = Mathf.Clamp(tr, 0, maxD);
        float ts = Mathf.Clamp01((abd * tr + abao) / Mathf.Max(0.0001f, abab));
        Vector3 onSeg = a + ab * ts;
        tr = Mathf.Clamp(Vector3.Dot(onSeg - o, d), 0, maxD);
        float gap = (onSeg - (o + d * tr)).magnitude;
        if (gap > r) return false;
        t = Mathf.Max(0, tr - Mathf.Sqrt(Mathf.Max(0, r * r - gap * gap)));
        return t <= maxD;
    }

    [ObserversRpc]
    void FireFx(Vector3 from, Vector3 to, bool hitFlesh)
    {
        if (!IsOwner) Fx.Bullet(from, to, new Color(1f, 0.75f, 0.4f), 380f, 7f);
        if (hitFlesh) Fx.Blood(to, (from - to).normalized); else Fx.Impact(to, (from - to).normalized);
    }

    [TargetRpc]
    void TargetHitMark(FishNet.Connection.NetworkConnection c, bool head)
    {
        var p = Game.I ? Game.I.player : null; if (!p) return;
        p.hitMarker = head ? 0.35f : 0.25f;
        Sfx.Ui(Sfx.Raw("hit"), 0.35f, head ? 1.4f : 1f);
    }

    public void ServerDamage(float dmg, NetPlayer by)
    {
        if (!IsServerInitialized || !Alive) return;
        hp.Value -= dmg;
        TargetHurt(Owner, by ? by.transform.position : transform.position);
        if (hp.Value <= 0)
        {
            dead.Value = true; deaths.Value++;
            if (by) { by.kills.Value++; }
            if (TdmMatch.I) TdmMatch.I.OnKill(by, this);
        }
    }

    [TargetRpc]
    void TargetHurt(FishNet.Connection.NetworkConnection c, Vector3 from)
    {
        var p = Game.I ? Game.I.player : null; if (p) p.Hurt(0.0001f, from);   // flash + direction only; hp is authoritative
        if (p) p.SetNetHealth(hp.Value);
    }

    [TargetRpc]
    public void TargetRespawn(FishNet.Connection.NetworkConnection c, Vector3 pos)
    {
        var p = Game.I ? Game.I.player : null; if (!p) return;
        p.Revive(pos);
    }

    public void ServerRespawn(Vector3 pos)
    {
        hp.Value = 100f; dead.Value = false;
        transform.position = pos;
        hist.Clear(); Push(pos, transform.eulerAngles.y, 0, false, false);
        if (gun) gun.gameObject.SetActive(true);
        TargetRespawn(Owner, pos);
    }
}
