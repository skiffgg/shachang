using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EType { Rifle, Rusher, Sniper, Heavy, Boss }
public enum Role { Assault, Suppress, Flank }

// E. Squads share what they know about the player and hand out roles.
public class Squad
{
    public readonly List<Enemy> members = new List<Enemy>();
    public Vector3 lastKnown; public float lastSeenT = -99, alertT, nextGrenadeT; public bool alerted;
    public Vector3 objective; public bool hasObjective;   // G: capture point this squad is heading for
    public void Report(Vector3 p, bool seen)
    {
        lastKnown = p; if (seen) lastSeenT = Time.time;
        if (!alerted) { alerted = true; alertT = Time.time; nextGrenadeT = Time.time + 20; Assign(); if (Game.I) Game.I.Feed("敌方小队发现你了"); }
    }
    public void Assign()
    {
        int i = 0;
        foreach (var m in members)
        {
            if (!m || !m.alive) continue;
            m.role = m.type == EType.Heavy ? Role.Suppress : m.type == EType.Rusher ? Role.Assault : m.type == EType.Sniper ? Role.Suppress : (Role)(i++ % 3);
        }
    }
    public bool AnyAlive => members.Exists(m => m && m.alive);
}

public class Enemy : MonoBehaviour
{
    public static readonly List<Enemy> All = new List<Enemy>();
    public bool alive = true; public EType type; public Role role; public Squad squad;
    float hp = 100, maxHp, shootT, thinkT, hitT, grenadeT, burstLeft, suspicion;
    NavMeshAgent agent; Animation anim; Transform model, head, rh, lh, gun, muzzle, hips;
    Vector3 hipsRest, patrolTarget, investigate; bool hasInvestigate, inCover, crouched;
    string cur = ""; LineRenderer laser; float keep = 18, accuracy = 0.5f, damage = 8, fireGap = 0.13f, burstSize = 4;
    float slowT;
    public bool Unaware => !squad.alerted && suspicion < 0.5f;
    Player P => Game.I.player;

    public void Init(EType t, Squad sq, int wave)
    {
        type = t; squad = sq; sq.members.Add(this); All.Add(this);
        bool boss = t == EType.Boss;
        model = Instantiate(Game.Model(boss ? "boss" : "enemy"), transform).transform;
        if (boss) model.localScale = Vector3.one * 1.35f;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Color tint = t == EType.Rusher ? new Color(1f, 0.72f, 0.65f) : t == EType.Sniper ? new Color(0.95f, 0.9f, 0.62f) : t == EType.Heavy ? new Color(0.5f, 0.5f, 0.55f) : boss ? new Color(0.75f, 0.45f, 0.4f) : Color.white;
            if (tint != Color.white) foreach (var m in r.materials) if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", m.GetColor("baseColorFactor") * tint); else if (m.HasProperty("_Color")) m.color *= tint;
        }
        anim = model.GetComponentInChildren<Animation>();
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.gameObject.layer = 9;   // culled beyond ~150 m
        if (anim)
        {
            anim.cullingType = AnimationCullingType.BasedOnRenderers;
            foreach (AnimationState st in anim) { st.wrapMode = WrapMode.Loop; }
            foreach (var n in new[] { "Death", "DeathHead", "DeathBack" }) if (anim[n]) anim[n].wrapMode = WrapMode.ClampForever;
            foreach (var n in new[] { "Hit", "Fire", "Grenade" }) if (anim[n]) { anim[n].wrapMode = WrapMode.Once; anim[n].layer = 1; }
            if (anim["Hit"]) anim["Hit"].speed = 1.6f;
        }
        foreach (var tr in model.GetComponentsInChildren<Transform>())
        {
            if (tr.name.EndsWith("Head") && !head) head = tr;
            if (tr.name.EndsWith("RightHand")) rh = tr; if (tr.name.EndsWith("LeftHand")) lh = tr;
            if (tr.name.EndsWith("Hips") && !hips) hips = tr;
        }
        if (hips) hipsRest = hips.localPosition;
        float s = boss ? 1.35f : 1;
        var body = gameObject.AddComponent<CapsuleCollider>(); body.height = 1.8f * s; body.radius = 0.32f * s; body.center = new Vector3(0, 0.9f * s, 0);
        if (head) { var hs = new GameObject("HeadHit"); hs.transform.SetParent(head, false); var sc = hs.AddComponent<SphereCollider>(); float k = Mathf.Max(0.0001f, head.lossyScale.x); sc.radius = 0.13f * s / k; sc.center = new Vector3(0, 0.08f / k, 0); }
        var gsrc = Game.Model("m4lo");
        if (gsrc) { gun = Instantiate(gsrc).transform; gun.localScale = Vector3.one * s; muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun, false); muzzle.localPosition = new Vector3(0, 0.02f, -0.42f); }
        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.angularSpeed = 540; agent.acceleration = 14; agent.radius = 0.35f; agent.height = 1.8f; agent.stoppingDistance = 0.4f; agent.autoBraking = true;
        float waveK = 1 + (wave - 1) * 0.08f;
        switch (t)
        {
            case EType.Rusher: hp = 80; agent.speed = 5.2f; keep = 4; accuracy = 0.5f; damage = 8; burstSize = 3; break;
            case EType.Sniper: hp = 70; agent.speed = 2.5f; keep = 50; accuracy = 0.72f; damage = 20; fireGap = 3.4f; burstSize = 1; LaserLine(); break;
            case EType.Heavy: hp = 210; agent.speed = 2.2f; keep = 16; accuracy = 0.3f; damage = 5; fireGap = 0.11f; burstSize = 8; break;
            case EType.Boss: hp = 1100; agent.speed = 2.4f; keep = 14; accuracy = 0.4f; damage = 7.5f; fireGap = 0.13f; burstSize = 7; break;
            default: hp = 100; agent.speed = 3.6f; keep = 18; break;
        }
        hp *= waveK; maxHp = hp; accuracy = Mathf.Min(0.72f, accuracy * (0.6f + wave * 0.035f)); damage *= Mathf.Lerp(0.45f, 1f, (wave - 1) / 6f);
        shootT = Random.Range(0.8f, 2f); grenadeT = Random.Range(6f, 12f);
        NewPatrol();
    }

    void LaserLine()
    {
        laser = new GameObject("Laser").AddComponent<LineRenderer>(); laser.material = Mats.New("Sprites/Default");
        laser.startWidth = laser.endWidth = 0.012f; laser.startColor = new Color(1, 0.1f, 0.1f, 0.8f); laser.endColor = new Color(1, 0.1f, 0.1f, 0.1f); laser.enabled = false;
    }

    void NewPatrol()
    {
        // with an objective the squad walks to it and then guards the area around the flag
        var p = squad.hasObjective
            ? squad.objective + new Vector3(Random.Range(-7f, 7f), 0, Random.Range(-7f, 7f))
            : transform.position + new Vector3(Random.Range(-14f, 14f), 0, Random.Range(-14f, 14f));
        if (NavMesh.SamplePosition(p, out var h, 6, NavMesh.AllAreas)) patrolTarget = h.position;
    }

    public float HealthFrac => hp / maxHp;

    bool CanSee(out float dist)
    {
        var p = P; dist = 999; if (!p || p.dead) return false;
        var eye = transform.position + Vector3.up * (crouched ? 1.1f : 1.55f); var tgt = p.CamT.position - Vector3.up * 0.25f;
        dist = Vector3.Distance(eye, tgt);
        float range = Game.I.night ? 38 : 70; if (Game.I.weatherK > 0.5f) range *= 0.6f;
        if (dist > range) return false;
        // field of view unless already alerted
        var to = (tgt - eye).normalized;
        if (!squad.alerted && Vector3.Dot(transform.forward, to) < 0.35f && dist > 6) return false;
        if (Physics.Raycast(eye, to, out var hit, dist, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            var pl = hit.collider.GetComponentInParent<Player>(); var v = hit.collider.GetComponentInParent<Vehicle>();
            return pl || (v && p.inVehicle);
        }
        return true;
    }

    // F. hearing: gunshots / explosions / footsteps within radius
    public void Hear(Vector3 pos, float radius)
    {
        if (!alive) return;
        float d = Vector3.Distance(pos, transform.position); if (d > radius) return;
        if (squad.alerted) { if (Time.time - squad.lastSeenT > 2) squad.lastKnown = pos; return; }
        suspicion += radius > 30 ? 1 : 0.4f; investigate = pos; hasInvestigate = true;
        if (suspicion >= 1) squad.Report(pos, false);
    }

    void Update()
    {
        if (!alive || Game.I.paused) return;
        var p = P; if (!p) return;
        // far, unaware enemies only think a couple of times per second
        if (!squad.alerted && (transform.position - p.transform.position).sqrMagnitude > 100 * 100) { slowT -= Time.deltaTime; if (slowT > 0) return; slowT = 0.5f; Patrol(); return; }
        float dt = Time.deltaTime;
        hitT -= dt; thinkT -= dt; grenadeT -= dt;
        bool sees = CanSee(out float d);
        if (sees) { suspicion += dt * (d < 15 ? 3 : 1.2f); if (suspicion > 0.8f || squad.alerted) squad.Report(p.transform.position, true); }

        if (!squad.alerted) Patrol();
        else if (thinkT <= 0) { thinkT = Random.Range(1f, 2.2f); Tactics(sees, p); }

        if (squad.alerted)
        {
            var look = (sees ? p.transform.position : squad.lastKnown) - transform.position; look.y = 0;
            if (look.sqrMagnitude > 0.01f) { agent.updateRotation = false; transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), dt * 7); }
            // suppressors keep firing at the last known spot even without line of sight
            bool fire = sees || (role == Role.Suppress && Time.time - squad.lastSeenT < 6 && type != EType.Sniper);
            shootT -= dt;
            if (laser) { laser.enabled = sees && shootT < 1.2f; if (laser.enabled) { laser.SetPosition(0, muzzle ? muzzle.position : transform.position + Vector3.up * 1.4f); laser.SetPosition(1, p.CamT.position - Vector3.up * 0.2f); } }
            if (fire && shootT <= 0 && hitT <= 0) Shoot(p, sees, d);
            // grenade when the player has been hiding behind cover for a while
            if (grenadeT <= 0 && Time.time > squad.nextGrenadeT && !sees && Time.time - squad.lastSeenT > 3f && Time.time - squad.lastSeenT < 12)
            {
                float gd = Vector3.Distance(transform.position, squad.lastKnown);
                if (gd > 8 && gd < 28 && (type == EType.Boss || type == EType.Rifle || type == EType.Heavy)) { ThrowGrenade(squad.lastKnown); squad.nextGrenadeT = Time.time + (type == EType.Boss ? 8 : 15); }
                grenadeT = type == EType.Boss ? Random.Range(6f, 9f) : Random.Range(12f, 20f);
            }
        }
        else agent.updateRotation = true;
        Animate();
    }

    void Patrol()
    {
        agent.speed = squad.hasObjective && Vector3.Distance(transform.position, squad.objective) > 18 ? 3.4f : 1.4f; crouched = false;
        var t = hasInvestigate ? investigate : patrolTarget;
        agent.SetDestination(t);
        if (!agent.pathPending && agent.remainingDistance < 1.2f) { if (hasInvestigate) { hasInvestigate = false; suspicion *= 0.5f; } NewPatrol(); }
    }

    void Tactics(bool sees, Player p)
    {
        var pp = sees ? p.transform.position : squad.lastKnown;
        float dist = Vector3.Distance(transform.position, pp);
        var to = pp - transform.position; to.y = 0; to.Normalize();
        var side = Vector3.Cross(Vector3.up, to);
        Vector3 dest = transform.position; inCover = false;
        agent.speed = type == EType.Rusher ? 5.5f : type == EType.Heavy || type == EType.Boss ? 2.4f : 3.6f;
        if (type == EType.Rusher || role == Role.Assault && dist > keep + 6) dest = pp - to * keep + side * Random.Range(-3f, 3f);
        else if (role == Role.Flank)
        {
            // swing wide around the player's side
            float s = (GetInstanceID() & 1) == 0 ? 1 : -1;
            dest = pp + Quaternion.Euler(0, 70 * s, 0) * (-to) * 16;
        }
        else if (dist < keep * 0.5f) dest = transform.position - to * 6;
        else if (FindCover(pp, out var c)) { dest = c; inCover = true; }
        else dest = transform.position + side * (Random.value < 0.5f ? -4 : 4);
        if (NavMesh.SamplePosition(dest, out var h, 6, NavMesh.AllAreas)) agent.SetDestination(h.position);
    }

    // cover: a nearby spot where a low line to the player is blocked but a standing one is not (can pop up and shoot)
    bool FindCover(Vector3 threat, out Vector3 best)
    {
        best = Vector3.zero; float bestScore = 1e9f;
        for (int i = 0; i < 14; i++)
        {
            var c = transform.position + Quaternion.Euler(0, i * 360f / 14, 0) * Vector3.forward * Random.Range(3f, 12f);
            if (!NavMesh.SamplePosition(c, out var h, 1.5f, NavMesh.AllAreas)) continue;
            var low = h.position + Vector3.up * 0.9f; var tgt = threat + Vector3.up * 1.2f;
            if (!Physics.Linecast(low, tgt, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore) || hit.distance > 3) continue;
            float score = Vector3.Distance(transform.position, h.position) + Mathf.Abs(Vector3.Distance(h.position, threat) - keep) * 0.5f;
            if (score < bestScore) { bestScore = score; best = h.position; }
        }
        return bestScore < 1e9f;
    }

    void Shoot(Player p, bool sees, float d)
    {
        if (burstLeft <= 0) burstLeft = burstSize;
        burstLeft--; shootT = burstLeft > 0 ? fireGap : Random.Range(0.9f, 2f) * (type == EType.Sniper ? 2 : 1);
        if (anim && anim[crouched ? "CrouchFire" : "Fire"] && !crouched) anim.Play("Fire", PlayMode.StopSameLayer);
        var from = muzzle ? muzzle.position : transform.position + Vector3.up * 1.4f;
        Sfx.At(Sfx.Shot(type == EType.Sniper ? "sniper" : "rifle", false), from, type == EType.Sniper ? 1f : 0.8f, 160);
        if (Vector3.Distance(from, p.transform.position) > 70) Sfx.At(Sfx.Pick("distant", 1f, 3), from, 0.6f, 300);
        Fx.Flash(from, transform.forward, 0.3f);
        var target = sees ? p.CamT.position - Vector3.up * 0.35f : squad.lastKnown + Vector3.up * 1.2f;
        float acc = sees ? Mathf.Clamp(accuracy - d * 0.014f - (p.inVehicle ? 0 : p.GetComponent<CharacterController>().velocity.magnitude * 0.025f), 0.05f, 0.9f) : 0;
        bool hit = Random.value < acc;
        if (!hit) target += Random.insideUnitSphere * Mathf.Lerp(0.6f, 2.2f, Random.value);
        Fx.Bullet(from, target, new Color(1f, 0.55f, 0.25f), 330f, 6f);
        if (hit) p.Hurt(damage * Random.Range(0.8f, 1.2f), transform.position);
        else if (Vector3.Distance(target, p.CamT.position) < 2.2f) p.NearMiss(target);
        // stray rounds hit cover
        if (!hit && Physics.Linecast(from, target, out var h, ~(1 << 2), QueryTriggerInteraction.Ignore)) { var dd = h.collider.GetComponentInParent<Destructible>(); if (dd) dd.Damage(damage * 0.6f, h.point); }
    }

    void ThrowGrenade(Vector3 at)
    {
        if (anim && anim["Grenade"]) anim.Play("Grenade", PlayMode.StopSameLayer); hitT = 1.2f;
        var from = transform.position + Vector3.up * 1.7f; var to = at - from; float T = 1.3f;
        var v = new Vector3(to.x / T, (to.y + 0.5f * 9.81f * T * T) / T, to.z / T);
        StartCoroutine(Delayed(0.6f, () => Projectile.Grenade(from + transform.forward * 0.5f, v, false)));
        Game.I.Feed("！ 敌人扔出手雷");
    }

    System.Collections.IEnumerator Delayed(float t, System.Action a) { yield return new WaitForSeconds(t); if (alive) a(); }

    void Animate()
    {
        if (!anim || hitT > 0.5f) return;
        var v = transform.InverseTransformDirection(agent.velocity); float sp = v.magnitude;
        crouched = inCover && sp < 0.6f && Time.time % 6 > 2.5f;   // duck in cover, pop up periodically
        string n;
        if (sp < 0.3f) n = crouched ? (anim["Cover"] ? "Cover" : "Idle") : "Idle";
        else if (inCover && sp < 2.5f && anim["CrouchWalk"]) n = "CrouchWalk";
        else if (v.z < -0.5f && Mathf.Abs(v.z) > Mathf.Abs(v.x)) n = anim["Backpedal"] ? "Backpedal" : "Walk";
        else if (Mathf.Abs(v.x) > Mathf.Abs(v.z) && anim["StrafeL"]) n = v.x < 0 ? "StrafeL" : "StrafeR";
        else n = sp > 2.8f ? "Run" : "Walk";
        if (!anim[n]) n = anim["Walk"] ? "Walk" : "Idle";
        if (cur != n) { anim.CrossFade(n, 0.2f); cur = n; }
    }

    void LateUpdate()
    {
        if (alive && hips) { var lp = hips.localPosition; hips.localPosition = new Vector3(hipsRest.x, lp.y, hipsRest.z); }
        if (!gun || !rh || !lh) return;
        var a = rh.position; var dir = (lh.position - a).normalized;
        gun.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 180, 0);
        gun.position = a + dir * 0.12f * gun.localScale.x + gun.rotation * Tune.I.enemyGunPos * gun.localScale.x;
    }

    public void Damage(float dmg, bool head, Vector3 from, bool byPlayer)
    {
        if (!alive) return;
        hp -= dmg; squad.Report(from, false); suspicion = 2;
        if (hp <= 0) { Die(head, byPlayer); return; }
        if (anim && anim["Hit"] && dmg > 20) { anim.Play("Hit", PlayMode.StopSameLayer); hitT = 0.35f; }
        thinkT = Mathf.Min(thinkT, 0.3f);
    }

    void Die(bool head, bool byPlayer)
    {
        alive = false; agent.enabled = false; if (laser) Destroy(laser.gameObject);
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (anim) { anim.Stop(); var n = head && anim["DeathHead"] ? "DeathHead" : Random.value < 0.5f && anim["DeathBack"] ? "DeathBack" : "Death"; if (anim[n]) anim.Play(n); }
        if (byPlayer) Game.I.OnKill(this, head);
        if (Random.value < 0.35f) Loot.Drop(transform.position + Vector3.up * 0.3f, Random.value < 0.6f ? LootKind.Ammo : LootKind.Med);
        if (gun) { gun.SetParent(null); var rb = gun.gameObject.AddComponent<Rigidbody>(); gun.gameObject.AddComponent<BoxCollider>(); rb.AddForce(Random.insideUnitSphere * 2, ForceMode.Impulse); Destroy(gun.gameObject, 12); }
        All.Remove(this); Destroy(gameObject, 12f);
    }

    void OnDestroy() { All.Remove(this); if (gun) Destroy(gun.gameObject); if (laser) Destroy(laser.gameObject); }
}


