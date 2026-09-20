using UnityEngine;

// Thrown grenades (bounce, fuse) and RPG rockets (straight, smoke trail, impact detonation).
public class Projectile : MonoBehaviour
{
    public bool rocket, byPlayer = true;
    float fuse = 2.4f, life;
    Rigidbody rb; ParticleSystem trail; Vector3 vel;

    public static Projectile Grenade(Vector3 pos, Vector3 velocity, bool byPlayer)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "Grenade"; g.transform.position = pos; g.transform.localScale = Vector3.one * 0.12f;
        var r = g.GetComponent<Renderer>(); r.material.color = new Color(0.25f, 0.3f, 0.2f);
        var rb = g.AddComponent<Rigidbody>(); rb.mass = 0.4f; rb.linearVelocity = velocity; rb.angularDamping = 1; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var pm = new PhysicsMaterial { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.6f }; g.GetComponent<Collider>().material = pm;
        g.layer = 2;
        var p = g.AddComponent<Projectile>(); p.rb = rb; p.byPlayer = byPlayer; return p;
    }

    public static Projectile Rocket(Vector3 pos, Vector3 dir, bool byPlayer)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Capsule); g.name = "Rocket"; Object.Destroy(g.GetComponent<Collider>());
        g.transform.position = pos; g.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0); g.transform.localScale = new Vector3(0.09f, 0.25f, 0.09f);
        g.GetComponent<Renderer>().material.color = new Color(0.3f, 0.32f, 0.25f);
        var p = g.AddComponent<Projectile>(); p.rocket = true; p.vel = dir * 55; p.byPlayer = byPlayer;
        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 5; l.intensity = 2; l.color = new Color(1, 0.6f, 0.3f);
        return p;
    }

    void Update()
    {
        life += Time.deltaTime;
        if (rocket)
        {
            var step = vel * Time.deltaTime;
            if (Physics.Raycast(transform.position, vel.normalized, out var hit, step.magnitude + 0.2f, ~(1 << 2), QueryTriggerInteraction.Ignore) || life > 5)
            {
                Explode.At(hit.collider ? hit.point : transform.position, 6.5f, 160, byPlayer); Destroy(gameObject); return;
            }
            transform.position += step;
            if (Time.frameCount % 2 == 0) Fx.Tracer(transform.position, transform.position - vel.normalized * 0.9f, new Color(0.8f, 0.8f, 0.8f, 0.6f));
            return;
        }
        fuse -= Time.deltaTime;
        if (fuse <= 0) { Explode.At(transform.position, 7f, 130, byPlayer); Destroy(gameObject); }
    }

    void OnCollisionEnter(Collision c) { if (!rocket && rb && rb.linearVelocity.magnitude > 2) Sfx.At(Sfx.Pick("shells", 0.25f, 6), transform.position, 0.4f, 25, 0.6f); }
}
