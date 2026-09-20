using UnityEngine;

// C. Destructible cover: crates / wooden walls take damage, bullets pass through wood (with damage loss),
// break into physics debris; red barrels explode and chain-react.
public class Destructible : MonoBehaviour
{
    public float hp = 60;
    public bool penetrable = true;   // bullets continue through, losing damage
    public bool explosive;
    bool dead;

    public void Damage(float d, Vector3 at)
    {
        if (dead) return;
        hp -= d;
        if (!explosive) Fx.Splinters(at);
        if (hp <= 0) Break();
    }

    public void Break()
    {
        if (dead) return; dead = true;
        var p = transform.position;
        if (explosive) { Explode.At(p + Vector3.up * 0.6f, 6f, 120f); }
        else
        {
            // chunks: a few rigidbody boxes flying apart, then fade
            var b = Game.WorldBounds(gameObject);
            int n = Mathf.Clamp(Mathf.RoundToInt(b.size.magnitude * 4), 4, 14);
            var mat = GetComponentInChildren<Renderer>() ? GetComponentInChildren<Renderer>().sharedMaterial : null;
            for (int i = 0; i < n; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.transform.position = b.center + new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-b.extents.y, b.extents.y), Random.Range(-b.extents.z, b.extents.z)) * 0.8f;
                c.transform.localScale = new Vector3(Random.Range(0.08f, 0.35f), Random.Range(0.03f, 0.1f), Random.Range(0.2f, 0.6f));
                c.transform.rotation = Random.rotation;
                if (mat) c.GetComponent<Renderer>().sharedMaterial = mat;
                var rb = c.AddComponent<Rigidbody>(); rb.mass = 2; rb.AddExplosionForce(260, b.center - Vector3.up * 0.3f, 4);
                c.layer = 2; Destroy(c, Random.Range(5f, 8f));
            }
            Sfx.At(Sfx.Pick("impact_dirt", 0.4f, 4), p, 1f, 60, 0.6f);
            Fx.Splinters(b.center);
        }
        Game.I.OnStructureDestroyed(this);
        Destroy(gameObject);
    }
}

public static class Explode
{
    // area damage to everything: player, enemies, vehicles, destructibles; physics push
    public static void At(Vector3 p, float radius, float dmg, bool byPlayer = true)
    {
        Fx.Explosion(p, radius);
        Game.I.Noise(p, 90);
        var hits = Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore);
        var done = new System.Collections.Generic.HashSet<Object>();
        foreach (var h in hits)
        {
            float k = 1 - Mathf.Clamp01(Vector3.Distance(h.ClosestPoint(p), p) / radius);
            var e = h.GetComponentInParent<Enemy>(); if (e && done.Add(e)) { e.Damage(dmg * (0.3f + 0.7f * k), false, p, byPlayer); continue; }
            var pl = h.GetComponentInParent<Player>(); if (pl && done.Add(pl)) { pl.Hurt(dmg * 0.6f * k, p); continue; }
            var v = h.GetComponentInParent<Vehicle>(); if (v && done.Add(v)) { v.Damage(dmg * k); continue; }
            var d = h.GetComponentInParent<Destructible>(); if (d && done.Add(d)) { d.Damage(dmg * k, h.ClosestPoint(p)); continue; }
            if (h.attachedRigidbody) h.attachedRigidbody.AddExplosionForce(600 * k, p, radius);
        }
    }
}
