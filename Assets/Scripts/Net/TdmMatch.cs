using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Team deathmatch: 6v6, first team to the kill target or the highest score when the clock runs out.
// All of it runs on the server; clients only read the synced numbers for the HUD.
public class TdmMatch : NetworkBehaviour
{
    public static TdmMatch I;

    public const int KillTarget = 75;
    public const float MatchSeconds = 600f, RespawnSeconds = 8f;

    public readonly SyncVar<int> scoreA = new SyncVar<int>();
    public readonly SyncVar<int> scoreB = new SyncVar<int>();
    public readonly SyncVar<float> timeLeft = new SyncVar<float>(MatchSeconds);
    public readonly SyncVar<bool> over = new SyncVar<bool>();
    public readonly SyncVar<int> winner = new SyncVar<int>(-1);

    readonly List<NetPlayer> players = new List<NetPlayer>();
    readonly Dictionary<NetPlayer, float> respawnAt = new Dictionary<NetPlayer, float>();

    // two opposite corners of the map, far enough apart to give each side a safe start
    public static bool DevDuel;                     // test flag: both teams start together

    public static Vector3 SpawnFor(int team, int index)
    {
        var baseP = team == 0 || DevDuel ? new Vector3(-150, 0, -120) : new Vector3(150, 0, 120);
        float a = index * 0.9f;
        var p = baseP + new Vector3(Mathf.Cos(a) * 12f, 0, Mathf.Sin(a) * 12f);
        return World.OnGround(p) + Vector3.up * 0.4f;
    }

    public int PlayerCount { get { int n = 0; foreach (var p in players) if (p) n++; return n; } }

    void Awake() { I = this; }
    // the scene object starts deactivated, so Awake may be skipped until it is spawned
    public override void OnStartNetwork() { base.OnStartNetwork(); I = this; }

    bool Ready => NetworkObject != null && NetworkObject.IsSpawned && IsServerInitialized;

    public void OnPlayerJoined(NetPlayer p)
    {
        if (!Ready || !p) { Debug.LogWarning("[Net] join ignored: ready=" + Ready + " spawned=" + (NetworkObject != null && NetworkObject.IsSpawned)); return; }
        int a = 0, b = 0;
        foreach (var q in players) { if (!q) continue; if (q.team.Value == 0) a++; else b++; }
        p.team.Value = a <= b ? 0 : 1;
        players.Add(p);
        p.ServerRespawn(SpawnFor(p.team.Value, players.Count));
    }

    public void OnKill(NetPlayer killer, NetPlayer victim)
    {
        if (!Ready) return;
        if (killer && killer.team.Value == 0) scoreA.Value++;
        else if (killer) scoreB.Value++;
        respawnAt[victim] = Time.time + RespawnSeconds;
        if (scoreA.Value >= KillTarget || scoreB.Value >= KillTarget) End();
    }

    void End()
    {
        over.Value = true;
        winner.Value = scoreA.Value == scoreB.Value ? -1 : (scoreA.Value > scoreB.Value ? 0 : 1);
    }

    void Update()
    {
        if (!Ready || over.Value) return;
        timeLeft.Value -= Time.deltaTime;
        if (timeLeft.Value <= 0) { timeLeft.Value = 0; End(); return; }

        // respawn anyone whose timer elapsed
        if (respawnAt.Count == 0) return;
        List<NetPlayer> done = null;
        foreach (var kv in respawnAt)
        {
            if (Time.time < kv.Value) continue;
            (done ??= new List<NetPlayer>()).Add(kv.Key);
        }
        if (done == null) return;
        foreach (var p in done)
        {
            respawnAt.Remove(p);
            if (p) p.ServerRespawn(SpawnFor(p.team.Value, players.IndexOf(p) + 1));
        }
    }
}
