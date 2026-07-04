using System.Collections.Generic;
using System.Linq;

namespace OpenFanControl.Models;

public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>Root persisted application state.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    /// <summary>How often to poll sensors, in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 1500;

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Register a logon task so the app starts with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Saved control profiles. The app always operates on <see cref="ActiveProfile"/>.</summary>
    public List<Profile> Profiles { get; set; } = new();

    public string ActiveProfileName { get; set; } = "Default";

    // ---- Legacy top-level config (pre-profiles). Migrated into a "Default" profile on load. ----
    public List<FanConfig> FanConfigs { get; set; } = new();
    public List<CustomSensorConfig> CustomSensors { get; set; } = new();

    /// <summary>
    /// Returns the active profile, migrating legacy top-level config into a "Default" profile the
    /// first time. Always returns a valid profile (creating "Default" if none exist).
    /// </summary>
    public Profile ActiveProfile()
    {
        if (Profiles.Count == 0)
        {
            Profiles.Add(new Profile
            {
                Name = "Default",
                FanConfigs = FanConfigs ?? new List<FanConfig>(),
                CustomSensors = CustomSensors ?? new List<CustomSensorConfig>()
            });
            FanConfigs = new List<FanConfig>();
            CustomSensors = new List<CustomSensorConfig>();
            ActiveProfileName = "Default";
        }

        var profile = Profiles.FirstOrDefault(p => p.Name == ActiveProfileName) ?? Profiles[0];
        ActiveProfileName = profile.Name;
        return profile;
    }
}
