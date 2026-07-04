using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using OpenFanControl.ViewModels;
using OpenFanControl.Views;

namespace OpenFanControl;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _trayIcon;

    /// <summary>Set when the user truly wants to quit (vs. closing to tray).</summary>
    public bool ForceExit { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the last window shouldn't quit — we may live in the tray.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _viewModel = new MainWindowViewModel();

            var icon = LoadIcon();
            _window = new MainWindow
            {
                DataContext = _viewModel,
                Icon = icon
            };
            _window.CloseToTrayRequested += HideToTray;
            _window.Opened += OnWindowOpened;

            SetupTray(desktop, icon);

            desktop.MainWindow = _window;
            desktop.Exit += (_, _) => _viewModel?.Dispose();

            if (!_viewModel.ShouldStartMinimized)
                _window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_viewModel is null) return;
        try
        {
            await _viewModel.InitializeAsync();
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = "Failed to open hardware: " + ex.Message;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.TrayTooltip) && _trayIcon is not null)
            _trayIcon.ToolTipText = _viewModel?.TrayTooltip;
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop, WindowIcon? icon)
    {
        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Open Fan Control",
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        var menu = new NativeMenu();

        var open = new NativeMenuItem("Open Open Fan Control");
        open.Click += (_, _) => ShowMainWindow();
        menu.Add(open);

        menu.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            ForceExit = true;
            _trayIcon!.IsVisible = false;
            desktop.Shutdown();
        };
        menu.Add(quit);

        _trayIcon.Menu = menu;
    }

    private void HideToTray(object? sender, EventArgs e) => _window?.Hide();

    private void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            var uri = new Uri("avares://OpenFanControl/Assets/logo.png");
            return new WindowIcon(AssetLoader.Open(uri));
        }
        catch
        {
            return null;
        }
    }
}
