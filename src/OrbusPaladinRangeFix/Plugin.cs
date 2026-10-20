using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace OrbusPaladinRangeFix;

[BepInPlugin("com.horizon.orbuspaladinrangefix", "OrbusPaladinRangeFix", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    internal static BepInEx.Logging.ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<int> PaladinSeconds = null!;
    internal static ConfigEntry<int> MountSeconds = null!;
    internal static ConfigEntry<float> SampleInterval = null!;

    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("Diagnostics", "Enabled", true,
            "Enable the read-only Paladin and Mount investigation hotkeys.");
        PaladinSeconds = Config.Bind("Diagnostics", "PaladinRecordingSeconds", 60,
            new ConfigDescription("Length of F8 hammer recording.", new AcceptableValueRange<int>(15, 180)));
        MountSeconds = Config.Bind("Diagnostics", "MountRecordingSeconds", 90,
            new ConfigDescription("Length of F9 mount/combat recording.", new AcceptableValueRange<int>(15, 180)));
        SampleInterval = Config.Bind("Diagnostics", "SampleIntervalSeconds", 0.10f,
            new ConfigDescription("Local player position sampling interval.", new AcceptableValueRange<float>(0.05f, 0.5f)));

        AddComponent<MechanicsDiagnostics>();
        Log.LogInfo("[PaladinRangeFix] v0.1.0 READ-ONLY diagnostic build loaded.");
        Log.LogInfo("[PaladinRangeFix] F8=Paladin 60s, F9=mount/combat 90s, F10=local hierarchy, F11=event marker.");
        Log.LogInfo("[PaladinRangeFix] No teleport changes, no mount automation, no gameplay patches in this build.");
    }
}
