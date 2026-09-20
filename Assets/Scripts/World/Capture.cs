using System.Collections.Generic;
using UnityEngine;

// G. Capture points for the conquest mode: a flag pole, a capture radius, and an owner.
// owner: -1 neutral, 0 player, 1 enemy.
public class CapturePoint : MonoBehaviour
{
    public static readonly List<CapturePoint> All = new List<CapturePoint>();
    public string label; public int owner = -1; public float radius = 14f;
    public float progress;          // 0..1 toward `contender`
    public int contender = -1;      // who is currently taking it
    public bool contested;          // both sides inside
    public float supplyT;

    Transform flag; Renderer flagR, poleR; Material flagMat;

    public static CapturePoint Create(Vector3 pos, string label)
    {
        var go = new GameObject("Capture_" + label); go.transform.position = World.OnGround(pos);
        var cp = go.AddComponent<CapturePoint>(); cp.label = label;

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(pole.GetComponent<Collider>());
        pole.name = "Pole"; pole.transform.SetParent(go.transform, false); pole.transform.localPosition = new Vector3(0, 3f, 0); pole.transform.localScale = new Vector3(0.12f, 3f, 0.12f);
        var pm = Mats.New("Standard", new Color(0.32f, 0.33f, 0.3f)).F("_Metallic", 0.4f).F("_Glossiness", 0.3f);
        pole.GetComponent<Renderer>().sharedMaterial = pm; cp.poleR = pole.GetComponent<Renderer>();

        var f = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(f.GetComponent<Collider>());
        f.name = "Flag"; f.transform.SetParent(go.transform, false); f.transform.localPosition = new Vector3(0.85f, 5.1f, 0); f.transform.localScale = new Vector3(1.7f, 1.1f, 0.05f);
        cp.flagMat = Mats.New("Standard", Color.gray).F("_Glossiness", 0.1f);
        f.GetComponent<Renderer>().sharedMaterial = cp.flagMat; cp.flag = f.transform; cp.flagR = f.GetComponent<Renderer>();

        // a low sandbag ring so the point reads as a position worth holding
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2; var p = go.transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 5.5f;
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); b.name = "CoverBag"; b.transform.SetParent(go.transform);
            b.transform.position = World.OnGround(p) + Vector3.up * 0.45f; b.transform.rotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
            b.transform.localScale = new Vector3(2.6f, 0.9f, 0.6f);
            var bm = Resources.Load<Material>("Mats/Bags"); if (bm) b.GetComponent<Renderer>().sharedMaterial = bm;
        }
        All.Add(cp); return cp;
    }

    void OnDestroy() { All.Remove(this); }

    public Color Tint => owner == 0 ? new Color(0.3f, 0.65f, 1f) : owner == 1 ? new Color(1f, 0.3f, 0.22f) : new Color(0.75f, 0.75f, 0.7f);

    public bool Inside(Vector3 p) { p.y = transform.position.y; return Vector3.Distance(p, transform.position) < radius; }

    public void Tick(float dt)
    {
        var pl = Game.I.player;
        bool playerIn = pl && !pl.dead && Inside(pl.transform.position);
        int enemiesIn = 0;
        foreach (var e in Enemy.All) if (e && e.alive && Inside(e.transform.position)) enemiesIn++;
        contested = playerIn && enemiesIn > 0;

        int taker = contested ? -2 : playerIn ? 0 : enemiesIn > 0 ? 1 : -2;
        if (taker == -2 || taker == owner)
        {
            progress = Mathf.MoveTowards(progress, 0, dt * 0.25f);      // slowly decays when nobody works on it
        }
        else
        {
            if (contender != taker) { contender = taker; progress = 0; }
            float rate = taker == 0 ? 0.16f : 0.035f * Mathf.Min(2, enemiesIn);
            progress += rate * dt;
            if (progress >= 1)
            {
                progress = 0; int old = owner; owner = taker; supplyT = 0;
                Game.I.OnCaptured(this, old);
            }
        }
        // flag colour and height
        if (flagMat) flagMat.color = Color.Lerp(Tint, Color.white, contested ? 0.35f * Mathf.PingPong(Time.time * 2, 1) : 0);
        float h = owner == -1 ? 3.6f : 5.1f;
        flag.localPosition = new Vector3(0.85f, Mathf.MoveTowards(flag.localPosition.y, h, dt * 1.2f), 0);

        // player-held points keep dropping supplies
        if (owner == 0)
        {
            supplyT -= dt;
            if (supplyT <= 0)
            {
                supplyT = 35;
                var spot = transform.position + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                Loot.Drop(World.OnGround(spot) + Vector3.up * 0.3f, Random.value < 0.5f ? LootKind.Ammo : LootKind.Med);
            }
        }
    }
}
