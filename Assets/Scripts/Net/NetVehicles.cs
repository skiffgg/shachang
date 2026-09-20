using System.Collections.Generic;
using UnityEngine;

// Vehicle state for online matches.
//
// Every machine generates the same world from the same seed and spawns the same car list, so the
// vehicles already exist everywhere; they only need an owner and a pose. Whoever sits in the driver
// seat streams the transform through his own NetPlayer object, the server hands out the seat, and
// everyone else glides their local copy toward the relayed pose. One small packet per moving car,
// and nothing has to be spawned over the network.
//
// This is deliberately plain state rather than a NetworkBehaviour: observer rpcs sent from the
// shared match object never reached the other clients, while the per-player object is a proven path.
public static class NetVehicles
{
    public const float SendHz = 20f;

    public struct Pose { public Vector3 p, e; public float rpm; }

    static List<Vehicle> order;                                       // stable index shared by every machine
    static readonly Dictionary<int, int> seat = new Dictionary<int, int>();     // vehicle index -> client id
    static readonly Dictionary<int, Pose> target = new Dictionary<int, Pose>(); // vehicle index -> relayed pose
    static readonly List<int> tmp = new List<int>();
    static float sendT;

    public static bool Online
    {
        get { return Game.I && Game.I.mode == Mode.Online && Game.I.player && Game.I.player.net != null; }
    }

    // ---------------------------------------------------------------- stable identity
    // sorted by kind and rounded position, so the numbering can never depend on spawn timing
    static string Key(Vehicle v) { return v.kind + "|" + Mathf.RoundToInt(v.transform.position.x) + "|" + Mathf.RoundToInt(v.transform.position.z); }

    static List<Vehicle> Order()
    {
        if (order != null) return order;
        order = new List<Vehicle>(Vehicle.All);
        order.Sort((a, b) => string.CompareOrdinal(Key(a), Key(b)));
        return order;
    }

    public static void Reset() { order = null; seat.Clear(); target.Clear(); }

    public static int IndexOf(Vehicle v) { return v ? Order().IndexOf(v) : -1; }
    public static Vehicle At(int i) { var o = Order(); return i >= 0 && i < o.Count ? o[i] : null; }

    public static bool Riding(int clientId)
    {
        foreach (var kv in seat) if (kv.Value == clientId) return true;
        return false;
    }

    public static int SeatOwner(int idx) { int who; return seat.TryGetValue(idx, out who) ? who : -1; }
    public static bool Taken(Vehicle v) { return SeatOwner(IndexOf(v)) >= 0; }

    // ---------------------------------------------------------------- seats
    public static void Enter(Vehicle v)
    {
        if (!Online) return;
        int i = IndexOf(v); if (i >= 0) Game.I.player.net.ClaimVehicle(i, true);
    }

    public static void Leave(Vehicle v)
    {
        if (!Online) return;
        int i = IndexOf(v); if (i < 0) return;
        target.Remove(i);
        Game.I.player.net.ClaimVehicle(i, false);
    }

    // applied on every machine from the rpc
    public static void ApplySeat(int idx, int clientId, bool mine)
    {
        if (clientId < 0) { seat.Remove(idx); target.Remove(idx); }
        else seat[idx] = clientId;
        var v = At(idx); if (!v || mine) return;                      // my own car is driven, not replayed
        v.occupied = clientId >= 0;
    }

    public static void ApplyPose(int idx, Vector3 pos, Vector3 euler, float rpm)
    {
        target[idx] = new Pose { p = pos, e = euler, rpm = rpm };
        var v = At(idx); if (v) v.occupied = true;
    }

    // vehicle damage stays client side; the first machine that kills one tells everybody else, so a
    // wreck never survives on one screen and drives on another
    public static void ReportDead(Vehicle v)
    {
        if (!Online) return;
        int i = IndexOf(v); if (i >= 0) Game.I.player.net.ReportVehicleDead(i);
    }

    public static void ApplyDead(int idx)
    {
        target.Remove(idx); seat.Remove(idx);
        var v = At(idx); if (v && !v.dead) v.NetKill();
    }

    // ---------------------------------------------------------------- per-frame work on a client
    public static void Tick(Player p)
    {
        if (!Online) return;

        if (p != null && p.inVehicle && p.Vehicle)
        {
            sendT -= Time.deltaTime;
            if (sendT <= 0)
            {
                sendT = 1f / SendHz;
                int i = IndexOf(p.Vehicle);
                var t = p.Vehicle.transform;
                if (i >= 0) p.net.StreamVehicle(i, t.position, t.eulerAngles, p.Vehicle.rpm);
            }
        }

        if (target.Count == 0) return;
        tmp.Clear(); tmp.AddRange(target.Keys);
        foreach (int i in tmp)
        {
            var v = At(i);
            if (!v || v.dead) { target.Remove(i); continue; }
            if (p != null && p.inVehicle && p.Vehicle == v) continue;
            var d = target[i];
            v.transform.position = Vector3.Lerp(v.transform.position, d.p, 1f - Mathf.Exp(-14f * Time.deltaTime));
            v.transform.rotation = Quaternion.Slerp(v.transform.rotation, Quaternion.Euler(d.e), 1f - Mathf.Exp(-12f * Time.deltaTime));
            v.rpm = d.rpm;                                            // keeps a flying helicopter's rotor spinning
        }
    }
}
