using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// First-person player: movement (sprint / crouch / slide / lean), 5 weapons with ADS, grenades, melee,
// attachments, vehicles, footsteps on different surfaces.
public class Player : MonoBehaviour
{
    public float hp = 100, armor, hurtFlash, hitMarker, shake;
    public bool dead, inVehicle, reloading, ads;
    public string hint = "";
    public int weapon, grenades = 3;
    // the player's pack: consumables carried, valuables looted, attachments owned
    public int medkits = 1, armorPlates = 0, ammoBoxes = 1;
    public readonly List<(string name, int value)> valuables = new List<(string, int)>();
    public readonly List<string> attachments = new List<string>();
    public int[] ammo = new int[5], reserve = new int[5];
    public Loadout loadout = new Loadout();
    public Vector3 lastHurtFrom; public float lastHurtT = -99;
    public Camera cam;
    public Transform CamT => camT;

    CharacterController cc; Transform camT, viewRoot; ViewModel[] views = new ViewModel[5]; PackView pack;
    float yaw, pitch, vy, bob, recoil, cool, lean, crouchK, slideT, stepDist, meleeCool, adsK, regenDelay, sprintK;
    public bool sprinting; public float AdsK => adsK; public bool leanOut;
    public bool Crouching => crouch;
    public NetPlayer net;                                   // set when this is an online match
    public Vehicle Vehicle => vehicle;                      // the car the network layer has to stream

    // in multiplayer the server owns the health bar; this only mirrors it
    public void SetNetHealth(float v) { hp = Mathf.Clamp(v, 0, 100); }

    // test hook (-autofire): steer the real look angles, so the view keeps facing the target
    public void DebugAim(Vector3 target)
    {
        var d = target - camT.position; if (d.sqrMagnitude < 0.01f) return;
        yaw = Quaternion.LookRotation(d).eulerAngles.y;
        pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
    }
    int shotCount;
    Vector3 slideDir; Light muzzleLight; Vehicle vehicle; bool crouch;
    public WeaponDef W => Weapons.All[weapon];

    void Start()
    {
        gameObject.layer = 10;
        cc = gameObject.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 50;
        cam = Camera.main; if (!cam) cam = new GameObject("Camera").AddComponent<Camera>();
        camT = cam.transform; camT.SetParent(transform, false); camT.localPosition = new Vector3(0, 1.62f, 0); camT.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.03f; cam.farClipPlane = 450; cam.fieldOfView = Tune.I.fov;
        if (!cam.GetComponent<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
        viewRoot = new GameObject("ViewRoot").transform; viewRoot.SetParent(camT, false);
        pack = new PackView(viewRoot); views[0] = pack;
        string[] ids = { "rifle", "shotgun", "sniper", "smg", "rpg" };
        for (int i = 1; i < 5; i++) views[i] = new ArmsView(viewRoot, ids[i]);
        for (int i = 0; i < 5; i++) { ammo[i] = Weapons.All[i].mag; reserve[i] = Weapons.All[i].reserve; views[i].Show(i == 0); }
        muzzleLight = Fx.MuzzleLight(camT, new Vector3(0.12f, -0.1f, 0.9f));
        RefreshLoadout();
        views[0].OnEquip();
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        yaw = transform.eulerAngles.y;
    }

    public void ApplyTune() { if (cam) cam.fieldOfView = Tune.I.fov; }
    void SetYaw(float y) { yaw = y; pitch = 0; }
    // which weapons accept attachments: rifle, shotgun, SMG (sniper has its own scope, RPG its own sight)
    public static bool TakesAttachments(int w) => w == 0 || w == 1 || w == 3;

    public void RefreshLoadout()
    {
        for (int i = 0; i < views.Length; i++) if (TakesAttachments(i)) views[i].ApplyLoadout(loadout);
        for (int i = 0; i < views.Length; i++) views[i].Show(i == weapon);
    }
    public void Shake(float k) { shake = Mathf.Max(shake, k); }

    void Update()
    {
        if (Game.I.paused || Game.I.showMap) return;
        if (dead) return;
        float dt = Time.deltaTime;
        hurtFlash = Mathf.Max(0, hurtFlash - dt * 1.5f); hitMarker = Mathf.Max(0, hitMarker - dt); shake = Mathf.Max(0, shake - dt * 2.5f);
        regenDelay -= dt; if (regenDelay <= 0 && hp < 100) hp = Mathf.Min(100, hp + 7 * dt);

        float sens = Tune.I.mouseSens * (ads ? W.adsFov / Tune.I.fov : 1);
        yaw += Input.GetAxisRaw("Mouse X") * sens; pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * sens, -85, 85);

        if (Input.GetKeyDown(KeyCode.F)) { if (inVehicle) ExitVehicle(); else TryEnterVehicle(); }
        if (inVehicle)
        {
            // V toggles between driving/gunning and leaning out with your own weapon
            if (Input.GetKeyDown(KeyCode.V)) { leanOut = !leanOut; ShowSelf(leanOut); if (leanOut) views[weapon].OnEquip(); }
            vehicle.Drive(this, yaw, pitch, leanOut);
            viewRoot.gameObject.SetActive(leanOut);
            if (leanOut)
            {
                // stand up out of the hatch / lean out of the door, so you can actually see and shoot
                // lean out of the left side so the body and the mounted gun stay out of the way
                var local = vehicle.kind == "heli" ? new Vector3(-1.75f, 1.35f, 0.3f)
                          : vehicle.kind == "bike" ? new Vector3(-0.4f, 1.5f, 0.15f)
                          : new Vector3(-1.35f, 1.65f, 0.35f);
                var seat = vehicle.transform.TransformPoint(local);
                camT.position = seat; camT.rotation = Quaternion.Euler(pitch - recoil * 1.6f, yaw, 0);
                recoil = Mathf.Lerp(recoil, 0, dt * 9); adsK = Mathf.MoveTowards(adsK, ads ? 1 : 0, dt * 6);
                cam.fieldOfView = Mathf.Lerp(Tune.I.fov, AdsFov(), adsK);
                viewRoot.localPosition = Vector3.Lerp(viewRoot.localPosition, Vector3.Lerp(new Vector3(0, 0, -recoil * 0.035f), Tune.I.adsOffset, adsK), dt * 16);
                viewRoot.localRotation = Quaternion.Euler(-recoil * 2.2f, 0, 0);
                for (int i = 0; i < views.Length; i++) views[i].root.gameObject.SetActive(i == weapon && !(W.scope && adsK > 0.9f));
                views[weapon].Tick(dt, camT);
                Weapon(dt);
            }
            return;
        }
        viewRoot.gameObject.SetActive(true);

        Move(dt);
        Weapon(dt);
        UpdateHint();
        Recover();
        UsePack();
    }

    // ------------------------------------------------------------ movement
    void Move(float dt)
    {
        transform.rotation = Quaternion.Euler(0, yaw, 0);
        var input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")), 1);
        bool wantCrouch = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
        bool sprint = Input.GetKey(KeyCode.LeftShift) && input.magnitude > 0.1f && input.z > -0.3f && !ads && !reloading && slideT <= 0;
        sprinting = sprint; sprintK = Mathf.MoveTowards(sprintK, sprint ? 1 : 0, dt * 5);
        // A. slide: crouch while sprinting
        if (sprint && Input.GetKeyDown(KeyCode.C) && cc.isGrounded) { slideT = 0.75f; slideDir = transform.forward; Sfx.At(Sfx.Pick("step_gravel", 0.5f, 6), transform.position, 0.8f, 30, 0.8f); }
        crouch = wantCrouch || slideT > 0;
        float speed = sprint ? Tune.I.sprintSpeed : crouch ? Tune.I.crouchSpeed : Tune.I.moveSpeed;
        if (ads) speed *= 0.6f;
        Vector3 move;
        if (slideT > 0) { slideT -= dt; move = slideDir * Mathf.Lerp(3, 11, slideT / 0.75f); }
        else move = transform.TransformDirection(input) * speed;
        if (cc.isGrounded) { vy = -1; if (Input.GetKeyDown(KeyCode.Space) && !crouch) vy = 5.6f; } else vy -= 18 * dt;
        move.y = vy; vy = Mathf.Max(vy, -45f); cc.Move(move * dt);
        // never end up under the ground, whatever the controller did on a steep slope
        var gp = transform.position; float gh = World.Height(gp);
        if (gp.y < gh - 0.35f) { cc.Move(Vector3.up * (gh + 0.05f - gp.y)); if (vy < 0) vy = -1f; }
        // stand up only if there is headroom
        float targetH = crouch ? 1.15f : 1.8f;
        if (!crouch && cc.height < 1.79f && Physics.SphereCast(transform.position + Vector3.up * 0.6f, 0.3f, Vector3.up, out _, 1.2f, ~(1 << 2), QueryTriggerInteraction.Ignore)) targetH = cc.height;
        cc.height = Mathf.MoveTowards(cc.height, targetH, dt * 4); cc.center = new Vector3(0, cc.height / 2, 0);
        crouchK = Mathf.InverseLerp(1.8f, 1.15f, cc.height);

        float moving = new Vector2(cc.velocity.x, cc.velocity.z).magnitude;
        if (cc.isGrounded && moving > 0.5f && slideT <= 0)
        {
            bob += dt * moving * 1.5f; stepDist += moving * dt;
            if (stepDist > (sprint ? 2.1f : 1.6f)) { stepDist = 0; Footstep(sprint ? 1f : crouch ? 0.35f : 0.65f); }
        }
        // A. lean with Q / E (blocked by walls)
        float wantLean = (Input.GetKey(KeyCode.Q) ? -1 : 0) + (Input.GetKey(KeyCode.E) ? 1 : 0);
        if (wantLean != 0 && Physics.Raycast(camT.position, transform.right * wantLean, 0.6f, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore)) wantLean = 0;
        lean = Mathf.Lerp(lean, wantLean, dt * 8);
        recoil = Mathf.Lerp(recoil, 0, dt * 9);
        float sk = shake * shake * 0.6f;
        camT.localPosition = new Vector3(lean * 0.45f, 1.62f - crouchK * 0.6f + Mathf.Abs(Mathf.Sin(bob)) * 0.03f, 0) + Random.insideUnitSphere * sk * 0.05f;
        camT.localRotation = Quaternion.Euler(pitch - recoil * 1.6f + Random.Range(-sk, sk) * 3, 0, -lean * 14);
        // view model: sway, bob, kick, sprint pose, ADS
        adsK = Mathf.MoveTowards(adsK, ads ? 1 : 0, dt * 6);
        var sprintOffset = sprint ? new Vector3(-0.04f, -0.06f, -0.04f) : Vector3.zero;
        var tp = Vector3.Lerp(new Vector3(Mathf.Sin(bob) * 0.012f, Mathf.Abs(Mathf.Cos(bob)) * 0.01f, -recoil * 0.035f) + sprintOffset, Tune.I.adsOffset + new Vector3(0, 0, -recoil * 0.015f), adsK);
        viewRoot.localPosition = Vector3.Lerp(viewRoot.localPosition, tp, dt * 16);
        viewRoot.localRotation = Quaternion.Euler(-recoil * 2.2f + (sprint ? 10 : 0), sprint ? -18 : 0, 0);
        cam.fieldOfView = Mathf.Lerp(Tune.I.fov + sprintK * 9, AdsFov(), adsK);
        foreach (var v in views) if (v != null && v.root.gameObject.activeSelf) v.Tick(dt, camT);
        // sniper scope hides the model at full zoom
        views[weapon].root.gameObject.SetActive(!(W.scope && adsK > 0.9f));
    }

    bool AdsInput => Input.GetMouseButton(1) || Game.I.autoAds;
    float AdsFov() { if (TakesAttachments(weapon)) return loadout.acog ? 20 : loadout.holo ? Mathf.Min(42, W.adsFov) : W.adsFov; return W.adsFov; }
    public bool ScopeOverlay => ads && adsK > 0.9f && (W.scope || (TakesAttachments(weapon) && loadout.acog));
    public bool RedDot => ads && adsK > 0.9f && TakesAttachments(weapon) && (loadout.holo || loadout.acog);

    // X heals, Z plates armour, and an empty reload pulls a box out of the pack
    void UsePack()
    {
        if (Input.GetKeyDown(KeyCode.X) && medkits > 0 && hp < 100)
        { medkits--; hp = Mathf.Min(100, hp + 45); Sfx.Ui(Sfx.Raw("pickup"), 0.7f); Game.I.Feed("使用急救包 +45 生命"); }
        if (Input.GetKeyDown(KeyCode.Z) && armorPlates > 0 && armor < 100)
        { armorPlates--; armor = Mathf.Min(100, armor + 50); Sfx.Ui(Sfx.Raw("pickup"), 0.7f); Game.I.Feed("装上护甲板 +50 护甲"); }
    }

    public bool UseAmmoBox()
    {
        if (ammoBoxes <= 0) return false;
        ammoBoxes--; GiveAmmo(0.6f); Game.I.Feed("拆开弹药箱，补充弹药"); return true;
    }

    // if the player ends up under the terrain or outside the map, put them back on solid ground
    void Recover()
    {
        var q = transform.position;
        bool bad = float.IsNaN(q.x) || float.IsNaN(q.y) || q.y < World.BASE - 45 || q.y < World.Height(q) - 2f || Mathf.Abs(q.x) > World.HALF + 20 || Mathf.Abs(q.z) > World.HALF + 20;
        if (!bad) return;
        var safe = new Vector3(Mathf.Clamp(float.IsNaN(q.x) ? 0 : q.x, -World.HALF + 15, World.HALF - 15), 0, Mathf.Clamp(float.IsNaN(q.z) ? 0 : q.z, -World.HALF + 15, World.HALF - 15));
        cc.enabled = false; transform.position = World.OnGround(safe) + Vector3.up * 1.2f; cc.enabled = true; vy = 0;
        Game.I.Say("已脱离地图边缘，送回地面");
    }

    // where the crosshair actually points, used by the launcher and its sight
    public Vector3 AimPoint(float max = 400)
    {
        if (Physics.Raycast(camT.position, camT.forward, out var h, max, ~((1 << 2) | (1 << 10)), QueryTriggerInteraction.Ignore)) return h.point;
        return camT.position + camT.forward * max;
    }
    public bool RpgSight => ads && adsK > 0.5f && W.rocket;

    void Footstep(float vol)
    {
        string surf = "step_sand";
        if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var h, 1f, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            var n = h.collider.name.ToLower();
            if (n.Contains("floor") || n.Contains("stair") || n.Contains("crate")) surf = "step_wood";
            else if (n.Contains("road") || n.Contains("rock") || n.Contains("boulder")) surf = "step_gravel";
        }
        Sfx.At(Sfx.Pick(surf, 0.35f, 10), transform.position, vol * 0.6f, 25);
        if (vol > 0.5f) Game.I.Noise(transform.position, vol > 0.9f ? 16 : 9);   // F/E: enemies hear footsteps
    }

    // ------------------------------------------------------------ weapons
    void Weapon(float dt)
    {
        cool -= dt; meleeCool -= dt; muzzleLight.intensity = Mathf.Max(0, muzzleLight.intensity - dt * 60);
        for (int i = 0; i < 5; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Switch(i);
        float wheel = Input.GetAxis("Mouse ScrollWheel"); if (wheel > 0.01f) Switch((weapon + 4) % 5); if (wheel < -0.01f) Switch((weapon + 1) % 5);
        ads = AdsInput && !reloading && slideT <= 0;
        if (Input.GetKeyDown(KeyCode.R) && !reloading && ammo[weapon] < W.mag && reserve[weapon] > 0) StartCoroutine(Reload());
        if (Input.GetKeyDown(KeyCode.G) && grenades > 0 && !reloading) ThrowGrenade();
        if (Input.GetKeyDown(KeyCode.V) && meleeCool <= 0) Melee();
        bool trigger = W.auto ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        bool sprinting = Input.GetKey(KeyCode.LeftShift) && Input.GetAxisRaw("Vertical") > 0.1f && !ads;
        if (trigger && Cursor.lockState == CursorLockMode.Locked && !reloading && !sprinting && cool <= 0 && views[weapon].animBusy <= 0)
        {
            if (ammo[weapon] > 0) Fire();
            else { cool = 0.3f; Sfx.Ui(Sfx.Raw("empty"), 0.6f); if (reserve[weapon] > 0) StartCoroutine(Reload()); }
        }
    }

    void Switch(int i)
    {
        if (i == weapon || reloading) return;
        views[weapon].Show(false); weapon = i; views[i].Show(true); views[i].OnEquip(); cool = 0.4f;
        Sfx.Ui(Sfx.Pick("mag", 0.3f, 4), 0.5f); Game.I.Say(W.name, 1.2f);
    }

    public void Fire()
    {
        ammo[weapon]--; cool = W.rate; shotCount++;
        bool sil = TakesAttachments(weapon) && loadout.silencer;
        float rec = W.recoil * (TakesAttachments(weapon) && loadout.grip ? 0.65f : 1) * (crouch ? 0.7f : 1);
        recoil = Mathf.Min(recoil + rec, 6); pitch -= rec * 0.45f; yaw += Random.Range(-0.2f, 0.2f) * rec;
        var muzzle = views[weapon].Muzzle;
        if (!sil) { muzzleLight.transform.position = muzzle; muzzleLight.intensity = 5; Fx.Flash(muzzle, camT.forward, weapon == 1 ? 0.45f : 0.28f); }
        Sfx.Ui(Sfx.Shot(W.id, sil), sil ? 0.6f : 0.85f, Random.Range(0.95f, 1.05f));
        if (weapon == 0 || weapon == 3 || weapon == 1) StartCoroutine(Casing());
        Game.I.Noise(transform.position, sil ? 8 : W.id == "sniper" ? 120 : 80);
        views[weapon].OnFire();
        if (W.rocket) { var origin = camT.position + camT.forward * 0.9f + camT.right * 0.1f - camT.up * 0.05f; Projectile.Rocket(origin, (AimPoint() - origin).normalized, true); if (ammo[weapon] == 0 && reserve[weapon] > 0) StartCoroutine(Reload()); return; }
        bool moving = cc.velocity.magnitude > 0.5f;
        float spread = Mathf.Lerp(W.hipSpread * (TakesAttachments(weapon) && loadout.laser ? 0.55f : 1) * (moving ? 1.5f : 1), W.adsSpread, adsK) * (crouch ? 0.7f : 1);
        if (net)
        {
            for (int q = 0; q < W.pellets; q++)
            {
                var nd = (camT.forward + camT.right * Random.Range(-spread, spread) + camT.up * Random.Range(-spread, spread)).normalized;
                net.LocalFire(camT.position, nd, W.dmg, 400f);
            }
        }
        for (int p = 0; p < W.pellets; p++)
        {
            var dir = (camT.forward + camT.right * Random.Range(-spread, spread) + camT.up * Random.Range(-spread, spread)).normalized;
            // like the real thing: only every third round is a tracer (always on the bolt gun)
            Shoot(camT.position, dir, W.dmg, muzzle, p == 0 && (W.scope || W.pellets > 1 || shotCount % 3 == 0));
        }
    }

    // hitscan with penetration through wood/crates (C)
    void Shoot(Vector3 from, Vector3 dir, float dmg, Vector3 muzzle, bool tracer)
    {
        var hits = Physics.RaycastAll(from, dir, 300, ~(1 << 2), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Vector3 end = from + dir * 250;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            end = h.point;
            var e = h.collider.GetComponentInParent<Enemy>();
            if (e) { if (!e.alive) continue; bool head = h.collider.name == "HeadHit"; e.Damage(dmg * (head ? 3.2f : 1), head, transform.position, true); Fx.Blood(h.point, -dir); hitMarker = 0.25f; Sfx.Ui(Sfx.Raw("hit"), 0.35f, head ? 1.4f : 1); break; }
            var v = h.collider.GetComponentInParent<Vehicle>(); if (v) { v.Damage(dmg * 0.5f); Fx.Impact(h.point, h.normal, true); hitMarker = 0.15f; break; }
            var d = h.collider.GetComponentInParent<Destructible>();
            if (d) { d.Damage(dmg, h.point); if (d && d.penetrable && dmg > 25) { dmg *= 0.5f; continue; } break; }
            Fx.Impact(h.point, h.normal, h.collider.name.ToLower().Contains("barrel") || h.collider.name.ToLower().Contains("v_"));
            break;
        }
        if (tracer) Fx.Bullet(muzzle, end, new Color(1f, 0.92f, 0.62f));
    }

    IEnumerator Casing() { yield return new WaitForSeconds(0.35f); Sfx.At(Sfx.Pick("shells", 0.3f, 8), transform.position, 0.25f, 12); }

    IEnumerator Reload()
    {
        reloading = true; views[weapon].OnReload(W.reload);
        var clip = W.id == "shotgun" ? Sfx.Pick("pump", 1.2f, 2) : W.id == "sniper" ? Sfx.Pick("bolt", 1.4f, 2) : Sfx.Raw("reload");
        Sfx.Ui(clip, 0.7f);
        int w = weapon; if (reserve[w] <= 0) UseAmmoBox();
        yield return new WaitForSeconds(W.reload);
        if (w == weapon) { int take = Mathf.Min(W.mag - ammo[w], reserve[w]); ammo[w] += take; reserve[w] -= take; }
        reloading = false;
    }

    void ThrowGrenade()
    {
        grenades--; Projectile.Grenade(camT.position + camT.forward * 0.5f, camT.forward * 16 + Vector3.up * 3.5f + cc.velocity * 0.5f, true);
        Sfx.Ui(Sfx.Pick("mag", 0.25f, 4), 0.5f, 1.4f);
    }

    void Melee()
    {
        meleeCool = 0.7f; Sfx.Ui(Sfx.Pick("whiz", 0.3f, 2), 0.6f, 1.6f); recoil += 2;
        if (Physics.SphereCast(camT.position, 0.35f, camT.forward, out var h, 2.2f, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            var e = h.collider.GetComponentInParent<Enemy>();
            if (e && e.alive) { e.Damage(e.Unaware ? 999 : 110, false, transform.position, true); Fx.Blood(h.point, -camT.forward); hitMarker = 0.3f; if (e.Unaware) Game.I.Say("暗杀！", 1.2f); }
            var d = h.collider.GetComponentInParent<Destructible>(); if (d) d.Damage(40, h.point);
        }
    }

    // ------------------------------------------------------------ damage
    public void Hurt(float dmg, Vector3 from)
    {
        if (dead || Game.I.paused) return;
        if (inVehicle) { vehicle.Damage(dmg); dmg *= 0.25f; }
        if (armor > 0) { float a = Mathf.Min(armor, dmg * 0.6f); armor -= a; dmg -= a; }
        hp -= dmg; hurtFlash = 1; regenDelay = 5; lastHurtFrom = from; lastHurtT = Time.time; Shake(0.3f);
        if (hp <= 0) { hp = 0; dead = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; Game.I.OnPlayerDied(); }
    }

    public void NearMiss(Vector3 pos) { Sfx.At(Sfx.Pick("whiz", 0.6f, 2), pos, 0.9f, 15); Shake(0.15f); }

    // conquest respawn
    public void Revive(Vector3 pos)
    {
        cc.enabled = false; transform.position = pos; cc.enabled = true;
        hp = 100; armor = Mathf.Max(armor, 25); dead = false; hurtFlash = 0; vy = 0;
        for (int i = 0; i < 5; i++) ammo[i] = Weapons.All[i].mag;
        GiveAmmo(0.35f); grenades = Mathf.Max(grenades, 2);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        views[weapon].OnEquip();
    }

    public void GiveAmmo(float frac) { for (int i = 0; i < 5; i++) reserve[i] = Mathf.Min(Weapons.All[i].reserve * 2, reserve[i] + Mathf.CeilToInt(Weapons.All[i].reserve * frac)); }

    // ------------------------------------------------------------ vehicles
    Vehicle NearVehicle()
    {
        Vehicle best = null; float bd = 4.5f;
        foreach (var v in Vehicle.All) { if (v.enemy || v.dead || (NetVehicles.Online && NetVehicles.Taken(v))) continue; float d = Vector3.Distance(v.transform.position, transform.position); if (d < bd) { bd = d; best = v; } }
        return best;
    }
    public void TryEnterVehicle() { var v = NearVehicle(); if (!v) return; vehicle = v; inVehicle = true; leanOut = false; v.occupied = true; cc.enabled = false; ads = false; ShowSelf(false); NetVehicles.Enter(v); }

    // hide everything the player carries while riding: the third-person camera would otherwise show
    // stray view-model pieces floating at the driver's seat
    void ShowSelf(bool on)
    {
        // the camera (and with it the view model) is detached from the player while driving,
        // so the weapon renderers have to be toggled through viewRoot as well
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        if (viewRoot) foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        if (muzzleLight) muzzleLight.enabled = on;
    }
    public void ExitVehicle()
    {
        if (!inVehicle) return;
        if (vehicle.flying && vehicle.Altitude > 1.5f) { Game.I.Say("先降落（按住 C / Ctrl 下降）", 1.5f); return; }
        NetVehicles.Leave(vehicle);
        inVehicle = false; leanOut = false; vehicle.occupied = false;
        transform.position = vehicle.transform.position - vehicle.transform.right * 2.8f + Vector3.up * 0.3f; cc.enabled = true;
        camT.SetParent(transform, false); camT.localPosition = new Vector3(0, 1.62f, 0); vehicle = null; ShowSelf(true); views[weapon].OnEquip();
    }
    public void ForceExit() { if (!inVehicle) return; vehicle.flying = false; ExitVehicle(); }

    void ForceLean() { if (inVehicle && !leanOut) { leanOut = true; ShowSelf(true); views[weapon].OnEquip(); } }

    public string VehicleHintExtra => inVehicle ? (leanOut ? "  ·  V 收枪回到驾驶位" : "  ·  V 探身用自己的枪") : "";

    void UpdateHint()
    {
        var v = NearVehicle(); if (v) { hint = "按 F 上" + v.displayName; return; }
        var loot = Loot.Near(transform.position); if (loot) { hint = "按 F 拾取 " + loot.label; if (Input.GetKeyDown(KeyCode.F)) loot.Take(this); return; }
        hint = "";
    }
}
