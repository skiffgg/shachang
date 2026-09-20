using System.IO;
using UnityEngine;

[System.Serializable]
public class WeaponTune
{
    public string id; public Vector3 modelPos; public float scale = 1; public Vector3 R, L, muzzle;
    // nudges for mounted parts, in the weapon model's local space
    public Vector3 attSight, attSilencer, attLaser;
}

// Values that need visual tuning live in StreamingAssets/tune.json so they can be changed
// without rebuilding (press F5 in game to reload).
[System.Serializable]
public class Tune
{
    public Vector3 fpPos = new Vector3(0.12f, -0.24f, 0.32f);
    public Vector3 fpRot = new Vector3(0f, 180f, 0f);
    public float fpScale = 1f;
    public Vector3 adsOffset = new Vector3(-0.12f, 0.012f, 0.06f);
    public float fov = 70f;
    public float moveSpeed = 5.5f, sprintSpeed = 8.5f, crouchSpeed = 2.6f, mouseSens = 2.0f;
    public Vector3 enemyGunPos = new Vector3(0f, 0.03f, -0.17f);
    public float vehicleYaw = 180f;
    public float renderScale = 0.85f;
    public int enemyCount = 6;
    // IK arms
    public Vector3 holderPos = new Vector3(0.15f, -0.19f, 0.22f);
    public Vector3 shoulderPos = new Vector3(0.03f, -0.33f, -0.05f);
    public float armsScale = 1.3f;
    public Vector3 poleR = new Vector3(0.6f, -1f, -0.4f), poleL = new Vector3(-0.5f, -1f, -0.1f);
    public Vector3 handRf = new Vector3(-1f, -0.3f, 0.2f), handRt = new Vector3(0f, 0.5f, 0.8f), handLf = new Vector3(1f, 0.4f, 0.3f), handLt = new Vector3(-0.5f, 0.6f, 0.6f);
    public Vector3 curlAxis = new Vector3(1, 0, 0); public float curlR = -60f, curlL = -55f;
    public WeaponTune[] weapons =
    {
        new WeaponTune { id = "shotgun", modelPos = new Vector3(0, -0.03f, 0.12f), scale = 0.95f, R = new Vector3(0, -0.1f, 0), L = new Vector3(0, -0.07f, 0.4f), muzzle = new Vector3(0, 0, 0.6f), attSight = new Vector3(0, -0.01f, -0.03f) },
        new WeaponTune { id = "sniper", modelPos = new Vector3(0, -0.02f, 0.28f), scale = 0.8f, R = new Vector3(0, -0.1f, 0), L = new Vector3(0, -0.08f, 0.34f), muzzle = new Vector3(0, 0, 0.86f) },
        new WeaponTune { id = "smg", modelPos = new Vector3(0, -0.03f, 0.16f), scale = 0.85f, R = new Vector3(0, -0.1f, 0.12f), L = new Vector3(0, -0.13f, 0.3f), muzzle = new Vector3(0, 0, 0.46f), attSight = new Vector3(0, -0.055f, 0.02f) },
        new WeaponTune { id = "rpg", modelPos = new Vector3(0, 0.05f, 0), scale = 1f, R = new Vector3(0, -0.05f, 0.1f), L = new Vector3(0, -0.05f, 0.27f), muzzle = new Vector3(0, 0.05f, 0.55f) },
    };
    // attachment offsets relative to the auto-detected mount points on the M16
    public Vector3 attHolo = new Vector3(0, -0.095f, 0), attAcog = new Vector3(0, -0.095f, 0), attSilencer = new Vector3(0, 0, 0.02f), attGrip = new Vector3(0, -0.02f, 0), attLaser = new Vector3(0.01f, 0, 0);

    public WeaponTune Weapon(string id) { foreach (var w in weapons) if (w.id == id) return w; return weapons[0]; }

    public static Tune I = new Tune();
    static string PathFile => Path.Combine(Application.streamingAssetsPath, "tune.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(PathFile)) I = JsonUtility.FromJson<Tune>(File.ReadAllText(PathFile));
            else File.WriteAllText(PathFile, JsonUtility.ToJson(I, true));
        }
        catch (System.Exception e) { Debug.LogWarning("tune.json: " + e.Message); }
    }
}

