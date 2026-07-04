using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenFanControl.ViewModels;

namespace OpenFanControl.Views;

public partial class MainWindow : Window
{
    /// <summary>Raised when the user clicks the red button and "minimize to tray" is enabled.</summary>
    public event EventHandler? CloseToTrayRequested;

    public MainWindow()
    {
        InitializeComponent();

        var titleBar = this.FindControl<Grid>("TitleBar");
        if (titleBar is not null)
            titleBar.PointerPressed += OnTitleBarPressed;

        WireTrafficButton("CloseButton", OnCloseClicked);
        WireTrafficButton("MinButton", (_, _) => WindowState = WindowState.Minimized);
        WireTrafficButton("ZoomButton", OnZoomClicked);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void WireTrafficButton(string name, EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
            button.Click += handler;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnZoomClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.MinimizeToTray)
            CloseToTrayRequested?.Invoke(this, EventArgs.Empty);
        else
            Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Window-manager close (Alt+F4) also honours minimize-to-tray unless we're really quitting.
        if (Application.Current is App { ForceExit: false } &&
            DataContext is MainWindowViewModel { MinimizeToTray: true })
        {
            e.Cancel = true;
            CloseToTrayRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }
}
