using System;
using System.Collections.Generic;
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
    public const string PluginVersion = "1.1.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<bool> enabledConfig = null!;
    private ConfigEntry<KeyCode> abilityKey = null!;
    private ConfigEntry<KeyCode> shopKey = null!;
    private ConfigEntry<KeyCode> blinkKey = null!;
    private ConfigEntry<KeyCode> sellKey = null!;
    private ConfigEntry<int> savedGold = null!;
    private ConfigEntry<bool> savedBlink = null!;
    private ConfigEntry<bool> savedBoots = null!;

    private Hero hero = Hero.Pudge;
    private float abilityCooldown;
    private float blinkCooldown;
    private float goldTimer;
    private float eventTimer;
    private float eventMessageTimer;
    private float cameraShake;
    private Vector3 cameraShakeOffset;
    private float pullTime;
    private bool heroMenu;
    private bool shopMenu;
    private bool heroSelectionRequired = true;
    private bool playerWasPresent;
    private string eventTitle = string.Empty;
    private string eventSubtitle = string.Empty;
    private bool movementBaselineCaptured;
    private float baseMovementModifier = 1f;
    private GameObject? hookEffect;
    private GameObject? tinyEffect;
    private readonly List<LootPile> lootPiles = new();

    private const int BlinkCost = 50;
    private const int BootsCost = 25;
    private const float BootsMultiplier = 1.12f;
    private const float HookCooldown = 8f;
    private const float HookRange = 15f;
    private const float TinyCooldown = 10f;
    private const float TinyRange = 7.5f;

    private enum Hero { Pudge, Tiny }

    private void Awake()
    {
        Log = Logger;
        enabledConfig = Config.Bind("DotaPeak", "Enabled", true, "Enable the DotaPeak gameplay layer.");
        abilityKey = Config.Bind("Controls", "AbilityV2", KeyCode.Z, "Dota hero ability. Z avoids PEAK drop/throw.");
        shopKey = Config.Bind("Controls", "Shop", KeyCode.B, "Open the Dota item shop.");
        blinkKey = Config.Bind("Controls", "BlinkV2", KeyCode.C, "Use Blink when owned. C avoids PEAK interaction.");
        sellKey = Config.Bind("Controls", "SellLootV2", KeyCode.X, "Sell nearby PEAK loot. X avoids PEAK emote, voice and item controls.");
        savedGold = Config.Bind("Save", "Gold", 0, "Persistent DotaPeak gold balance.");
        savedBlink = Config.Bind("Save", "Blink", false, "Whether Blink has been purchased.");
        savedBoots = Config.Bind("Save", "BootsOfSpeed", false, "Whether Boots of Speed have been purchased.");
        hero = Hero.Pudge;
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. PEAK host confirmed.");
    }

    private void Update()
    {
        if (!enabledConfig.Value) return;

        Character? currentCharacter = Character.localCharacter;
        HandleRunLifecycle(currentCharacter);

        TickTimers();
        AwardTimeGold();
        TickRunEvents();
        ApplyMovementModifier();
        TryDetectNearbyChest();

        if (!heroSelectionRequired && Input.GetKeyDown(shopKey.Value))
            shopMenu = !shopMenu;

        if (!heroSelectionRequired && shopMenu)
            HandleShop();

        if (heroSelectionRequired)
        {
            HandleHeroMenu();
            return;
        }

        if (Input.GetKeyDown(sellKey.Value)) SellNearestLoot();
        if (Input.GetKeyDown(blinkKey.Value)) UseBlink();
        if (Input.GetKeyDown(abilityKey.Value) && abilityCooldown <= 0f)
        {
            if (hero == Hero.Pudge) UsePudgeHook();
            else UseTinyToss();
        }
    }

    private void HandleRunLifecycle(Character? currentCharacter)
    {
        if (currentCharacter == null)
        {
            playerWasPresent = false;
            return;
        }

        if (playerWasPresent) return;

        playerWasPresent = true;
        BeginNewRun();
    }

    private void BeginNewRun()
    {
        // Purchases are run-local. Old config values are overwritten here so they cannot leak into a new climb.
        savedBlink.Value = false;
        savedBoots.Value = false;

        abilityCooldown = 0f;
        blinkCooldown = 0f;
        goldTimer = 0f;
        eventTimer = 0f;
        eventMessageTimer = 0f;
        cameraShake = 0f;
        cameraShakeOffset = Vector3.zero;
        movementBaselineCaptured = false;

        for (int i = lootPiles.Count - 1; i >= 0; i--)
        {
            if (lootPiles[i].Root != null) Destroy(lootPiles[i].Root);
            lootPiles.RemoveAt(i);
        }

        if (hookEffect != null) { Destroy(hookEffect); hookEffect = null; }
        if (tinyEffect != null) { Destroy(tinyEffect); tinyEffect = null; }

        hero = Hero.Pudge;
        shopMenu = false;
        heroSelectionRequired = true;
        ShowEvent("CHOOSE YOUR HERO", "PUDGE or TINY — pick your climber.", 3f);
        Log.LogInfo("DotaPeak: new run detected; run-local items and effects reset.");
    }

    private void TickTimers()
    {
        UpdateCameraShake();
        abilityCooldown = Mathf.Max(0f, abilityCooldown - Time.deltaTime);
        blinkCooldown = Mathf.Max(0f, blinkCooldown - Time.deltaTime);
        pullTime = Mathf.Max(0f, pullTime - Time.deltaTime);
        cameraShake = Mathf.Max(0f, cameraShake - Time.deltaTime);
        eventMessageTimer = Mathf.Max(0f, eventMessageTimer - Time.deltaTime);
    }

    private void AwardTimeGold()
    {
        goldTimer += Time.deltaTime;
        if (goldTimer < 45f) return;
        goldTimer -= 45f;
        savedGold.Value += 5;
        ShowEvent("SURVIVAL BONUS", "+5 GOLD", 2.5f);
    }

    private void TickRunEvents()
    {
        eventTimer += Time.deltaTime;
        if (eventTimer < 90f) return;
        eventTimer = 0f;

        int roll = UnityEngine.Random.Range(0, 4);
        if (roll == 0) SpawnRichCache();
        else if (roll == 1) StartAnomaly();
        else if (roll == 2) SpawnDotaCache();
        else ShowEvent("MOUNTAIN EVENT", "A rare opportunity appeared nearby.", 4f);
    }

    private void SpawnRichCache()
    {
        Character? c = Character.localCharacter;
        if (c == null) return;
        Vector3 pos = c.Center + Vector3.up * 0.2f + c.transform.forward * 4f;
        CreateLootPile(pos, "RICH CACHE", 35);
        ShowEvent("RICH CACHE", "A valuable cache appeared nearby.", 4f);
    }

    private void SpawnDotaCache()
    {
        Character? c = Character.localCharacter;
        if (c == null) return;
        Vector3 pos = c.Center + Vector3.up * 0.2f + c.transform.forward * 3f;
        CreateLootPile(pos, hero == Hero.Pudge ? "BUTCHER'S CACHE" : "STONE CACHE", 50);
        ShowEvent("DOTA EVENT", hero == Hero.Pudge ? "Pudge's cache: sell it for 50 gold." : "Tiny's cache: sell it for 50 gold.", 4f);
    }

    private void StartAnomaly()
    {
        cameraShake = 0.9f;
        ShowEvent("MOUNTAIN ANOMALY", "The mountain is unstable. Watch your footing.", 5f);
        if (tinyEffect != null) Destroy(tinyEffect);
        tinyEffect = CreateTremorEffect(Character.localCharacter?.Center ?? Vector3.zero, 2.2f);
        Destroy(tinyEffect, 2f);
        tinyEffect = null;
    }

    private void TryDetectNearbyChest()
    {
        Character? c = Character.localCharacter;
        if (c == null) return;
        Collider[] hits = Physics.OverlapSphere(c.Center, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            string n = hits[i].name.ToLowerInvariant();
            if (!n.Contains("chest") && !n.Contains("loot") && !n.Contains("container")) continue;
            if (n.Contains("dotapeak_sold")) continue;
            Vector3 p = hits[i].transform.position;
            if (!HasLootAt(p)) CreateLootPile(p, "PEAK LOOT", UnityEngine.Random.Range(5, 21));
        }
    }

    private void CreateLootPile(Vector3 position, string name, int value)
    {
        if (HasLootAt(position)) return;
        GameObject root = new GameObject("DotaPeak_Loot");
        root.transform.position = position;
        GameObject gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gem.name = "DotaPeak_LootItem";
        gem.transform.SetParent(root.transform);
        gem.transform.localPosition = Vector3.up * 0.35f;
        gem.transform.localScale = new Vector3(0.35f, 0.22f, 0.35f);
        gem.GetComponent<Collider>().enabled = false;
        Renderer renderer = gem.GetComponent<Renderer>();
        renderer.material = NewMaterial(new Color(0.95f, 0.65f, 0.12f, 1f), false);
        lootPiles.Add(new LootPile(root, name, value));
    }

    private bool HasLootAt(Vector3 p)
    {
        for (int i = lootPiles.Count - 1; i >= 0; i--)
        {
            if (lootPiles[i].Root == null) { lootPiles.RemoveAt(i); continue; }
            if (Vector3.Distance(lootPiles[i].Root.transform.position, p) < 0.75f) return true;
        }
        return false;
    }

    private void SellNearestLoot()
    {
        Character? c = Character.localCharacter;
        if (c == null) return;

        int best = -1;
        float distance = 2.5f;
        for (int i = 0; i < lootPiles.Count; i++)
        {
            if (lootPiles[i].Root == null) continue;
            float d = Vector3.Distance(c.Center, lootPiles[i].Root.transform.position);
            if (d < distance) { distance = d; best = i; }
        }

        if (best < 0)
        {
            ShowEvent("DOTA SHOP", "No loot nearby to sell.", 2.5f);
            return;
        }

        LootPile loot = lootPiles[best];
        savedGold.Value += loot.Value;
        Destroy(loot.Root);
        lootPiles.RemoveAt(best);
        ShowEvent("ITEM SOLD", $"{loot.Name}  +{loot.Value} GOLD", 3f);
    }

    private void HandleHeroMenu()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectHero(Hero.Pudge);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectHero(Hero.Tiny);
    }

    private void SelectHero(Hero selected)
    {
        hero = selected;
        abilityCooldown = 0f;
        heroMenu = false;
        heroSelectionRequired = false;
        if (hookEffect != null) { Destroy(hookEffect); hookEffect = null; }
        if (tinyEffect != null) { Destroy(tinyEffect); tinyEffect = null; }
        ShowEvent("HERO SELECTED", hero == Hero.Pudge ? "PUDGE — MEAT HOOK" : "TINY — TOSS", 3f);
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
        ShowEvent("ITEM PURCHASED", "BOOTS OF SPEED", 2.5f);
    }

    private void BuyBlink()
    {
        if (savedBlink.Value || !SpendGold(BlinkCost)) return;
        savedBlink.Value = true;
        ShowEvent("ITEM PURCHASED", "BLINK", 2.5f);
    }

    private bool SpendGold(int amount)
    {
        if (savedGold.Value < amount)
        {
            ShowEvent("NOT ENOUGH GOLD", $"Need {amount} GOLD", 2.5f);
            return false;
        }
        savedGold.Value -= amount;
        return true;
    }

    private void UsePudgeHook()
    {
        Character? c = Character.localCharacter;
        Camera? cam = Camera.main;
        if (c == null || cam == null) return;

        Vector3 start = cam.transform.position + cam.transform.forward * 0.75f;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, HookRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            abilityCooldown = HookCooldown;
            SpawnHookProjectile(start, start + cam.transform.forward * HookRange, false);
            ShowEvent("MEAT HOOK", "MISS", 1.2f);
            return;
        }

        abilityCooldown = HookCooldown;
        SpawnHookProjectile(start, hit.point, true);
        StartCoroutine(PullCharacter(c, hit.point));
    }

    private System.Collections.IEnumerator PullCharacter(Character c, Vector3 target)
    {
        pullTime = 0.32f;
        cameraShake = 0.25f;
        Vector3 start = c.Center;
        Vector3 direction = (target - start).normalized;
        Vector3 destination = target - direction * 1.35f;
        float duration = Mathf.Clamp(Vector3.Distance(start, destination) / 18f, 0.18f, 0.75f);
        float t = 0f;

        while (t < duration && c != null)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            p = p * p * (3f - 2f * p);
            WarpCharacter(c, Vector3.Lerp(start, destination, p));
            yield return null;
        }

        if (c != null) WarpCharacter(c, destination);
        cameraShake = 0.18f;
    }

    private void SpawnHookProjectile(Vector3 start, Vector3 target, bool hit)
    {
        if (hookEffect != null) Destroy(hookEffect);
        hookEffect = new GameObject("DotaPeak_MeatHook");
        HookProjectile projectile = hookEffect.AddComponent<HookProjectile>();
        projectile.Initialize(start, target, hit);
    }

    private void UpdateCameraShake()
    {
        Camera? cam = Camera.main;
        if (cam == null) return;

        cam.transform.localPosition -= cameraShakeOffset;
        cameraShakeOffset = Vector3.zero;

        if (cameraShake <= 0f) return;

        float strength = cameraShake * 0.045f;
        cameraShakeOffset = new Vector3(
            UnityEngine.Random.Range(-strength, strength),
            UnityEngine.Random.Range(-strength, strength),
            0f);
        cam.transform.localPosition += cameraShakeOffset;
    }

    private void UseTinyToss()
    {
        Character? c = Character.localCharacter;
        Camera? cam = Camera.main;
        if (c == null || cam == null) return;

        Vector3 origin = c.Center;
        Vector3 forward = cam.transform.forward;
        Vector3 target = origin + forward * TinyRange + Vector3.up * 1.2f;

        if (Physics.Raycast(origin + Vector3.up * 0.2f, forward, out RaycastHit hit, TinyRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            target = hit.point - forward * 1.1f + Vector3.up * 1.8f;

        abilityCooldown = TinyCooldown;
        cameraShake = 0.5f;
        if (tinyEffect != null) Destroy(tinyEffect);
        tinyEffect = CreateTremorEffect(origin, 2.4f);
        Destroy(tinyEffect, 1.2f);
        tinyEffect = null;

        StartCoroutine(TossCharacter(c, target));
        ShowEvent("TOSS", "THE MOUNTAIN SHAKES", 1.8f);
    }

    private System.Collections.IEnumerator TossCharacter(Character c, Vector3 target)
    {
        Vector3 start = c.Center;
        float duration = 0.42f;
        float t = 0f;
        while (t < duration && c != null)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float eased = p * p * (3f - 2f * p);
            Vector3 pos = Vector3.Lerp(start, target, eased);
            pos += Vector3.up * Mathf.Sin(p * Mathf.PI) * 1.4f;
            WarpCharacter(c, pos);
            yield return null;
        }
        if (c != null) WarpCharacter(c, target);
        cameraShake = 0.2f;
    }

    private GameObject CreateTremorEffect(Vector3 position, float radius)
    {
        GameObject root = new GameObject("DotaPeak_EarthTremor");
        root.transform.position = position;
        TremorVfx vfx = root.AddComponent<TremorVfx>();
        vfx.Initialize(radius);
        return root;
    }

    private void UseBlink()
    {
        if (!savedBlink.Value || blinkCooldown > 0f) return;
        Character? c = Character.localCharacter;
        Camera? cam = Camera.main;
        if (c == null || cam == null) return;

        Vector3 origin = c.Center;
        Vector3 destination = origin + cam.transform.forward * 10f;
        if (Physics.Raycast(origin + Vector3.up * 0.5f, cam.transform.forward, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            destination = hit.point - cam.transform.forward * 1.5f;

        WarpCharacter(c, destination);
        blinkCooldown = 12f;
        cameraShake = 0.12f;
        ShowEvent("BLINK", "10m TRAVERSAL", 1.5f);
    }

    private static void WarpCharacter(Character c, Vector3 destination) => c.WarpPlayer(destination, false);

    private void ApplyMovementModifier()
    {
        Character? c = Character.localCharacter;
        if (c == null || c.refs == null || c.refs.movement == null)
        {
            movementBaselineCaptured = false;
            return;
        }

        if (!movementBaselineCaptured)
        {
            baseMovementModifier = c.refs.movement.movementModifier;
            movementBaselineCaptured = true;
        }

        c.refs.movement.movementModifier = baseMovementModifier * (savedBoots.Value ? BootsMultiplier : 1f);
    }

    private void ShowEvent(string title, string subtitle, float seconds)
    {
        eventTitle = title;
        eventSubtitle = subtitle;
        eventMessageTimer = seconds;
        Log.LogInfo($"DotaPeak Event: {title} / {subtitle}");
    }

    private void OnGUI()
    {
        if (!enabledConfig.Value) return;

        if (heroSelectionRequired)
        {
            DrawHeroSelection();
            return;
        }

        // No top HUD. Gameplay is kept clean; the only persistent control is the shop in the lower-left.
        float shopButtonWidth = 190f;
        float shopButtonHeight = 42f;
        Rect shopToggle = new Rect(24f, Screen.height - shopButtonHeight - 24f, shopButtonWidth, shopButtonHeight);
        if (!shopMenu)
        {
            if (GUI.Button(shopToggle, "B  DOTA SHOP", UiSkin.ShopButton))
                shopMenu = true;
        }
        else
        {
            DrawShopPanel();
        }

        if (eventMessageTimer > 0f)
            DrawToast();

        if (cameraShake > 0f)
            GUI.Label(new Rect(Screen.width * 0.5f - 110f, Screen.height - 72f, 220f, 30f), "MOUNTAIN SHAKES", UiSkin.Alert);
    }

    private void DrawHeroSelection()
    {
        float w = 600f;
        float h = 330f;
        float x = (Screen.width - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        GUI.Box(new Rect(x, y, w, h), GUIContent.none, UiSkin.Panel);
        GUI.Label(new Rect(x + 28f, y + 20f, w - 56f, 34f), "CHOOSE YOUR HERO", UiSkin.Title);
        GUI.Label(new Rect(x + 28f, y + 54f, w - 56f, 24f), "Your choice starts the Dota × PEAK run.", UiSkin.Muted);

        DrawHeroButton(new Rect(x + 28f, y + 94f, 258f, 185f), "PUDGE", "Z  MEAT HOOK", "Mobility / rescue", Hero.Pudge);
        DrawHeroButton(new Rect(x + 314f, y + 94f, 258f, 185f), "TINY", "Z  TOSS", "Traversal / impact", Hero.Tiny);

        GUI.Label(new Rect(x + 28f, y + 288f, w - 56f, 24f), "[1] PUDGE     [2] TINY", UiSkin.Muted);
    }

    private void DrawHeroButton(Rect rect, string name, string ability, string role, Hero selected)
    {
        if (GUI.Button(rect, GUIContent.none, selected == hero ? UiSkin.SelectedCardButton : UiSkin.CardButton))
            SelectHero(selected);

        GUI.Label(new Rect(rect.x + 16f, rect.y + 14f, rect.width - 32f, 32f), name, UiSkin.Hero);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 58f, rect.width - 32f, 28f), ability, UiSkin.Gold);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 96f, rect.width - 32f, 46f), role, UiSkin.Body);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 145f, rect.width - 32f, 26f), selected == hero ? "SELECTED" : "CLICK TO SELECT", UiSkin.Muted);
    }

    private void DrawShopPanel()
    {
        float w = 300f;
        float h = 250f;
        float x = 24f;
        float y = Screen.height - h - 24f;

        GUI.Box(new Rect(x, y, w, h), GUIContent.none, UiSkin.Panel);
        GUI.Label(new Rect(x + 18f, y + 14f, w - 36f, 30f), "DOTA SHOP", UiSkin.Title);
        GUI.Label(new Rect(x + 190f, y + 18f, 90f, 24f), $"{savedGold.Value} G", UiSkin.Gold);

        DrawShopButton(new Rect(x + 18f, y + 55f, w - 36f, 72f), "BOOTS OF SPEED", "25 G  •  Movement +12%", savedBoots.Value, BuyBoots);
        DrawShopButton(new Rect(x + 18f, y + 135f, w - 36f, 72f), "BLINK", "50 G  •  10m traversal / 12s", savedBlink.Value, BuyBlink);

        if (GUI.Button(new Rect(x + 18f, y + 213f, 125f, 26f), "CLOSE", UiSkin.SmallButton))
            shopMenu = false;

        GUI.Label(new Rect(x + 150f, y + 214f, 132f, 24f), $"{sellKey.Value}  SELL LOOT", UiSkin.Muted);
    }

    private void DrawShopButton(Rect rect, string name, string info, bool owned, Action purchase)
    {
        if (!owned && GUI.Button(rect, GUIContent.none, UiSkin.CardButton))
            purchase();

        GUI.Label(new Rect(rect.x + 12f, rect.y + 9f, rect.width - 24f, 24f), name, UiSkin.Hero);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 35f, rect.width - 24f, 24f), owned ? "OWNED — RESETS NEXT RUN" : info, owned ? UiSkin.Gold : UiSkin.Body);
    }

    private void DrawToast()
    {
        float w = 440f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height - 138f;
        GUI.Box(new Rect(x, y, w, 68f), GUIContent.none, UiSkin.Toast);
        GUI.Label(new Rect(x + 18f, y + 9f, w - 36f, 22f), eventTitle, UiSkin.Gold);
        GUI.Label(new Rect(x + 18f, y + 32f, w - 36f, 26f), eventSubtitle, UiSkin.Body);
    }

    private static string CooldownText(float value) => value <= 0f ? "READY" : $"{value:0.0}s";

    private static Material NewMaterial(Color color, bool emissive)
    {
        Shader shader = Shader.Find(emissive ? "Standard" : "Sprites/Default");
        Material m = new Material(shader);
        m.color = color;
        if (emissive && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color);
        }
        return m;
    }

    private sealed class LootPile
    {
        public GameObject Root { get; }
        public string Name { get; }
        public int Value { get; }

        public LootPile(GameObject root, string name, int value)
        {
            Root = root;
            Name = name;
            Value = value;
        }
    }

    private sealed class HookProjectile : MonoBehaviour
    {
        private Vector3 start;
        private Vector3 target;
        private float time;
        private float duration;
        private bool hit;
        private LineRenderer chain = null!;
        private GameObject head = null!;
        private const int Segments = 12;

        public void Initialize(Vector3 from, Vector3 to, bool didHit)
        {
            start = from;
            target = to;
            hit = didHit;
            duration = Mathf.Clamp(Vector3.Distance(from, to) / 32f, 0.12f, 0.52f);

            chain = gameObject.AddComponent<LineRenderer>();
            chain.positionCount = Segments;
            chain.material = NewMaterial(new Color(0.18f, 0.15f, 0.12f, 1f), false);
            chain.startWidth = 0.075f;
            chain.endWidth = 0.09f;

            head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "DotaPeak_MeatHook_Head";
            head.transform.localScale = Vector3.one * 0.24f;
            head.GetComponent<Collider>().enabled = false;
            head.GetComponent<Renderer>().material = NewMaterial(new Color(0.5f, 0.3f, 0.18f, 1f), true);
        }

        private void Update()
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            Vector3 pos = Vector3.Lerp(start, target, t);
            head.transform.position = pos;
            head.transform.Rotate(720f * Time.deltaTime, 520f * Time.deltaTime, 0f, Space.Self);

            for (int i = 0; i < Segments; i++)
            {
                float p = i / (float)(Segments - 1);
                Vector3 point = Vector3.Lerp(start, pos, p);
                float sag = Mathf.Sin(p * Mathf.PI) * Mathf.Min(0.35f, Vector3.Distance(start, pos) * 0.018f);
                point += Vector3.down * sag;
                chain.SetPosition(i, point);
            }

            if (t >= 1f)
            {
                if (hit) SpawnImpact(target);
                Destroy(head);
                Destroy(gameObject, 0.08f);
                enabled = false;
            }
        }

        private static void SpawnImpact(Vector3 position)
        {
            GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = "DotaPeak_HookImpact";
            impact.transform.position = position;
            impact.transform.localScale = Vector3.one * 0.45f;
            impact.GetComponent<Collider>().enabled = false;
            impact.GetComponent<Renderer>().material = NewMaterial(new Color(0.95f, 0.68f, 0.2f, 0.75f), true);
            Destroy(impact, 0.22f);
        }
    }

    private sealed class TremorVfx : MonoBehaviour
    {
        private LineRenderer[] rings = Array.Empty<LineRenderer>();
        private float radius;
        private float age;

        public void Initialize(float r)
        {
            radius = r;
            rings = new LineRenderer[3];
            for (int i = 0; i < rings.Length; i++)
            {
                LineRenderer ring = gameObject.AddComponent<LineRenderer>();
                ring.positionCount = 28;
                ring.loop = true;
                ring.useWorldSpace = false;
                ring.material = NewMaterial(new Color(0.48f, 0.32f, 0.18f, 0.8f - i * 0.2f), false);
                ring.startWidth = 0.09f;
                ring.endWidth = 0.015f;
                rings[i] = ring;
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            float scale = Mathf.Clamp01(age / 0.8f);
            for (int r = 0; r < rings.Length; r++)
            {
                float rr = radius * scale * (0.65f + r * 0.25f);
                for (int i = 0; i < 28; i++)
                {
                    float a = i / 28f * Mathf.PI * 2f;
                    float wave = Mathf.Sin(a * 5f + age * 20f) * 0.05f;
                    rings[r].SetPosition(i, new Vector3(Mathf.Cos(a) * (rr + wave), 0.03f + r * 0.025f, Mathf.Sin(a) * (rr + wave)));
                }
            }
            transform.position += Vector3.down * Time.deltaTime * 0.04f;
        }
    }

    private static class UiSkin
    {
        public static readonly GUIStyle Panel = Make(new Color(0.035f, 0.045f, 0.06f, 0.96f), new Color(0.75f, 0.62f, 0.28f, 1f));
        public static readonly GUIStyle Card = Make(new Color(0.055f, 0.065f, 0.085f, 0.98f), new Color(0.18f, 0.2f, 0.24f, 1f));
        public static readonly GUIStyle SelectedCard = Make(new Color(0.09f, 0.075f, 0.045f, 0.99f), new Color(0.86f, 0.68f, 0.25f, 1f));
        public static readonly GUIStyle CardButton = Make(new Color(0.055f, 0.065f, 0.085f, 0.98f), new Color(0.28f, 0.31f, 0.36f, 1f));
        public static readonly GUIStyle SelectedCardButton = Make(new Color(0.09f, 0.075f, 0.045f, 0.99f), new Color(0.86f, 0.68f, 0.25f, 1f));
        public static readonly GUIStyle ShopButton = Make(new Color(0.07f, 0.065f, 0.045f, 0.98f), new Color(0.86f, 0.68f, 0.25f, 1f));
        public static readonly GUIStyle SmallButton = Make(new Color(0.055f, 0.065f, 0.085f, 0.98f), new Color(0.28f, 0.31f, 0.36f, 1f));
        public static readonly GUIStyle Toast = Make(new Color(0.035f, 0.045f, 0.06f, 0.98f), new Color(0.86f, 0.68f, 0.25f, 1f));
        public static readonly GUIStyle Title = Text(22, FontStyle.Bold, new Color(0.92f, 0.86f, 0.7f, 1f));
        public static readonly GUIStyle Hero = Text(18, FontStyle.Bold, new Color(0.88f, 0.88f, 0.9f, 1f));
        public static readonly GUIStyle Gold = Text(17, FontStyle.Bold, new Color(1f, 0.78f, 0.25f, 1f));
        public static readonly GUIStyle Body = Text(15, FontStyle.Normal, new Color(0.78f, 0.8f, 0.84f, 1f));
        public static readonly GUIStyle Muted = Text(12, FontStyle.Normal, new Color(0.52f, 0.56f, 0.62f, 1f));
        public static readonly GUIStyle Alert = Text(18, FontStyle.Bold, new Color(1f, 0.55f, 0.25f, 1f));

        private static GUIStyle Make(Color background, Color border)
        {
            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, background);
            bg.Apply();
            Texture2D br = new Texture2D(1, 1);
            br.SetPixel(0, 0, border);
            br.Apply();
            GUIStyle s = new GUIStyle(GUI.skin.box);
            s.normal.background = bg;
            s.border = new RectOffset(2, 2, 2, 2);
            return s;
        }

        private static GUIStyle Text(int size, FontStyle style, Color color)
        {
            return new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, normal = { textColor = color } };
        }
    }
}
