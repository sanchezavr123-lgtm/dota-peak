using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DotaPeak;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "sanchezavr123-lgtm.DotaPeak";
    public const string PluginName = "DotaPeak";
    public const string PluginVersion = "0.4.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    private ConfigEntry<bool> enabledConfig = null!;
    private ConfigEntry<KeyCode> abilityKey = null!;
    private ConfigEntry<KeyCode> menuKey = null!;
    private ConfigEntry<KeyCode> shopKey = null!;
    private ConfigEntry<KeyCode> blinkKey = null!;
    private ConfigEntry<string> savedHero = null!;
    private ConfigEntry<int> savedGold = null!;
    private ConfigEntry<bool> savedBlink = null!;
    private ConfigEntry<bool> savedBoots = null!;

    private Hero hero = Hero.Pudge;
    private float abilityCooldown;
    private float blinkCooldown;
    private float bladeFuryTime;
    private float goldTimer;
    private bool heroMenu;
    private bool shopMenu;
    private float baseMovementModifier = 1f;
    private bool movementBaselineCaptured;
    private GameObject? hookEffect;
    private GameObject? bladeFuryEffect;

    private const int BlinkCost = 50;
    private const int BootsCost = 25;
    private const float BootsMultiplier = 1.12f;
    private const float BladeFuryMultiplier = 1.35f;

    private enum Hero { Pudge, Juggernaut }

    private void Awake()
    {
        Log = Logger;
        enabledConfig = Config.Bind("DotaPeak", "Enabled", true, "Enable the DotaPeak gameplay layer.");
        abilityKey = Config.Bind("Controls", "Ability", KeyCode.Q, "Selected hero ability.");
        menuKey = Config.Bind("Controls", "HeroMenu", KeyCode.F1, "Open hero selection.");
        shopKey = Config.Bind("Controls", "Shop", KeyCode.B, "Open the Dota item shop.");
        blinkKey = Config.Bind("Controls", "Blink", KeyCode.E, "Use Blink when owned.");
        savedHero = Config.Bind("Save", "Hero", "Pudge", "Last selected hero.");
        savedGold = Config.Bind("Save", "Gold", 0, "Persistent prototype gold balance.");
        savedBlink = Config.Bind("Save", "Blink", false, "Whether Blink has been purchased.");
        savedBoots = Config.Bind("Save", "BootsOfSpeed", false, "Whether Boots of Speed have been purchased.");
        hero = ParseHero(savedHero.Value);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. PEAK host confirmed.");
    }

    private void Update()
    {
        if (!enabledConfig.Value) return;
        TickCooldowns();
        AwardTimeGold();
        ApplyMovementModifier();

        if (Input.GetKeyDown(menuKey.Value)) { heroMenu = !heroMenu; shopMenu = false; }
        if (Input.GetKeyDown(shopKey.Value)) { shopMenu = !shopMenu; heroMenu = false; }
        if (heroMenu) { HandleHeroMenu(); return; }
        if (shopMenu) { HandleShop(); return; }

        if (Input.GetKeyDown(blinkKey.Value)) UseBlink();
        if (Input.GetKeyDown(abilityKey.Value) && abilityCooldown <= 0f)
        {
            if (hero == Hero.Pudge) UsePudgeHook();
            else UseJuggernautBladeFury();
        }
    }

    private void TickCooldowns()
    {
        abilityCooldown = Mathf.Max(0f, abilityCooldown - Time.deltaTime);
        blinkCooldown = Mathf.Max(0f, blinkCooldown - Time.deltaTime);
        float previousBlade = bladeFuryTime;
        bladeFuryTime = Mathf.Max(0f, bladeFuryTime - Time.deltaTime);
        if (previousBlade > 0f && bladeFuryTime <= 0f && bladeFuryEffect != null)
        {
            Destroy(bladeFuryEffect);
            bladeFuryEffect = null;
        }
    }

    private void AwardTimeGold()
    {
        goldTimer += Time.deltaTime;
        if (goldTimer < 45f) return;
        goldTimer -= 45f;
        savedGold.Value += 5;
        Log.LogInfo($"DotaPeak: +5 gold. Gold={savedGold.Value}");
    }

    private void HandleHeroMenu()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectHero(Hero.Pudge);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectHero(Hero.Juggernaut);
    }

    private void SelectHero(Hero selected)
    {
        hero = selected;
        savedHero.Value = hero.ToString();
        abilityCooldown = 0f;
        if (bladeFuryEffect != null) { Destroy(bladeFuryEffect); bladeFuryEffect = null; }
        bladeFuryTime = 0f;
        Log.LogInfo($"Hero selected: {hero}");
    }

    private void HandleShop()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) BuyBoots();
        if (Input.GetKeyDown(KeyCode.Alpha2)) BuyBlink();
    }

    private void BuyBoots()
    {
        if (savedBoots.Value || !SpendGold(BootsCost)) return;
        savedBoots.Value = true;
        Log.LogInfo("Purchased Boots of Speed.");
    }

    private void BuyBlink()
    {
        if (savedBlink.Value || !SpendGold(BlinkCost)) return;
        savedBlink.Value = true;
        Log.LogInfo("Purchased Blink.");
    }

    private bool SpendGold(int amount)
    {
        if (savedGold.Value < amount) { Log.LogInfo($"Not enough gold: need {amount}, have {savedGold.Value}."); return false; }
        savedGold.Value -= amount;
        return true;
    }

    private void UsePudgeHook()
    {
        Character? character = Character.localCharacter;
        Camera? camera = Camera.main;
        if (character == null || camera == null) return;

        Vector3 start = camera.transform.position + camera.transform.forward * 0.8f;
        Ray ray = new(camera.transform.position, camera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, 35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            abilityCooldown = 1f;
            SpawnHookProjectile(start, start + camera.transform.forward * 12f, false);
            Log.LogInfo("Pudge Hook: missed.");
            return;
        }

        SpawnHookProjectile(start, hit.point, true);
        Vector3 offset = hit.point - character.Center;
        if (offset.magnitude > 2.5f)
            WarpCharacter(character, hit.point - offset.normalized * 2f);

        abilityCooldown = 4f;
        Log.LogInfo($"Pudge Hook -> {hit.point}");
    }

    private void SpawnHookProjectile(Vector3 start, Vector3 target, bool hit)
    {
        if (hookEffect != null) Destroy(hookEffect);
        hookEffect = new GameObject("DotaPeak_MeatHook");
        HookProjectile projectile = hookEffect.AddComponent<HookProjectile>();
        projectile.Initialize(start, target, hit);
    }

    private void UseJuggernautBladeFury()
    {
        bladeFuryTime = 3f;
        abilityCooldown = 12f;
        if (bladeFuryEffect != null) Destroy(bladeFuryEffect);
        bladeFuryEffect = CreateBladeFuryEffect();
        Log.LogInfo("Juggernaut Blade Fury activated for 3 seconds.");
    }

    private GameObject CreateBladeFuryEffect()
    {
        GameObject root = new GameObject("DotaPeak_BladeFury");
        BladeFuryVfx vfx = root.AddComponent<BladeFuryVfx>();
        vfx.Initialize();
        return root;
    }

    private void UseBlink()
    {
        if (!savedBlink.Value || blinkCooldown > 0f) return;
        Character? character = Character.localCharacter;
        Camera? camera = Camera.main;
        if (character == null || camera == null) return;
        Vector3 origin = character.Center;
        Vector3 destination = origin + camera.transform.forward * 10f;
        if (Physics.Raycast(origin + Vector3.up * 0.5f, camera.transform.forward, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            destination = hit.point - camera.transform.forward * 1.5f;
        WarpCharacter(character, destination);
        blinkCooldown = 12f;
        Log.LogInfo($"Blink -> {destination}");
    }

    private static void WarpCharacter(Character character, Vector3 destination) => character.WarpPlayer(destination, false);

    private void ApplyMovementModifier()
    {
        Character? character = Character.localCharacter;
        if (character == null || character.refs == null || character.refs.movement == null) { movementBaselineCaptured = false; return; }
        if (!movementBaselineCaptured) { baseMovementModifier = character.refs.movement.movementModifier; movementBaselineCaptured = true; }
        float multiplier = savedBoots.Value ? BootsMultiplier : 1f;
        if (bladeFuryTime > 0f) multiplier *= BladeFuryMultiplier;
        character.refs.movement.movementModifier = baseMovementModifier * multiplier;
    }

    private static Hero ParseHero(string value) => Enum.TryParse(value, true, out Hero result) ? result : Hero.Pudge;

    private void OnGUI()
    {
        if (!enabledConfig.Value) return;
        GUI.Box(new Rect(15, 15, 390, 105), "DOTA x PEAK");
        GUI.Label(new Rect(30, 42, 350, 22), $"Hero: {hero}    Gold: {savedGold.Value}");
        GUI.Label(new Rect(30, 64, 350, 22), $"Q: {FormatCooldown(abilityCooldown)}    E Blink: {FormatCooldown(blinkCooldown)}");
        GUI.Label(new Rect(30, 86, 350, 22), "F1 Heroes   B Shop   E Blink   Q Ability");
        if (bladeFuryTime > 0f) GUI.Label(new Rect(30, 120, 350, 24), $"BLADE FURY {bladeFuryTime:0.0}s");
        if (heroMenu)
        {
            GUI.Box(new Rect(15, 150, 390, 145), "Hero Selection");
            GUI.Label(new Rect(35, 180, 350, 25), "1 - Pudge | Q: Meat Hook");
            GUI.Label(new Rect(35, 220, 350, 25), "2 - Juggernaut | Q: Blade Fury");
        }
        if (shopMenu)
        {
            GUI.Box(new Rect(15, 150, 390, 170), "Dota Shop");
            GUI.Label(new Rect(35, 180, 350, 25), $"1 - Boots ({BootsCost}) {(savedBoots.Value ? "OWNED" : "")}");
            GUI.Label(new Rect(35, 215, 350, 25), $"2 - Blink ({BlinkCost}) {(savedBlink.Value ? "OWNED" : "")}");
            GUI.Label(new Rect(35, 250, 350, 25), "Gold: +5 / 45 sec");
            GUI.Label(new Rect(35, 278, 350, 25), "B - close shop");
        }
    }

    private static string FormatCooldown(float value) => value <= 0f ? "READY" : $"{value:0.0}s";

    private sealed class HookProjectile : MonoBehaviour
    {
        private Vector3 start;
        private Vector3 target;
        private float time;
        private float duration;
        private bool hit;
        private LineRenderer line = null!;
        private GameObject head = null!;

        public void Initialize(Vector3 from, Vector3 to, bool didHit)
        {
            start = from; target = to; hit = didHit;
            duration = Mathf.Clamp(Vector3.Distance(from, to) / 45f, 0.08f, 0.38f);
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.035f;
            line.endWidth = 0.09f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = new Color(0.85f, 0.78f, 0.58f, 1f);
            line.endColor = new Color(0.35f, 0.28f, 0.2f, 1f);

            head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "DotaPeak_HookHead";
            head.transform.localScale = Vector3.one * 0.18f;
            head.GetComponent<Collider>().enabled = false;
            Renderer renderer = head.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.3f, 0.22f, 0.14f, 1f);
        }

        private void Update()
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            Vector3 pos = Vector3.Lerp(start, target, t);
            line.SetPosition(0, start);
            line.SetPosition(1, pos);
            head.transform.position = pos;
            head.transform.Rotate(0f, 720f * Time.deltaTime, 0f, Space.Self);
            if (t >= 1f)
            {
                if (hit) SpawnImpact(target);
                Destroy(head);
                Destroy(gameObject, 0.04f);
                enabled = false;
            }
        }

        private void SpawnImpact(Vector3 position)
        {
            GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = "DotaPeak_HookImpact";
            impact.transform.position = position;
            impact.transform.localScale = Vector3.one * 0.32f;
            impact.GetComponent<Collider>().enabled = false;
            Renderer renderer = impact.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.9f, 0.7f, 0.25f, 0.8f);
            Destroy(impact, 0.16f);
        }
    }

    private sealed class BladeFuryVfx : MonoBehaviour
    {
        private Transform player = null!;
        private LineRenderer[] rings = Array.Empty<LineRenderer>();
        private float spin;

        public void Initialize()
        {
            player = Character.localCharacter != null ? Character.localCharacter.transform : null!;
            rings = new LineRenderer[3];
            for (int i = 0; i < rings.Length; i++)
            {
                LineRenderer ring = gameObject.AddComponent<LineRenderer>();
                ring.positionCount = 25;
                ring.loop = true;
                ring.useWorldSpace = false;
                ring.startWidth = 0.055f;
                ring.endWidth = 0.015f;
                ring.material = new Material(Shader.Find("Sprites/Default"));
                float alpha = 0.85f - i * 0.18f;
                ring.startColor = new Color(1f, 0.8f, 0.18f, alpha);
                ring.endColor = new Color(0.9f, 0.3f, 0.05f, alpha);
                rings[i] = ring;
            }
        }

        private void Update()
        {
            if (player == null) { Destroy(gameObject); return; }
            transform.position = player.position + Vector3.up * 0.75f;
            spin += Time.deltaTime * 10f;
            for (int r = 0; r < rings.Length; r++)
            {
                float radius = 0.7f + r * 0.32f;
                float height = 0.25f + r * 0.18f;
                for (int i = 0; i < 25; i++)
                {
                    float a = (i / 24f) * Mathf.PI * 2f + spin * (1f + r * 0.15f);
                    float y = Mathf.Sin(a * (2f + r)) * height;
                    rings[r].SetPosition(i, new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                }
            }
        }
    }
}
