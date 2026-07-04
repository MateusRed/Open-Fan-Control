using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenFanControl.ViewModels;

/// <summary>An editable point on a fan curve (bound to the <c>CurveEditor</c> control).</summary>
public sealed partial class CurvePointViewModel : ObservableObject
{
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private double _percent;

    public CurvePointViewModel(double temperature, double percent)
    {
        _temperature = temperature;
        _percent = percent;
    }
}
