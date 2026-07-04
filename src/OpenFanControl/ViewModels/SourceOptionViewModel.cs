using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenFanControl.ViewModels;

/// <summary>A selectable input sensor for a Mix custom sensor (a checkbox row).</summary>
public sealed partial class SourceOptionViewModel : ObservableObject
{
    private readonly Action _onToggle;
    private bool _suppress;

    [ObservableProperty] private bool _isSelected;

    public SourceOptionViewModel(string identifier, string name, string hardwareName, bool selected, Action onToggle)
    {
        Identifier = identifier;
        Name = name;
        HardwareName = hardwareName;
        _isSelected = selected;
        _onToggle = onToggle;
    }

    public string Identifier { get; }
    public string Name { get; }
    public string HardwareName { get; }

    /// <summary>Set the checkbox without firing the toggle callback (for programmatic sync).</summary>
    public void SetSelectedQuietly(bool value)
    {
        _suppress = true;
        IsSelected = value;
        _suppress = false;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_suppress) _onToggle();
    }
}
