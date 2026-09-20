using UnityEngine;
using UnityEditor;

// Smoke test for the lag-compensated hit geometry. The server no longer uses Unity colliders for
// player hits - it rewinds positions and tests a capsule plus a head sphere by hand - so a mistake
// in that maths would silently break every shot in an online match. Run from the build script.
public static class NetTests
{
    static int failed;

    static void Check(string what, bool expectHit, bool expectHead, Vector3 origin, Vector3 dir, Vector3 feet, bool crouch = false)
    {
        float d; bool head;
        bool hit = NetPlayer.TestHit(origin, dir, 200f, feet, crouch, out d, out head);
        bool ok = hit == expectHit && (!expectHit || head == expectHead);
        if (!ok) failed++;
        Debug.Log("[NetTest] " + (ok ? "ok   " : "FAIL ") + what + "  hit=" + hit + " head=" + head + " dist=" + d.ToString("0.00"));
    }

    [MenuItem("Build/Net tests")]
    public static void Run()
    {
        failed = 0;
        var body = new Vector3(0, 0, 20);                       // a soldier standing 20 m away

        Check("chest, level shot", true, false, new Vector3(0, 1.0f, 0), Vector3.forward, body);
        Check("head shot", true, true, new Vector3(0, 1.7f, 0), Vector3.forward, body);
        Check("legs", true, false, new Vector3(0, 0.5f, 0), Vector3.forward, body);
        Check("over the head", false, false, new Vector3(0, 2.6f, 0), Vector3.forward, body);
        Check("wide left", false, false, new Vector3(-1.2f, 1.0f, 0), Vector3.forward, body);
        Check("grazing the shoulder", true, false, new Vector3(-0.3f, 1.3f, 0), Vector3.forward, body);
        Check("behind the shooter", false, false, new Vector3(0, 1.0f, 40), Vector3.forward, body);
        Check("crouched: chest shot still lands", true, false, new Vector3(0, 0.9f, 0), Vector3.forward, body, true);
        Check("crouched: standing head line misses", false, false, new Vector3(0, 1.7f, 0), Vector3.forward, body, true);
        Check("angled down from a roof", true, false, new Vector3(0, 9f, 12f), new Vector3(0, -9f, 8f), body);

        Debug.Log("[Builder] net tests " + (failed == 0 ? "PASSED" : "FAILED " + failed));
    }
}
