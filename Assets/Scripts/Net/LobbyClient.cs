using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class RoomInfo
{
    public string host, name, mode, state;
    public int port, players, max;
    public string Line => name + "   " + players + "/" + max + "   " + (state == "playing" ? "进行中" : "等待中");
}

// Talks to the little room directory: the match server reports itself every few seconds,
// the client pulls the list for the menu.
public class LobbyClient : MonoBehaviour
{
    public static LobbyClient I;
    public static string LobbyUrl = "";                    // e.g. http://1.2.3.4:8080

    public readonly List<RoomInfo> rooms = new List<RoomInfo>();
    public string status = "";
    public bool busy;

    public static LobbyClient Create()
    {
        if (I) return I;
        var go = new GameObject("Lobby"); DontDestroyOnLoad(go);
        I = go.AddComponent<LobbyClient>();
        return I;
    }

    public void Refresh()
    {
        if (busy || string.IsNullOrEmpty(LobbyUrl)) { if (string.IsNullOrEmpty(LobbyUrl)) status = "未设置大厅地址"; return; }
        StartCoroutine(Fetch());
    }

    IEnumerator Fetch()
    {
        busy = true; status = "正在获取房间…";
        using (var req = UnityWebRequest.Get(LobbyUrl.TrimEnd('/') + "/rooms"))
        {
            req.timeout = 6;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                status = "大厅连接失败：" + req.error + "（" + req.responseCode + "）";
                Debug.LogWarning("[Lobby] " + LobbyUrl + " -> " + req.result + " " + req.error);
            }
            else
            {
                rooms.Clear();
                // JsonUtility cannot read a bare array, so wrap it
                var wrapped = "{\"items\":" + req.downloadHandler.text + "}";
                var list = JsonUtility.FromJson<Wrapper>(wrapped);
                if (list?.items != null) rooms.AddRange(list.items);
                status = rooms.Count == 0 ? "暂无房间" : "共 " + rooms.Count + " 个房间";
            }
        }
        busy = false;
    }

    [Serializable] class Wrapper { public RoomInfo[] items; }

    // ---------------------------------------------------------------- server side
    public void StartHeartbeat(string url, int port, string name, Func<int> players, Func<string> state)
    {
        LobbyUrl = url;
        StartCoroutine(Beat(port, name, players, state));
    }

    IEnumerator Beat(int port, string name, Func<int> players, Func<string> state)
    {
        var wait = new WaitForSeconds(5f);
        while (true)
        {
            string body = JsonUtility.ToJson(new Beat_(port, name, players(), state()));
            using (var req = new UnityWebRequest(LobbyUrl.TrimEnd('/') + "/heartbeat", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 5;
                yield return req.SendWebRequest();
            }
            yield return wait;
        }
    }

    [Serializable]
    class Beat_
    {
        public int port, players, max = 12; public string name, mode = "tdm", state;
        public Beat_(int p, string n, int pl, string st) { port = p; name = n; players = pl; state = st; }
    }
}
