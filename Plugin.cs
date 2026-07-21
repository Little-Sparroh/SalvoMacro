using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[MycoMod(null, ModFlags.IsClientSide)]
public class SparrohPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "sparroh.salvomacro";
    public const string PluginName = "SalvoMacro";
    public const string PluginVersion = "1.2.0";

    internal static new ManualLogSource Logger;

    public enum ActivationMode
    {
        None,
        Toggle,
        Always
    }

    public static ConfigEntry<ActivationMode> salvoMode;
    public static ConfigEntry<bool> useZeroLockRelease;
    public static ConfigEntry<bool> suppressSalvoModelAlways;

    internal static SparrohPlugin Instance { get; private set; }

    private FileSystemWatcher configWatcher;
    private volatile bool configReloadPending;
    private int lastConfigChangeTick;
    private const int ConfigReloadDebounceMs = 250;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        salvoMode = Config.Bind(
            "General",
            "SalvoActivationMode",
            ActivationMode.Toggle,
            "None: default manual firing. Toggle: Slot3 toggles auto-fire on/off. Always: auto-fire whenever charged.");

        useZeroLockRelease = Config.Bind(
            "General",
            "UseZeroLockRelease",
            true,
            "When true (recommended), auto-fire uses vanilla zero-lock release (crosshair point + spread). When false, instantly fills target locks via FindSalvoTarget before firing.");

        suppressSalvoModelAlways = Config.Bind(
            "General",
            "SuppressSalvoModelAlways",
            false,
            "Always hide the 3D salvo launcher model to save screen space (including manual aim). Auto-fire never shows the model regardless.");

        salvoMode.SettingChanged += (_, __) => WingsuitPatches.ResetToggle();

        try
        {
            WingsuitPatches.InitializeAccess();
            var harmony = new Harmony(PluginGUID);
            harmony.PatchAll(typeof(WingsuitPatches));
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded");
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to patch methods: " + ex);
        }

        SetupConfigWatcher();
    }

    private void Update()
    {
        if (!configReloadPending)
            return;

        int elapsedMs = unchecked(Environment.TickCount - lastConfigChangeTick);
        if (elapsedMs < ConfigReloadDebounceMs)
            return;

        configReloadPending = false;

        try
        {
            Config.Reload();
            WingsuitPatches.ResetToggle();
            Logger.LogInfo("Config reloaded.");
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to reload config: " + ex.Message);
        }
    }

    private void SetupConfigWatcher()
    {
        try
        {
            string configPath = Config.ConfigFilePath;
            string directory = Path.GetDirectoryName(configPath);
            string fileName = Path.GetFileName(configPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                Logger.LogWarning("Could not set up config hot-reload: invalid config path.");
                return;
            }

            configWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            configWatcher.Changed += OnConfigFileChanged;
            configWatcher.Created += OnConfigFileChanged;
            configWatcher.Renamed += OnConfigFileRenamed;
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to set up config file watcher: " + ex.Message);
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        lastConfigChangeTick = Environment.TickCount;
        configReloadPending = true;
    }

    private void OnConfigFileRenamed(object sender, RenamedEventArgs e)
    {
        string configFileName = Path.GetFileName(Config.ConfigFilePath);
        if (string.Equals(e.Name, configFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.OldName, configFileName, StringComparison.OrdinalIgnoreCase))
        {
            lastConfigChangeTick = Environment.TickCount;
            configReloadPending = true;
        }
    }

    private void OnDestroy()
    {
        if (configWatcher == null)
            return;

        configWatcher.EnableRaisingEvents = false;
        configWatcher.Changed -= OnConfigFileChanged;
        configWatcher.Created -= OnConfigFileChanged;
        configWatcher.Renamed -= OnConfigFileRenamed;
        configWatcher.Dispose();
        configWatcher = null;
    }
}

/// <summary>
/// Auto-fires rocket salvo by committing locks + spending charge directly,
/// without entering the aim state (isSalvoActive / salvoModel / salvoHUD).
/// That avoids the stuck launcher model caused by same-frame press+release simulation.
/// </summary>
[HarmonyPatch(typeof(Wingsuit))]
public static class WingsuitPatches
{
    public static bool salvoAutoEnabled;

    private static FieldInfo isSalvoActiveField;
    private static FieldInfo salvoLockOrFireTimeField;
    private static FieldInfo salvoModelField;
    private static FieldInfo salvoAnimationTimeField;
    private static PropertyInfo maxSalvoLocksProperty;
    private static MethodInfo addExtraHealingRocketMethod;
    private static MethodInfo findSalvoTargetMethod;
    private static bool accessReady;

    public static void InitializeAccess()
    {
        isSalvoActiveField = AccessTools.Field(typeof(Wingsuit), "isSalvoActive");
        salvoLockOrFireTimeField = AccessTools.Field(typeof(Wingsuit), "salvoLockOrFireTime");
        salvoModelField = AccessTools.Field(typeof(Wingsuit), "salvoModel");
        salvoAnimationTimeField = AccessTools.Field(typeof(Wingsuit), "salvoAnimationTime");
        maxSalvoLocksProperty = AccessTools.Property(typeof(Wingsuit), "MaxSalvoLocks");
        addExtraHealingRocketMethod = AccessTools.Method(typeof(Wingsuit), "AddExtraHealingRocket");
        findSalvoTargetMethod = AccessTools.Method(typeof(Wingsuit), "FindSalvoTarget");

        accessReady =
            isSalvoActiveField != null
            && salvoLockOrFireTimeField != null
            && maxSalvoLocksProperty != null;

        if (!accessReady)
            SparrohPlugin.Logger.LogError("Failed to resolve one or more Wingsuit members for SalvoMacro.");
    }

    public static void ResetToggle()
    {
        salvoAutoEnabled = false;
    }

    private static bool IsAutoFireActive()
    {
        var mode = SparrohPlugin.salvoMode.Value;
        if (mode == SparrohPlugin.ActivationMode.Always)
            return true;
        if (mode == SparrohPlugin.ActivationMode.Toggle)
            return salvoAutoEnabled;
        return false;
    }

    [HarmonyPatch("OnSalvoPressed")]
    [HarmonyPrefix]
    private static bool OnSalvoPressedPrefix(InputAction.CallbackContext context)
    {
        // Only real player input toggles; never intercept synthetic/default contexts.
        if (SparrohPlugin.salvoMode.Value != SparrohPlugin.ActivationMode.Toggle)
            return true;

        // Ignore tap interactions the same way vanilla does for press handling.
        if (context.interaction is UnityEngine.InputSystem.Interactions.TapInteraction)
            return true;

        salvoAutoEnabled = !salvoAutoEnabled;
        SparrohPlugin.Logger.LogDebug($"Salvo auto-fire {(salvoAutoEnabled ? "enabled" : "disabled")}");
        return false;
    }

    [HarmonyPatch("FixedUpdate")]
    [HarmonyPostfix]
    private static void FixedUpdatePostfix(Wingsuit __instance)
    {
        if (!accessReady || !__instance.IsOwner)
            return;

        if (!IsAutoFireActive())
            return;

        try
        {
            TryAutoFire(__instance);
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogError("Error in salvo auto-fire: " + ex);
        }
    }

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    private static void UpdatePostfix(Wingsuit __instance)
    {
        if (!SparrohPlugin.suppressSalvoModelAlways.Value || !__instance.IsOwner)
            return;

        try
        {
            SuppressSalvoModel(__instance);
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogError("Error suppressing salvo model: " + ex.Message);
        }
    }

    [HarmonyPatch("OnSalvoPressed")]
    [HarmonyPostfix]
    private static void OnSalvoPressedPostfix(Wingsuit __instance)
    {
        if (!SparrohPlugin.suppressSalvoModelAlways.Value || !__instance.IsOwner)
            return;

        try
        {
            SuppressSalvoModel(__instance);
        }
        catch (Exception ex)
        {
            SparrohPlugin.Logger.LogError("Error suppressing salvo model on press: " + ex.Message);
        }
    }

    private static void SuppressSalvoModel(Wingsuit wingsuit)
    {
        if (salvoModelField == null)
            return;

        var salvoModel = salvoModelField.GetValue(wingsuit) as Transform;
        if (salvoModel != null && salvoModel.gameObject.activeSelf)
            salvoModel.gameObject.SetActive(false);

        // Keep animation time at 0 so vanilla hide logic stays consistent if suppress is turned off mid-aim.
        if (salvoAnimationTimeField != null)
            salvoAnimationTimeField.SetValue(wingsuit, 0f);
    }

    private static void TryAutoFire(Wingsuit wingsuit)
    {
        // Never interrupt manual aiming.
        if ((bool)isSalvoActiveField.GetValue(wingsuit))
            return;

        List<ITarget> locks = wingsuit.SalvoLocks;
        List<Vector3> lockPositions = wingsuit.SalvoLockPositions;
        if (locks == null || lockPositions == null)
            return;

        // Wait for the current volley to finish firing.
        if (locks.Count > 0)
            return;

        Cooldown rocketSalvoCooldown = wingsuit.RocketSalvoCooldown;
        if (rocketSalvoCooldown == null || !rocketSalvoCooldown.data.IsCharged)
            return;

        int maxLocks = (int)maxSalvoLocksProperty.GetValue(wingsuit);
        if (maxLocks <= 0)
            return;

        ref Wingsuit.WingsuitData data = ref wingsuit.Data;

        locks.Clear();
        lockPositions.Clear();
        salvoLockOrFireTimeField.SetValue(wingsuit, -99f);

        if (SparrohPlugin.useZeroLockRelease.Value)
            BuildZeroLockRelease(wingsuit, locks, lockPositions, maxLocks, ref data);
        else
            BuildInstantTargetLocks(wingsuit, locks, maxLocks);

        // If target-lock mode found nothing, fall back to zero-lock so a charge is not wasted silently.
        if (locks.Count == 0)
            BuildZeroLockRelease(wingsuit, locks, lockPositions, maxLocks, ref data);

        if (wingsuit.UpgradeFlags.IsEnabled(WingsuitUpgradeFlags.ExtraHealRocket)
            && addExtraHealingRocketMethod != null)
        {
            addExtraHealingRocketMethod.Invoke(wingsuit, null);
        }

        rocketSalvoCooldown.data.UseCharge();

        if (data.fuelAddedOnSalvoFire > 0f)
        {
            float scale = Mathf.LerpUnclamped(
                1f,
                0.13f,
                Mathf.InverseLerp(2f, 20f, rocketSalvoCooldown.MaxCharges));
            wingsuit.AddCharge(data.fuelAddedOnSalvoFire * scale);
        }
    }

    /// <summary>
    /// Mirrors vanilla OnSalvoReleased when salvoLocks.Count == 0:
    /// raycast crosshair point, then spawn MaxSalvoLocks null-target rockets with spread.
    /// </summary>
    private static void BuildZeroLockRelease(
        Wingsuit wingsuit,
        List<ITarget> locks,
        List<Vector3> lockPositions,
        int maxLocks,
        ref Wingsuit.WingsuitData data)
    {
        Vector3 aimPoint;
        if (IBullet.RaycastForBullet(
                PlayerLook.Position,
                PlayerLook.Forward,
                data.maxSalvoLockDistance,
                10241,
                0f,
                out RaycastHit hit))
        {
            aimPoint = hit.point;
        }
        else
        {
            aimPoint = PlayerLook.Position + PlayerLook.Forward * data.maxSalvoLockDistance;
        }

        for (int i = 0; i < maxLocks; i++)
        {
            locks.Add(null);
            lockPositions.Add(aimPoint + data.salvoSpread.spreadRandom.InsideUnitSphere() * 4f);
        }
    }

    /// <summary>
    /// Optional path: call FindSalvoTarget repeatedly to fill locks instantly (no hold time).
    /// </summary>
    private static void BuildInstantTargetLocks(Wingsuit wingsuit, List<ITarget> locks, int maxLocks)
    {
        if (findSalvoTargetMethod == null)
            return;

        // FindSalvoTarget respects MaxSalvoLocks and lock intervals via salvoLockOrFireTime.
        // We already set salvoLockOrFireTime to -99f, so interval checks pass.
        for (int i = 0; i < maxLocks; i++)
        {
            bool found = (bool)findSalvoTargetMethod.Invoke(wingsuit, null);
            if (!found)
                break;

            // Allow repeated locks on the same target in one auto tick.
            salvoLockOrFireTimeField.SetValue(wingsuit, -99f);
        }

        // Pad remaining locks like vanilla non-OneLockPerTarget release when at least one lock exists.
        if (locks.Count > 0
            && locks.Count < maxLocks
            && !wingsuit.UpgradeFlags.IsEnabled(WingsuitUpgradeFlags.OneLockPerTarget))
        {
            List<Vector3> lockPositions = wingsuit.SalvoLockPositions;
            int last = locks.Count - 1;
            for (int j = locks.Count; j < maxLocks; j++)
            {
                locks.Add(locks[last]);
                lockPositions.Add(lockPositions[last]);
            }
        }
    }
}
