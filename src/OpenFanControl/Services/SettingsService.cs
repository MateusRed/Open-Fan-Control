using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFanControl.Models;

namespace OpenFanControl.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %AppData%\OpenFanControl.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dir;
    private readonly string _path;

    public SettingsService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenFanControl");
        _path = Path.Combine(_dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults rather than crashing.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Best-effort persistence; ignore disk failures.
        }
    }
}
