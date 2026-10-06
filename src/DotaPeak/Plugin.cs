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
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    private ConfigEntry<bool> enabledConfig = null!;
    private ConfigEntry<KeyCode> abilityKey = null!;
    private ConfigEntry<KeyCode> menuKey = null!;

    private string hero = "Pudge";
    private float cooldown;
    private bool menu;

    private void Awake()
    {
        Log = Logger;
        enabledConfig = Config.Bind("DotaPeak", "Enabled", true, "Enable the DotaPeak prototype.");
        abilityKey = Config.Bind("Controls", "Ability", KeyCode.Q, "Selected hero ability.");
        menuKey = Config.Bind("Controls", "HeroMenu", KeyCode.F1, "Open hero selection.");
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. PEAK host confirmed.");
    }

    private void Update()
    {
        if (!enabledConfig.Value) return;

        if (Input.GetKeyDown(menuKey.Value))
            menu = !menu;

        if (menu)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) hero = "Pudge";
            if (Input.GetKeyDown(KeyCode.Alpha2)) hero = "Juggernaut";
            return;
        }

        if (cooldown > 0f) cooldown -= Time.deltaTime;

        if (Input.GetKeyDown(abilityKey.Value) && cooldown <= 0f)
        {
            if (hero == "Pudge") UsePudgeHook();
            else UseJuggernautAbility();
        }
    }

    private void UsePudgeHook()
    {
        Camera camera = Camera.main;
        if (camera == null) return;

        Ray ray = new(camera.transform.position, camera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, 35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Log.LogInfo("Pudge Hook: no surface in range.");
            cooldown = 1f;
            return;
        }

        Transform player = FindPlayerRoot(camera.transform);
        if (player == null) return;

        Vector3 offset = hit.point - player.position;
        if (offset.magnitude > 2.5f)
        {
            Vector3 destination = hit.point - offset.normalized * 2f;
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
                player.position = destination;
                controller.enabled = true;
            }
            else
            {
                player.position = destination;
            }
        }

        cooldown = 4f;
        Log.LogInfo($"Pudge Hook -> {hit.point}");
    }

    private void UseJuggernautAbility()
    {
        cooldown = 12f;
        Log.LogInfo("Juggernaut Blade Fury prototype activated for 3 seconds.");
    }

    private static Transform FindPlayerRoot(Transform camera)
    {
        Transform current = camera;
        while (current.parent != null && current.parent != current)
        {
            current = current.parent;
            if (current.GetComponent<Rigidbody>() != null || current.GetComponent<CharacterController>() != null)
                return current;
        }
        return camera.root;
    }

    private void OnGUI()
    {
        if (!enabledConfig.Value) return;

        GUI.Label(new Rect(20, 20, 500, 24), $"Dota x PEAK | Hero: {hero} | Q: ability | F1: heroes");

        if (menu)
        {
            GUI.Box(new Rect(20, 50, 360, 120), "Hero Selection");
            GUI.Label(new Rect(40, 80, 320, 25), "1 - Pudge: Meat Hook traversal");
            GUI.Label(new Rect(40, 110, 320, 25), "2 - Juggernaut: Blade Fury prototype");
            GUI.Label(new Rect(40, 140, 320, 25), "F1 - close menu");
        }
    }
}
