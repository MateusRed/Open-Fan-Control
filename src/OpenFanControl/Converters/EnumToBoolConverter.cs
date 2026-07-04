using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace OpenFanControl.Converters;

/// <summary>
/// Two-way converter used to bind a segmented control (a group of RadioButtons)
/// to a single enum property. IsChecked is true when the bound value equals the
/// ConverterParameter; checking a button writes that parameter back.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null)
        {
            try
            {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            catch
            {
                return BindingOperations.DoNothing;
            }
        }

        return BindingOperations.DoNothing;
    }
}
