using BepInEx;
using BepInEx.Logging;

namespace DotaPeak;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "sanchezavr123-lgtm.DotaPeak";
    public const string PluginName = "DotaPeak";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"{PluginName} loaded. PEAK host confirmed; prototype systems awaiting verified game APIs.");
    }
}