using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

// Starts the networking stack using the serialized NetworkManager, transport
// and registered player prefab, as either a dedicated server or a client.
//
//   ShaChang.exe -server -port 7777 -mode tdm      (headless match server)
//   ShaChang.exe -connect 1.2.3.4 -port 7777       (client joins straight away)
public class NetBoot : MonoBehaviour
{
    public static NetBoot I;
    public static bool IsServerBuild, WantsClient;
    public static string ConnectIp = "127.0.0.1";
    public static ushort Port = 7777;

    public NetworkManager manager;
    public Tugboat transport;
    NetworkObject playerPrefab, matchPrefab, matchInstance;

    public static void ParseArgs()
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length; i++)
        {
            string s = a[i].ToLower();
            if (s == "-server") IsServerBuild = true;
            else if (s == "-connect" && i + 1 < a.Length) { WantsClient = true; ConnectIp = a[i + 1]; }
            else if (s == "-port" && i + 1 < a.Length) ushort.TryParse(a[i + 1], out Port);
            else if (s == "-duel") TdmMatch.DevDuel = true;
        }
#if UNITY_SERVER
        IsServerBuild = true;
#endif
    }

    public static NetBoot Create()
    {
        // the manager lives in the scene; this only finds it
        if (!I) I = FindAnyObjectByType<NetBoot>();
        return I;
    }

    void Awake()
    {
        I = this;
        manager = GetComponent<NetworkManager>();
        transport = GetComponent<Tugboat>();
        playerPrefab = Resources.Load<GameObject>("Net/NetPlayer")?.GetComponent<NetworkObject>();
        matchPrefab = Resources.Load<GameObject>("Net/Match")?.GetComponent<NetworkObject>();
        // spawn a player only once that client has finished loading the scene (FishNet's own advice)
        manager.SceneManager.OnClientLoadedStartScenes += OnClientReady;
        manager.ServerManager.OnServerConnectionState += OnServerState;
        manager.ClientManager.OnClientConnectionState += OnClientState;
    }

    void OnDestroy()
    {
        if (!manager) return;
        manager.SceneManager.OnClientLoadedStartScenes -= OnClientReady;
        manager.ServerManager.OnServerConnectionState -= OnServerState;
        manager.ClientManager.OnClientConnectionState -= OnClientState;
        if (I == this) I = null;
    }

    void OnServerState(ServerConnectionStateArgs args)
    {
        Debug.Log("[Net] server transport " + args.ConnectionState + " port=" + Port);
        if (args.ConnectionState == LocalConnectionState.Started) StartCoroutine(SpawnMatch());
    }

    void OnClientState(ClientConnectionStateArgs args)
    {
        Debug.Log("[Net] client transport " + args.ConnectionState);
    }

    void OnClientReady(FishNet.Connection.NetworkConnection conn, bool asServer)
    {
        if (!asServer || playerPrefab == null) return;
        var inst = Instantiate(playerPrefab);
        inst.gameObject.SetActive(true);
        InstanceFinder.ServerManager.Spawn(inst, conn);
        if (TdmMatch.I) TdmMatch.I.OnPlayerJoined(inst.GetComponent<NetPlayer>());
        Debug.Log("[Net] client scene ready id=" + conn.ClientId + " players=" + (TdmMatch.I ? TdmMatch.I.PlayerCount : 0));
    }

    // the match state lives in the scene; make sure the server has spawned it before anyone joins
    IEnumerator SpawnMatch()
    {
        yield return null;
        if (matchPrefab == null) { Debug.LogError("[Net] match prefab missing from Resources/Net"); yield break; }
        // the transport reports Started more than once on some hosts; one match object is enough
        if (matchInstance != null && matchInstance.IsSpawned) yield break;
        var inst = Instantiate(matchPrefab);
        inst.gameObject.SetActive(true);
        InstanceFinder.ServerManager.Spawn(inst);
        yield return null;
        if (!inst.IsSpawned) { Destroy(inst.gameObject); Debug.LogWarning("[Net] match spawn failed, waiting for the transport"); yield break; }
        matchInstance = inst;
        Debug.Log("[Net] match spawned=" + inst.IsSpawned + " rules=" + (TdmMatch.I != null));
    }

    // Rpc links are a header compression that silently drops rpcs when the two sides disagree on the
    // link table; the few bytes they save are not worth losing player and vehicle updates.
    void DisableRpcLinks()
    {
        var dbg = manager ? manager.DebugManager : null;
        if (!dbg) return;
        dbg.DisableObserversRpcLinks = dbg.DisableTargetRpcLinks = dbg.DisableServerRpcLinks = true;
    }

    public void StartServer()
    {
        DisableRpcLinks();
        transport.SetPort(Port);
        manager.ServerManager.StartConnection();
        Debug.Log("[Net] server start requested on " + Port);
    }

    public void StartClient(string ip, ushort port)
    {
        DisableRpcLinks();
        transport.SetClientAddress(ip); transport.SetPort(port);
        manager.ClientManager.StartConnection(ip, port);
        Debug.Log("[Net] connecting to " + ip + ":" + port);
    }

    public void Stop()
    {
        if (manager.ServerManager.Started) manager.ServerManager.StopConnection(true);
        if (manager.ClientManager.Started) manager.ClientManager.StopConnection();
    }
}
