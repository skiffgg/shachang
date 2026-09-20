using System.Collections.Generic;
using UnityEngine;

public enum LootKind { Ammo, Med, Armor, Grenade, Valuable, Attachment }

// Pickups: enemy drops, and in Extraction mode the valuables / attachments scattered in houses and ruins.
public class Loot : MonoBehaviour
{
    public static readonly List<Loot> All = new List<Loot>();
    public LootKind kind; public string label; public int value; public string attachment;
    float bob;

    static readonly string[] ValuableNames = { "军用笔记本", "加密硬盘", "金条", "卫星电话", "情报文件", "古董匕首" };
    static readonly string[] AttNames = { "holo", "acog", "silencer", "grip", "laser" };
    public static string AttLabel(string a) => a == "holo" ? "全息瞄准镜" : a == "acog" ? "ACOG 倍镜" : a == "silencer" ? "消音器" : a == "grip" ? "战术握把" : "激光指示器";

    public static Loot Drop(Vector3 pos, LootKind k)
    {
        string model = k == LootKind.Ammo ? "ammo_box" : k == LootKind.Med ? "utility_box_01" : k == LootKind.Valuable ? "old_military_crate" : k == LootKind.Grenade ? "ammo_box" : "ammo_box";
        var go = new GameObject("Loot_" + k); go.transform.position = pos;
        var src = Game.Model(model);
        if (src)
        {
            var m = Instantiate(src, go.transform); m.transform.localPosition = Vector3.zero;
            var b = Game.WorldBounds(m); float s = 0.45f / Mathf.Max(b.size.x, b.size.y, b.size.z); m.transform.localScale = Vector3.one * s;
            foreach (var r in m.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 2.5f; l.intensity = 1.2f;
        l.color = k == LootKind.Valuable ? new Color(1, 0.8f, 0.3f) : k == LootKind.Attachment ? new Color(0.5f, 0.8f, 1f) : k == LootKind.Med ? new Color(0.4f, 1, 0.6f) : new Color(1, 0.95f, 0.8f);
        var lo = go.AddComponent<Loot>(); lo.kind = k;
        switch (k)
        {
            case LootKind.Ammo: lo.label = "弹药"; break;
            case LootKind.Med: lo.label = "医疗包"; break;
            case LootKind.Armor: lo.label = "护甲"; break;
            case LootKind.Grenade: lo.label = "手雷 ×2"; break;
            case LootKind.Valuable: lo.label = ValuableNames[Random.Range(0, ValuableNames.Length)]; lo.value = Random.Range(3, 12) * 100; lo.label += "（$" + lo.value + "）"; break;
            case LootKind.Attachment: lo.attachment = AttNames[Random.Range(0, AttNames.Length)]; lo.label = AttLabel(lo.attachment); break;
        }
        All.Add(lo); return lo;
    }

    void OnDestroy() { All.Remove(this); }
    void Update() { bob += Time.deltaTime; transform.GetChild(0).localRotation = Quaternion.Euler(0, bob * 40, 0); }

    public static Loot Near(Vector3 p)
    {
        Loot best = null; float bd = 2.2f;
        foreach (var l in All) { float d = Vector3.Distance(l.transform.position, p + Vector3.up * 0.4f); if (d < bd) { bd = d; best = l; } }
        return best;
    }

    public void Take(Player p)
    {
        switch (kind)
        {
            case LootKind.Ammo: if (p.reserve[p.weapon] < Weapons.All[p.weapon].reserve / 2) p.GiveAmmo(0.5f); else p.ammoBoxes++; break;
            case LootKind.Med: if (p.hp < 40) p.hp = Mathf.Min(100, p.hp + 45); else p.medkits++; break;
            case LootKind.Armor: if (p.armor < 20) p.armor = Mathf.Min(100, p.armor + 50); else p.armorPlates++; break;
            case LootKind.Grenade: p.grenades = Mathf.Min(6, p.grenades + 2); break;
            case LootKind.Valuable: Game.I.lootValue += value; p.valuables.Add((label, value)); break;
            case LootKind.Attachment:
                var lo = p.loadout;
                if (attachment == "holo") lo.holo = true; else if (attachment == "acog") lo.acog = true; else if (attachment == "silencer") lo.silencer = true; else if (attachment == "grip") lo.grip = true; else lo.laser = true;
                if (!p.attachments.Contains(attachment)) p.attachments.Add(attachment);
                p.RefreshLoadout(); break;
        }
        Sfx.Ui(Sfx.Raw("pickup"), 0.8f); Game.I.Feed("拾取：" + label);
        Destroy(gameObject);
    }
}
