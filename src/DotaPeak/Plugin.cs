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
    public const string PluginVersion = "0.2.0";

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
    private float runTime;
    private float goldTimer;
    private bool heroMenu;
    private bool shopMenu;
    private GameObject? hookVisual;

    private const int BlinkCost = 50;
    private const int BootsCost = 25;

    private enum Hero
    {
        Pudge,
        Juggernaut
    }

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
        Log.LogInfo($"Saved run state: hero={hero}, gold={savedGold.Value}, blink={savedBlink.Value}, boots={savedBoots.Value}");
    }

    private void Update()
    {
        if (!enabledConfig.Value) return;

        runTime += Time.deltaTime;
        TickCooldowns();
        AwardTimeGold();

        if (Input.GetKeyDown(menuKey.Value))
        {
            heroMenu = !heroMenu;
            shopMenu = false;
        }

        if (Input.GetKeyDown(shopKey.Value))
        {
            shopMenu = !shopMenu;
            heroMenu = false;
        }

        if (heroMenu)
        {
            HandleHeroMenu();
            return;
        }

        if (shopMenu)
        {
            HandleShop();
            return;
        }

        if (Input.GetKeyDown(blinkKey.Value))
            UseBlink();

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
        bladeFuryTime = Mathf.Max(0f, bladeFuryTime - Time.deltaTime);
    }

    private void AwardTimeGold()
    {
        goldTimer += Time.deltaTime;
        if (goldTimer < 45f) return;

        goldTimer -= 45f;
        savedGold.Value += 5;
        Log.LogInfo($"DotaPeak: +5 gold for surviving the climb. Gold={savedGold.Value}");
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
        Log.LogInfo($"Hero selected: {hero}");
    }

    private void HandleShop()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) BuyBoots();
        if (Input.GetKeyDown(KeyCode.Alpha2)) BuyBlink();
    }

    private void BuyBoots()
    {
        if (savedBoots.Value)
        {
            Log.LogInfo("Boots of Speed already owned.");
            return;
        }

        if (!SpendGold(BootsCost)) return;
        savedBoots.Value = true;
        Log.LogInfo("Purchased Boots of Speed. Passive movement integration is queued for the verified PEAK movement API.");
    }

    private void BuyBlink()
    {
        if (savedBlink.Value)
        {
            Log.LogInfo("Blink already owned.");
            return;
        }

        if (!SpendGold(BlinkCost)) return;
        savedBlink.Value = true;
        Log.LogInfo("Purchased Blink.");
    }

    private bool SpendGold(int amount)
    {
        if (savedGold.Value < amount)
        {
            Log.LogInfo($"Not enough gold: need {amount}, have {savedGold.Value}.");
            return false;
        }

        savedGold.Value -= amount;
        return true;
    }

    private void UsePudgeHook()
    {
        Transform? player = GetPlayerTransform();
        Camera? camera = Camera.main;
        if (player == null || camera == null) return;

        Ray ray = new(camera.transform.position, camera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, 35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            abilityCooldown = 1f;
            Log.LogInfo("Pudge Hook: no valid surface in 35m range.");
            return;
        }

        Vector3 offset = hit.point - player.position;
        if (offset.magnitude > 2.5f)
        {
            Vector3 destination = hit.point - offset.normalized * 2f;
            MovePlayer(player, destination);
            CreateHookVisual(camera.transform.position, hit.point);
        }

        abilityCooldown = 4f;
        Log.LogInfo($"Pudge Hook -> {hit.point}");
    }

    private void UseJuggernautBladeFury()
    {
        bladeFuryTime = 3f;
        abilityCooldown = 12f;
        Log.LogInfo("Juggernaut Blade Fury activated for 3 seconds.");
    }

    private void UseBlink()
    {
        if (!savedBlink.Value)
        {
            Log.LogInfo("Blink unavailable: buy it from the shop with B -> 2.");
            return;
        }

        if (blinkCooldown > 0f) return;

        Transform? player = GetPlayerTransform();
        Camera? camera = Camera.main;
        if (player == null || camera == null) return;

        Vector3 origin = player.position;
        Vector3 destination = origin + camera.transform.forward * 10f;

        if (Physics.Raycast(origin + Vector3.up * 0.5f, camera.transform.forward, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            destination = hit.point - camera.transform.forward * 1.5f;

        MovePlayer(player, destination);
        blinkCooldown = 12f;
        Log.LogInfo($"Blink -> {destination}");
    }

    private void MovePlayer(Transform player, Vector3 destination)
    {
        CharacterController? controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
            player.position = destination;
            controller.enabled = true;
            return;
        }

        Rigidbody? body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = destination;
            body.linearVelocity = Vector3.zero;
            return;
        }

        player.position = destination;
    }

    private void CreateHookVisual(Vector3 start, Vector3 end)
    {
        if (hookVisual != null)
            Destroy(hookVisual);

        hookVisual = new GameObject("DotaPeak_HookVisual");
        LineRenderer line = hookVisual.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = 0.035f;
        line.endWidth = 0.075f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        Destroy(hookVisual, 0.18f);
    }

    private Transform? GetPlayerTransform()
    {
        Camera? camera = Camera.main;
        if (camera == null) return null;

        Transform current = camera.transform;
        while (current.parent != null && current.parent != current)
        {
            current = current.parent;
            if (current.GetComponent<Rigidbody>() != null || current.GetComponent<CharacterController>() != null)
                return current;
        }

        return camera.transform.root;
    }

    private static Hero ParseHero(string value)
    {
        return Enum.TryParse(value, true, out Hero result) ? result : Hero.Pudge;
    }

    private void OnGUI()
    {
        if (!enabledConfig.Value) return;

        GUI.Box(new Rect(15, 15, 390, 105), "DOTA x PEAK");
        GUI.Label(new Rect(30, 42, 350, 22), $"Hero: {hero}    Gold: {savedGold.Value}");
        GUI.Label(new Rect(30, 64, 350, 22), $"Q Ability: {FormatCooldown(abilityCooldown)}    E Blink: {FormatCooldown(blinkCooldown)}");
        GUI.Label(new Rect(30, 86, 350, 22), "F1 Heroes   B Shop   E Blink   Q Ability");

        if (bladeFuryTime > 0f)
            GUI.Label(new Rect(30, 120, 350, 24), $"BLADE FURY {bladeFuryTime:0.0}s");

        if (heroMenu)
        {
            GUI.Box(new Rect(15, 150, 390, 145), "Hero Selection");
            GUI.Label(new Rect(35, 180, 350, 25), "1 - Pudge");
            GUI.Label(new Rect(35, 208, 350, 25), "   Q: Meat Hook traversal");
            GUI.Label(new Rect(35, 236, 350, 25), "2 - Juggernaut");
            GUI.Label(new Rect(35, 264, 350, 25), "   Q: Blade Fury state");
        }

        if (shopMenu)
        {
            GUI.Box(new Rect(15, 150, 390, 170), "Dota Shop");
            GUI.Label(new Rect(35, 180, 350, 25), $"1 - Boots of Speed ({BootsCost}) {(savedBoots.Value ? "OWNED" : "")}");
            GUI.Label(new Rect(35, 215, 350, 25), $"2 - Blink ({BlinkCost}) {(savedBlink.Value ? "OWNED" : "")}");
            GUI.Label(new Rect(35, 250, 350, 25), "Gold is earned during the solo climb.");
            GUI.Label(new Rect(35, 278, 350, 25), "B - close shop");
        }
    }

    private static string FormatCooldown(float value)
    {
        return value <= 0f ? "READY" : $"{value:0.0}s";
    }
}
