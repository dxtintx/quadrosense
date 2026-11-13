using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QuadroSense.Desktop.Services;

namespace QuadroSense.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly QuadroSenseApi _api = new();
    private readonly LocalApiHost _localApi = new();
    private QuadroSense.Api.Models.ControllerConfiguration? _configuration;
    private bool _refreshing;
    private bool _closed;
    private bool _closing;
    private int _pageVersion;
    private bool _serviceAvailable;
    private TuningConfig? _loadedTuning;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string _selectedPage = "Overview";
    private Diagnostics? _diagnostics;
    private bool _hasLoadedConnection;
    private DispatcherTimer? _gyroTimer;
    private readonly List<Action<ControllerMotion>> _stickUpdates = [];

    public MainWindow()
    {
        InitializeComponent();
        Title = "QuadroSense";
        if (Content is FrameworkElement content)
            content.ActualThemeChanged += (_, _) => ShowPage(_selectedPage);
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
        ShowPage("Overview");
        _refreshTimer.Tick += async (_, _) => await RefreshConnectionAsync();
        AppWindow.Closing += async (_, args) =>
        {
            if (_closed) return;
            args.Cancel = true;
            if (_closing) return;
            _closing = true;
            _refreshTimer.Stop();
            _gyroTimer?.Stop();
            try { await _localApi.DisposeAsync(); }
            finally
            {
                _api.Dispose();
                _closed = true;
                Close();
            }
        };
        _ = InitializeServiceAsync();
    }

    private async Task InitializeServiceAsync()
    {
        ConnectionLabel.Text = "Starting local serviceвЂ¦";
        try { await _localApi.EnsureRunningAsync(_api); }
        catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
        if (_closed) return;
        await RefreshConnectionAsync();
        _refreshTimer.Start();
    }

    private async Task RefreshConnectionAsync()
    {
        if (_refreshing || _closed) return;
        _refreshing = true;
        var wasAvailable = _serviceAvailable;
        try
        {
            _diagnostics = await _api.GetDiagnosticsAsync();
            _serviceAvailable = true;
            var devices = await _api.GetDevicesAsync();
            var connected = !string.IsNullOrWhiteSpace(_diagnostics?.DevicePath);
            ConnectionLabel.Text = connected ? $"DualSense В· {_diagnostics!.ConnectionType}" : devices.Count > 0 ? "Controller detected В· not connected" : "API connected - no controller";
            ConnectionIndicator.Background = Brush(connected ? "InkPrimaryBrush" : "InkSecondaryBrush");
            ReconnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            if ((!_hasLoadedConnection || !wasAvailable || _selectedPage == "Diagnostics") && (_selectedPage is "Overview" or "Diagnostics")) ShowPage(_selectedPage);
            _hasLoadedConnection = true;
        }
        catch
        {
            _serviceAvailable = false;
            _diagnostics = null;
            ConnectionLabel.Text = "Local service unavailable";
            ConnectionIndicator.Background = Brush("InkSecondaryBrush");
            ReconnectButton.Visibility = Visibility.Visible;
        }
        finally
        {
            ReconnectButton.Content = _serviceAvailable ? "Connect controller" : "Retry service";
            _refreshing = false;
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        try { await _localApi.EnsureRunningAsync(_api); if (_serviceAvailable) { await _api.ConnectAsync(); SetStatus("Controller connected", InfoBarSeverity.Success); } await RefreshConnectionAsync(); }
        catch (Exception ex) { SetStatus($"Could not connect: {ShortError(ex)}", InfoBarSeverity.Error); }
        finally { ReconnectButton.IsEnabled = true; }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag) ShowPage(tag);
    }

    private async void ShowPage(string page)
    {
        if (_closed) return;
        _gyroTimer?.Stop();
        _gyroTimer = null;
        _stickUpdates.Clear();
        _selectedPage = page;
        var version = ++_pageVersion;
        if (page is "Triggers" or "Haptics" or "Lighting")
        {
            PageHost.Content = new ProgressRing { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(24) };
            try { _configuration = await _api.GetConfigurationAsync(); }
            catch (Exception ex)
            {
                if (version != _pageVersion || _closed) return;
                var error = EmptyState("Settings could not load", ShortError(ex));
                var retry = new Button { Content = "Try again" };
                retry.Click += (_, _) => ShowPage(page);
                error.Children.Add(retry); PageHost.Content = error; return;
            }
            if (version != _pageVersion || _closed) return;
        }
        var meta = page switch
        {
            "Overview" => ("Overview", "Your controller, ready when you are."),
            "Profiles" => ("Profiles", "Keep a setup for each game and switch in one step."),
            "Triggers" => ("Adaptive triggers", "Set resistance and feedback independently for each side."),
            "Haptics" => ("Haptics & audio", "Shape rumble by hand or let system audio drive it."),
            "Mapping" => ("Button mapping", "Remaps, turbo, hold toggles and action macros."),
            "Tuning" => ("Input tuning", "Adjust stick response, touchpad behavior and gyro output."),
            "Lighting" => ("Lighting", "Set the lightbar, player indicators and dynamic effects."),
            "Integrations" => ("Game integrations", "Connect game mods to real-time controller effects."),
            "Diagnostics" => ("Diagnostics", "Inspect the live controller connection and session."),
            _ => ("Settings", "Configure the local QuadroSense service.")
        };
        PageTitle.Text = meta.Item1;
        PageSubtitle.Text = meta.Item2;
        PageHost.Content = page switch
        {
            "Overview" => BuildOverview(),
            "Profiles" => BuildProfiles(),
            "Triggers" => BuildTriggers(),
            "Haptics" => BuildHaptics(),
            "Lighting" => BuildLighting(),
            "Diagnostics" => BuildDiagnostics(),
            "Settings" => BuildSettings(),
            "Mapping" => BuildMapping(),
            "Tuning" => BuildTuning(),
            "Integrations" => BuildIntegrations(),
            _ => BuildSettings()
        };
    }

    private UIElement BuildOverview()
    {
        var root = PageStack();
        var top = new Grid { ColumnSpacing = 20 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var welcome = Panel();
        welcome.Children.Add(Kicker("ACTIVE SETUP"));
        welcome.Children.Add(new TextBlock { Text = "Controller settings\nthat follow your game.", FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 14, 0, 8) });
        welcome.Children.Add(new TextBlock { Text = "Create a profile once, then activate it whenever you play. Your settings stay on this PC.", FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Brush("InkSecondaryBrush"), MaxWidth = 520 });
        var active = Panel();
        active.Children.Add(Kicker("CURRENT PROFILE"));
        var activeName = new TextBlock { Text = "Loading profilesвЂ¦", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4), Foreground = Brush("InkPrimaryBrush") };
        active.Children.Add(activeName);
        active.Children.Add(new TextBlock { Text = "Select a profile to load its controller setup.", FontSize = 13, Foreground = Brush("InkSecondaryBrush") });
        var openProfiles = new Button { Content = "Manage profiles", Margin = new Thickness(0, 20, 0, 0) };
        openProfiles.Click += (_, _) => Navigate("Profiles"); active.Children.Add(openProfiles);
        Grid.SetColumn(active, 1); top.Children.Add(welcome); top.Children.Add(active);
        _ = LoadActiveProfileName(activeName);
        root.Children.Add(top);

        var section = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        section.Children.Add(new TextBlock { Text = "Quick controls", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush") });
        section.Children.Add(new TextBlock { Text = "Direct access to the settings you use most.", FontSize = 13, Foreground = Brush("InkSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(section);
        var quick = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < 3; i++) quick.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddQuick(quick, 0, "Triggers", "Resistance and adaptive feedback", "Tune trigger feel", "Triggers");
        AddQuick(quick, 1, "HAPTICS", "Rumble and system audio", "Set haptic response", "Haptics");
        AddQuick(quick, 2, "LIGHTING", "Color, player LEDs and effects", "Adjust lighting", "Lighting");
        root.Children.Add(quick);
        var footer = new Grid { ColumnSpacing = 24, Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.Children.Add(MetricPanel("SESSION", _diagnostics is null ? "вЂ”" : $"{_diagnostics.ReportsSent:N0}", "reports sent this session"));
        var battery = _diagnostics is null || _diagnostics.BatteryPercent < 0 ? "Not reported by API" : $"{_diagnostics.BatteryPercent}%";
        var batteryPanel = MetricPanel("BATTERY", battery, _diagnostics?.IsCharging == true ? "Charging" : "Battery telemetry"); Grid.SetColumn(batteryPanel, 1); footer.Children.Add(batteryPanel);
        root.Children.Add(footer);
        return root;
    }

    private async Task LoadActiveProfileName(TextBlock target)
    {
        try { var profiles = await _api.GetProfilesAsync(); target.Text = profiles.FirstOrDefault(p => p.IsActive)?.Name ?? "No active profile"; }
        catch { target.Text = _hasLoadedConnection ? "Service unavailable" : "Starting local service..."; }
    }

    private UIElement BuildProfiles()
    {
        var root = PageStack();
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "Saved profiles", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = new Button { Content = "New profile" }; create.Click += async (_, _) => await CreateProfileAsync();
        var refresh = new Button { Content = "Refresh" }; refresh.Click += (_, _) => ShowPage("Profiles"); controls.Children.Add(create); controls.Children.Add(refresh); Grid.SetColumn(controls, 1); header.Children.Add(controls); root.Children.Add(header);
        var list = new StackPanel { Spacing = 0 }; root.Children.Add(list);
        _ = LoadProfiles(list);
        return root;
    }

    private async Task LoadProfiles(StackPanel list)
    {
        try
        {
            list.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, Margin = new Thickness(16) });
            var profiles = await _api.GetProfilesAsync();
            list.Children.Clear();
            if (profiles.Count == 0) { list.Children.Add(EmptyState("No profiles yet", "Profiles save your trigger, haptic, lighting and button settings together.")); return; }
            foreach (var profile in profiles)
            {
                var row = new Grid { Margin = new Thickness(0, 16, 0, 16), ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock { Text = profile.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush") });
                details.Children.Add(new TextBlock { Text = profile.IsActive ? "Currently active" : "Saved profile", FontSize = 13, Foreground = Brush("InkSecondaryBrush") });
                row.Children.Add(details);
                var activate = new Button { Content = profile.IsActive ? "Active" : "Activate", IsEnabled = !profile.IsActive, MinWidth = 100 };
                activate.Click += async (_, _) => { try { await _api.ActivateProfileAsync(profile.Id); SetStatus($"{profile.Name} activated", InfoBarSeverity.Success); ShowPage("Profiles"); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                actions.Children.Add(activate);
                var save = new Button { Content = "Save current setup" };
                save.Click += async (_, _) =>
                {
                    save.IsEnabled = false;
                    try
                    {
                        var original = await _api.GetProfileAsync(profile.Id) ?? throw new InvalidOperationException("Profile no longer exists.");
                        var config = await _api.GetConfigurationAsync() ?? throw new InvalidOperationException("Controller settings are unavailable.");
                        var bindings = await _api.GetBindingsAsync();
                        await _api.SaveProfileAsync(original with
                        {
                            LeftTrigger = config.LeftTrigger, RightTrigger = config.RightTrigger,
                            Haptics = config.Haptics, Lighting = config.Lighting,
                            Bindings = bindings.Select(b => new QuadroSense.Api.Models.ButtonBinding { PhysicalButton = b.PhysicalButton, MappedTo = b.MappedTo, TurboEnabled = b.TurboEnabled, TurboRateHz = b.TurboRateHz, HoldToggle = b.HoldToggle }).ToList()
                        });
                        SetStatus($"Current trigger, haptic, lighting and mapping settings saved to {profile.Name}", InfoBarSeverity.Success);
                    }
                    catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
                    finally { save.IsEnabled = true; }
                };
                actions.Children.Add(save);
                var rename = new Button { Content = "Rename" };
                rename.Click += async (_, _) =>
                {
                    var input = new TextBox { Text = profile.Name, MaxLength = 100 };
                    var dialog = new ContentDialog { Title = "Rename profile", Content = input, PrimaryButtonText = "Save", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;
                    try { var original = await _api.GetProfileAsync(profile.Id); if (original is null) return; await _api.SaveProfileAsync(original with { Name = input.Text.Trim() }); ShowPage("Profiles"); }
                    catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
                };
                actions.Children.Add(rename);
                var automatic = new Button { Content = "Auto activation" };
                automatic.Click += async (_, _) => await EditActivationAsync(profile.Id);
                actions.Children.Add(automatic);
                var delete = new Button { Content = "Delete" };
                delete.Click += async (_, _) =>
                {
                    var dialog = new ContentDialog { Title = "Delete profile?", Content = $"Delete {profile.Name}? This removes its saved configuration.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    try { await _api.DeleteProfileAsync(profile.Id); ShowPage("Profiles"); }
                    catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
                };
                actions.Children.Add(delete);
                Grid.SetColumn(actions, 1); row.Children.Add(actions);
                list.Children.Add(row); list.Children.Add(new Border { Height = 1, Background = Brush("HairlineBrush") });
            }
        }
        catch (Exception ex) { list.Children.Clear(); var error = EmptyState("Profiles could not load", ShortError(ex)); var retry = new Button { Content = "Try again" }; retry.Click += (_, _) => ShowPage("Profiles"); error.Children.Add(retry); list.Children.Add(error); }
    }

    private async Task EditActivationAsync(string id)
    {
        try
        {
            var profile = await _api.GetProfileAsync(id) ?? throw new InvalidOperationException("Profile no longer exists.");
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(HelpText("Activate when a process starts. Enter executable names separated by commas, for example game.exe, emulator.exe. Leave blank to disable. QuadroSense must be running; detection takes up to two seconds."));
            var processes = new TextBox { Header = "Process names", Text = string.Join(", ", profile.AutoSwitchProcesses) };
            panel.Children.Add(processes);
            panel.Children.Add(HelpText("Or hold a controller combination. Select at least two buttons, for example PS and DPadUp. Clear all buttons to disable. Release before using it again. Use a different combination for each profile."));
            var choices = new StackPanel();
            var buttons = new List<CheckBox>();
            foreach (var name in new[] { "PS", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "L3", "R3", "Create", "Options", "TouchPadClick", "Mute" })
            {
                var box = new CheckBox { Content = name, IsChecked = profile.ActivationButtons.Contains(name), Margin = new Thickness(4) };
                buttons.Add(box); choices.Children.Add(box);
            }
            panel.Children.Add(choices);
            var dialog = new ContentDialog { Title = $"Auto activation В· {profile.Name}", Content = new ScrollViewer { Content = panel, MaxHeight = 460 }, PrimaryButtonText = "Save", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (buttons.Count(b => b.IsChecked == true) == 1)
                {
                    args.Cancel = true;
                    SetStatus("Select at least two buttons for a combination, or clear all buttons.", InfoBarSeverity.Warning);
                }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var latest = await _api.GetProfileAsync(id) ?? throw new InvalidOperationException("Profile no longer exists.");
            await _api.SaveProfileAsync(latest with
            {
                AutoSwitchProcesses = processes.Text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ActivationButtons = buttons.Where(b => b.IsChecked == true).Select(b => b.Content.ToString()!).ToList()
            });
            SetStatus("Profile auto activation saved", InfoBarSeverity.Success);
        }
        catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
    }

    private async Task CreateProfileAsync()
    {
        var name = new TextBox { PlaceholderText = "For example, Racing" };
        var dialog = new ContentDialog { Title = "Create profile", Content = name, PrimaryButtonText = "Create", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text)) { SetStatus("Enter a profile name", InfoBarSeverity.Warning); return; }
        try { await _api.CreateProfileAsync(Guid.NewGuid().ToString(), name.Text.Trim()); SetStatus($"Profile вЂњ{name.Text.Trim()}вЂќ created", InfoBarSeverity.Success); ShowPage("Profiles"); }
        catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
    }

    private UIElement BuildTriggers()
    {
        var root = PageStack();
        var note = InlineNote("Trigger setup", "Choose a mode and tune its start point and strength. Settings are sent to the controller when you apply them."); root.Children.Add(note);
        var columns = new Grid { ColumnSpacing = 20 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.Children.Add(TriggerEditor("Left trigger", "Left"));
        var right = TriggerEditor("Right trigger", "Right"); Grid.SetColumn(right, 1); columns.Children.Add(right); root.Children.Add(columns);
        var reset = new Button { Content = "Reset both triggers", Margin = new Thickness(0, 4, 0, 0) };
        reset.Click += async (_, _) => { try { await _api.ResetTriggersAsync(); SetStatus("Both triggers reset", InfoBarSeverity.Success); ShowPage("Triggers"); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        root.Children.Add(reset);
        return root;
    }

    private StackPanel TriggerEditor(string title, string side)
    {
        var panel = Panel(); panel.Children.Add(Kicker(side.ToUpperInvariant()));
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 4, 0, 16) });
        var mode = new ComboBox { Header = "Effect mode", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in new[] { "Off", "Rigid", "Pulse", "Rigid_A", "Rigid_B", "Rigid_AB", "Pulse_A", "Pulse_B", "Pulse_AB", "Calibration" }) mode.Items.Add(item);
        panel.Children.Add(mode);
        var start = new Slider { Header = "Start position", Minimum = 0, Maximum = 255, Value = 80, StepFrequency = 1, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(start);
        var startValue = new TextBlock { Text = "80 / 255", FontSize = 12, Foreground = Brush("InkSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(startValue);
        start.ValueChanged += (_, e) => startValue.Text = $"{(int)e.NewValue} / 255";
        var strength = new Slider { Header = "Resistance strength", Minimum = 0, Maximum = 8, Value = 5, StepFrequency = 1, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(strength);
        var strengthValue = new TextBlock { Text = "5 / 8", FontSize = 12, Foreground = Brush("InkSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(strengthValue);
        strength.ValueChanged += (_, e) => strengthValue.Text = $"{(int)e.NewValue} / 8";
        var pulseSettings = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        var end = new Slider { Header = "End position", Minimum = 0, Maximum = 255, Value = 150, StepFrequency = 1 };
        var frequency = new Slider { Header = "Pulse frequency", Minimum = 1, Maximum = 255, Value = 30, StepFrequency = 1 };
        var snap = new Slider { Header = "Snap force", Minimum = 0, Maximum = 8, Value = 3, StepFrequency = 1 };
        pulseSettings.Children.Add(end); pulseSettings.Children.Add(frequency); pulseSettings.Children.Add(snap); panel.Children.Add(pulseSettings);
        mode.SelectionChanged += (_, _) => pulseSettings.Visibility = mode.SelectedItem?.ToString()?.StartsWith("Pulse", StringComparison.Ordinal) == true ? Visibility.Visible : Visibility.Collapsed;
        var apply = new Button { Content = "Apply to " + side.ToLowerInvariant(), Margin = new Thickness(0, 18, 0, 0), MinWidth = 124 };
        apply.Click += async (_, _) => { try { await _api.ApplyTriggerAsync(side, mode.SelectedItem?.ToString() ?? "Off", (int)start.Value, (int)end.Value, (int)strength.Value, (int)frequency.Value, (int)snap.Value); SetStatus($"{side} trigger updated", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        var initial = side == "Left" ? _configuration?.LeftTrigger : _configuration?.RightTrigger;
        if (initial is not null)
        {
            mode.SelectedItem = initial.Mode.ToString(); start.Value = initial.StartPosition;
            end.Value = initial.EndPosition; strength.Value = initial.Strength;
            frequency.Value = Math.Max(1, (int)initial.Frequency); snap.Value = initial.SnapForce;
        }
        panel.Children.Add(apply);
        return panel;
    }

    private UIElement BuildHaptics()
    {
        var root = PageStack(); root.Children.Add(InlineNote("Audio to haptics", "Use system playback as the source for controller vibration. The API currently exposes a system default capture device and a low, mid, treble or full-band filter."));
        var panel = Panel(); panel.Children.Add(Kicker("AUDIO PIPELINE")); panel.Children.Add(new TextBlock { Text = "System audio", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 5, 0, 14) });
        var enabled = new ToggleSwitch { Header = "Audio drives haptics", OffContent = "Off", OnContent = "On" }; panel.Children.Add(enabled);
        var band = new ComboBox { Header = "Frequency band", SelectedIndex = 0, Width = 260, Margin = new Thickness(0, 14, 0, 0) }; foreach (var b in new[] { "bass", "mid", "treble", "all" }) band.Items.Add(b); panel.Children.Add(band);
        var gain = new Slider { Header = "Haptic gain", Minimum = 0, Maximum = 4, Value = 1, StepFrequency = 0.1, Width = 340, Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(gain);
        var gainLabel = new TextBlock { Text = "1.0Г—", FontSize = 12, Foreground = Brush("InkSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Right, Width = 340 }; panel.Children.Add(gainLabel); gain.ValueChanged += (_, e) => gainLabel.Text = $"{e.NewValue:0.0}Г—";
        var state = new TextBlock { Text = "Pipeline status loads from the local service.", FontSize = 13, Foreground = Brush("InkSecondaryBrush"), Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(state);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 18, 0, 0) };
        var apply = new Button { Content = "Apply audio settings", MinWidth = 160 }; apply.Click += async (_, _) => { try { await _api.SetHapticsAsync(enabled.IsOn, band.SelectedItem?.ToString() ?? "bass", gain.Value); var live = await _api.GetHapticsStateAsync(); state.Text = live?.AudioPipelineRunning == true ? $"Running В· {live.AudioDevice ?? "System audio device"} В· signal {live.CurrentRms:0.000}" : "Stopped"; SetStatus("Haptic settings applied", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        var status = new Button { Content = "Refresh status" }; status.Click += async (_, _) => { try { var live = await _api.GetHapticsStateAsync(); state.Text = live?.AudioPipelineRunning == true ? $"Running В· {live.AudioDevice ?? "System audio device"} В· signal {live.CurrentRms:0.000}" : "Stopped"; } catch (Exception ex) { state.Text = ShortError(ex); } };
        actions.Children.Add(apply); actions.Children.Add(status); panel.Children.Add(actions); root.Children.Add(panel);
        var manual = Panel(); manual.Children.Add(Kicker("MANUAL RUMBLE")); manual.Children.Add(new TextBlock { Text = "Motor levels", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 5, 0, 4) });
        manual.Children.Add(new TextBlock { Text = "Applying manual levels turns off audio conversion until you enable it again.", FontSize = 13, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var low = new Slider { Header = "Low frequency В· body", Minimum = 0, Maximum = 255, Value = 0, StepFrequency = 1, Width = 400 };
        var high = new Slider { Header = "High frequency В· detail", Minimum = 0, Maximum = 255, Value = 0, StepFrequency = 1, Width = 400 };
        manual.Children.Add(low); manual.Children.Add(high);
        var motorApply = new Button { Content = "Apply motor levels", Margin = new Thickness(0, 12, 0, 0) };
        motorApply.Click += async (_, _) => { try { await _api.SetHapticsAsync(false, "bass", 1, (int)low.Value, (int)high.Value); enabled.IsOn = false; await LoadHapticStateAsync(state); SetStatus("Motor levels applied", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        manual.Children.Add(motorApply);
        var stop = new Button { Content = "Stop all vibration", Margin = new Thickness(0, 8, 0, 0) };
        stop.Click += async (_, _) => { try { await _api.SetHapticsAsync(false, "bass", 1); ShowPage("Haptics"); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        manual.Children.Add(stop); root.Children.Add(manual);
        if (_configuration is { } config)
        {
            enabled.IsOn = config.Haptics.AudioToHapticEnabled;
            band.SelectedItem = config.Haptics.AudioBand; gain.Value = config.Haptics.HapticGain;
            low.Value = config.Haptics.RumbleLow; high.Value = config.Haptics.RumbleHigh;
        }
        _ = LoadHapticStateAsync(state);
        var tone = new Button { Content = "Play speaker test tone" };
        tone.Click += async (_, _) => { tone.IsEnabled = false; try { await _api.PlayToneAsync(); SetStatus("Speaker test played", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } finally { tone.IsEnabled = true; } };
        root.Children.Add(tone);
        return root;
    }

    private async Task LoadHapticStateAsync(TextBlock target)
    {
        try
        {
            var state = await _api.GetHapticsStateAsync();
            target.Text = state?.AudioPipelineRunning == true ? $"Running В· {state.AudioDevice} В· signal {state.CurrentRms:0.000}" : "Audio pipeline stopped";
        }
        catch (Exception ex) { target.Text = ShortError(ex); }
    }

    private UIElement BuildLighting()
    {
        var root = PageStack(); root.Children.Add(InlineNote("Lighting controls", "Configure a fixed lightbar color, player indicators, microphone mute LED or a dynamic effect."));
        var panel = Panel(); panel.Children.Add(Kicker("LIGHTBAR & INDICATORS"));
        panel.Children.Add(new TextBlock { Text = "Dynamic lighting", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 5, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Color values use 0вЂ“255 per channel. Some dynamic effects override the selected color.", FontSize = 14, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 });
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        var red = ChannelInput("Red", 255); var green = ChannelInput("Green", 255); var blue = ChannelInput("Blue", 255);
        colorRow.Children.Add(red); colorRow.Children.Add(green); colorRow.Children.Add(blue); panel.Children.Add(colorRow);
        var settings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 8, 0, 0) };
        var effect = new ComboBox { Header = "Effect", SelectedIndex = 0, Width = 220 }; foreach (var item in new[] { "none", "battery", "audio_reactive", "breathing", "rainbow" }) effect.Items.Add(item); settings.Children.Add(effect);
        var player = new ComboBox { Header = "Player LEDs", SelectedIndex = 0, Width = 150 }; foreach (var item in new[] { "Player 1", "Player 2", "Player 3", "Player 4" }) player.Items.Add(item); settings.Children.Add(player);
        settings.Children.Add(new TextBlock { Text = "Choose the controller's player number. The light pattern follows the DualSense player indicator convention.", FontSize = 12, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Width = 280, VerticalAlignment = VerticalAlignment.Center });
        var mic = new ToggleSwitch { Header = "Mic mute LED", OffContent = "Off", OnContent = "On", VerticalAlignment = VerticalAlignment.Bottom }; settings.Children.Add(mic); panel.Children.Add(settings);
        var effectHelp = new TextBlock { Text = "Breathing pulses the selected color; Rainbow cycles hues; Audio reactive brightens with rumble output; Battery pulses the selected color because battery telemetry is not available on this connection. Speed controls the animation rate.", FontSize = 12, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(effectHelp);
        effect.SelectionChanged += (_, _) => effectHelp.Text = effect.SelectedItem?.ToString() switch
        {
            "rainbow" => "Rainbow cycles through the full color spectrum. Speed controls how quickly it changes.",
            "audio_reactive" => "Audio reactive brightens with controller rumble output. Increase rumble or use an audio haptic effect to see the response.",
            "battery" => "Battery pulses the selected color. Battery percentage telemetry is not available from this controller session.",
            "breathing" => "Breathing fades the selected color up and down. Speed controls the pulse rate.",
            _ => "None keeps the lightbar at the selected RGB color. RGB values range from 0 (off) to 255 (full intensity)."
        };
        var speed = new Slider { Header = "Effect speed", Minimum = 0, Maximum = 255, Value = 128, StepFrequency = 1, Width = 340, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(speed);
        var reset = new Button { Content = "Reset to controller default", Margin = new Thickness(0, 20, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var apply = new Button { Content = "Apply lighting", MinWidth = 130 }; apply.Click += async (_, _) => { if (!byte.TryParse(red.Text, out var r) || !byte.TryParse(green.Text, out var g) || !byte.TryParse(blue.Text, out var b)) { SetStatus("Color channels must be from 0 to 255", InfoBarSeverity.Warning); return; } try { var masks = new[] { 0x04, 0x0A, 0x15, 0x1B }; await _api.SetLightingAsync(r, g, b, masks[Math.Clamp(player.SelectedIndex, 0, 3)], mic.IsOn, effect.SelectedItem?.ToString() ?? "none", (int)speed.Value); SetStatus("Lighting updated", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        reset.Click += async (_, _) => { try { await _api.ResetLightingAsync(); SetStatus("Lighting reset to PlayStation blue В· Player 1", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        actions.Children.Add(apply); actions.Children.Add(reset); panel.Children.Add(actions); root.Children.Add(panel);
        if (_configuration is { } config)
        {
            red.Text = config.Lighting.LightbarColor.R.ToString(); green.Text = config.Lighting.LightbarColor.G.ToString(); blue.Text = config.Lighting.LightbarColor.B.ToString();
            effect.SelectedItem = config.Lighting.DynamicEffect; speed.Value = config.Lighting.EffectSpeed;
            player.SelectedIndex = Array.IndexOf(new[] { 0x04, 0x0A, 0x15, 0x1B }, (int)config.Lighting.PlayerLeds);
            if (player.SelectedIndex < 0) player.SelectedIndex = 0;
            mic.IsOn = config.Lighting.MicLedEnabled;
        }
        return root;
    }

    private TextBox ChannelInput(string label, int value) => new() { Header = label, Text = value.ToString(), Width = 90 };

    private UIElement BuildDiagnostics()
    {
        var root = PageStack();
        if (_diagnostics is null || string.IsNullOrWhiteSpace(_diagnostics.DevicePath)) root.Children.Add(EmptyState("Controller not connected", "Connect a DualSense controller to start a session."));
        else
        {
            var grid = new Grid { ColumnSpacing = 20, RowSpacing = 14 };
            for (int i = 0; i < 2; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddMetricCell(grid, 0, 0, "CONNECTION", _diagnostics.ConnectionType, "DualSense HID");
            AddMetricCell(grid, 1, 0, "REPORTS SENT", _diagnostics.ReportsSent.ToString("N0"), "Since connected");
            AddMetricCell(grid, 0, 1, "BATTERY", _diagnostics.BatteryPercent < 0 ? "Unavailable" : $"{_diagnostics.BatteryPercent}%", _diagnostics.IsCharging ? "Charging" : "Telemetry status");
            AddMetricCell(grid, 1, 1, "SESSION UPTIME", TimeSpan.FromSeconds(_diagnostics.UptimeSeconds).ToString("hh\\:mm\\:ss"), "Current connection"); root.Children.Add(grid);
            root.Children.Add(InlineNote("Device path", string.IsNullOrWhiteSpace(_diagnostics.DevicePath) ? "Not reported" : _diagnostics.DevicePath));
            root.Children.Add(InlineNote("Firmware", string.IsNullOrWhiteSpace(_diagnostics.FirmwareVersion) ? "Firmware version is not available from the current API session." : _diagnostics.FirmwareVersion));
        }
        return root;
    }

    private UIElement BuildSettings()
    {
        var root = PageStack(); var panel = Panel(); panel.Children.Add(Kicker("LOCAL SERVICE"));
        panel.Children.Add(new TextBlock { Text = "API connection", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 5, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "QuadroSense runs locally. Choose the address used by this desktop app.", FontSize = 14, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        var url = new TextBox { Header = "Base URL", Text = _api.BaseUrl, Width = 360, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(url);
        var save = new Button { Content = "Use this address", Margin = new Thickness(0, 14, 0, 0) }; save.Click += async (_, _) => { if (Uri.TryCreate(url.Text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")) { try { _api.BaseUrl = uri.ToString().TrimEnd('/'); await _localApi.EnsureRunningAsync(_api); await RefreshConnectionAsync(); SetStatus("Service connected В· address saved", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } } else SetStatus("Enter a valid http or https address", InfoBarSeverity.Warning); }; panel.Children.Add(save);
        root.Children.Add(panel); root.Children.Add(InlineNote("Offline by design", "The application talks only to the local QuadroSense API. No account or remote service is required.")); return root;
    }

    private UIElement BuildMapping()
    {
        var root = PageStack(); root.Children.Add(InlineNote("Button bindings", "Map a physical button to another controller button. Turbo repeats while held; hold toggle keeps the mapped input down after release."));
        var panel = Panel(); panel.Children.Add(Kicker("BINDINGS"));
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 12) }; panel.Children.Add(list);
        _ = LoadBindings(list);
        var editor = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var physical = ButtonPicker("Physical button", new[] { "Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "L3", "R3", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "Options", "Create", "PS", "Mute", "TouchPadClick" });
        var mapped = ButtonPicker("Map to", new[] { "Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "L3", "R3", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "Options", "Create", "PS", "Mute", "TouchPadClick" });
        editor.Children.Add(physical); Grid.SetColumn(mapped, 1); editor.Children.Add(mapped);
        var turbo = new ToggleSwitch { Header = "Turbo", OffContent = "Off", OnContent = "On" };
        var hold = new ToggleSwitch { Header = "Hold toggle", OffContent = "Off", OnContent = "On" };
        Grid.SetRow(turbo, 1); editor.Children.Add(turbo); Grid.SetRow(hold, 1); Grid.SetColumn(hold, 1); editor.Children.Add(hold); panel.Children.Add(editor);
        var save = new Button { Content = "Save binding", Margin = new Thickness(0, 12, 0, 0) };
        save.Click += async (_, _) => { try { var items = (await _api.GetBindingsAsync()).ToList(); var key = physical.SelectedItem?.ToString() ?? "Cross"; items.RemoveAll(x => x.PhysicalButton == key); items.Add(new ButtonBinding(key, mapped.SelectedItem?.ToString() ?? "Cross", turbo.IsOn, 10, hold.IsOn)); await _api.SaveBindingsAsync(items); SetStatus("Button binding saved", InfoBarSeverity.Success); ShowPage("Mapping"); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        panel.Children.Add(save); root.Children.Add(panel);
        var macros = Panel(); macros.Children.Add(Kicker("MACROS")); macros.Children.Add(new TextBlock { Text = "Action sequences", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 4, 0, 8) });
        var macroList = new StackPanel { Spacing = 8 }; macros.Children.Add(macroList); _ = LoadMacros(macroList); root.Children.Add(macros);
        return root;
    }

    private async Task LoadBindings(StackPanel list)
    {
        try
        {
            var bindings = await _api.GetBindingsAsync();
            if (bindings.Count == 0) { list.Children.Add(new TextBlock { Text = "No custom bindings. Physical controls use their default mapping.", FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); return; }
            foreach (var binding in bindings) list.Children.Add(new TextBlock { Text = $"{binding.PhysicalButton}  в†’  {binding.MappedTo}{(binding.TurboEnabled ? "  В·  Turbo" : "")}{(binding.HoldToggle ? "  В·  Hold toggle" : "")}", FontSize = 14, Foreground = Brush("InkPrimaryBrush") });
        }
        catch (Exception ex) { list.Children.Add(new TextBlock { Text = ShortError(ex), FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); }
    }

    private async Task LoadMacros(StackPanel list)
    {
        try
        {
            var macros = await _api.GetMacrosAsync();
            if (macros.Count == 0) { list.Children.Add(new TextBlock { Text = "No macros saved yet.", FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); return; }
            foreach (var macro in macros)
            {
                var row = new Grid { ColumnSpacing = 16 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = $"{macro.Name}  В·  {macro.Steps.Count} steps", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("InkPrimaryBrush") });
                var play = new Button { Content = "Run" }; play.Click += async (_, _) => { try { await _api.TriggerMacroAsync(macro.Id); SetStatus($"{macro.Name} started", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
                Grid.SetColumn(play, 1); row.Children.Add(play); list.Children.Add(row);
            }
        }
        catch (Exception ex) { list.Children.Add(new TextBlock { Text = ShortError(ex), FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); }
    }

    private UIElement BuildTuning()
    {
        var root = PageStack(); root.Children.Add(InlineNote("Stick response", "The deadzone is the inner area ignored near the center. Anti-deadzone sets the minimum output after movement leaves that area. Curve exponent controls acceleration: 1.0 is linear, below 1 ramps up early, and above 1 ramps up later. Save stores all settings in the active profile; if none is active, Save creates and activates a Default profile."));
        var panel = Panel(); panel.Children.Add(Kicker("STICK RESPONSE"));
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = StickEditor("Left stick"); grid.Children.Add(left.Container); var right = StickEditor("Right stick"); Grid.SetColumn(right.Container, 1); grid.Children.Add(right.Container); panel.Children.Add(grid);
        var touch = new ToggleSwitch { Header = "Touchpad mouse emulation", OffContent = "Off", OnContent = "On", Margin = new Thickness(0, 20, 0, 0) }; panel.Children.Add(touch);
        panel.Children.Add(HelpText("When enabled, touchpad movement controls the mouse pointer. Turn it off to leave touchpad handling to games and other apps."));
        var dpi = new NumberBox { Header = "Mouse DPI", Value = 1600, Minimum = 100, Maximum = 32000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 240 }; panel.Children.Add(dpi);
        panel.Children.Add(HelpText("DPI sets pointer travel per touchpad movement. Higher values move the pointer farther; this only applies when mouse emulation is on."));
        var gyro = new ToggleSwitch { Header = "Gyroscope enabled", OffContent = "Off", OnContent = "On", Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(gyro);
        panel.Children.Add(HelpText("Enable gyro output to map controller rotation to the selected target. Keep the controller still briefly to see its neutral orientation."));
        var gyroOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        var sensitivity = new Slider { Header = "Sensitivity", Minimum = 0.1, Maximum = 5, Value = 1, StepFrequency = 0.1, Width = 300 };
        var target = new ComboBox { Header = "Output", SelectedIndex = 0, Width = 180 }; foreach (var value in new[] { "rightStick", "mouse", "none" }) target.Items.Add(value);
        gyroOptions.Children.Add(sensitivity); gyroOptions.Children.Add(target); panel.Children.Add(gyroOptions);
        var gyroPreview = BuildGyroPreview(); panel.Children.Add(gyroPreview);
        sensitivity.Header = "Sensitivity В· 0.1вЂ“5Г—";
        target.Header = "Gyro output";
        panel.Children.Add(HelpText("Sensitivity multiplies gyro movement. Right stick turns motion into stick input; Mouse maps it to pointer movement; None disables the output while preserving the preview."));
        var save = new Button { Content = "Save tuning", IsEnabled = true, Margin = new Thickness(0, 18, 0, 0) };
        save.Click += async (_, _) => { save.IsEnabled = false; try { var config = new TuningConfig(left.GetValue(), right.GetValue(), new TouchpadConfig(touch.IsOn, (int)dpi.Value, _loadedTuning?.Touchpad.InvertX ?? false, _loadedTuning?.Touchpad.InvertY ?? false), new GyroConfig(gyro.IsOn, (float)sensitivity.Value, _loadedTuning?.Gyro.DeadzoneDps ?? 0.5f, target.SelectedItem?.ToString() ?? "rightStick")); await _api.SaveTuningAsync(config); _loadedTuning = config; SetStatus("Tuning saved to the active profile", InfoBarSeverity.Success); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } finally { save.IsEnabled = true; } };
        panel.Children.Add(save); root.Children.Add(panel); _ = LoadTuningAsync(left, right, touch, dpi, gyro, sensitivity, target, save); return root;
    }

    private async Task LoadTuningAsync((StackPanel Container, Func<StickConfig> GetValue, Action<StickConfig> SetValue) left,
        (StackPanel Container, Func<StickConfig> GetValue, Action<StickConfig> SetValue) right, ToggleSwitch touch, NumberBox dpi,
        ToggleSwitch gyro, Slider sensitivity, ComboBox target, Button save)
    {
        try
        {
            var value = await _api.GetTuningAsync(); if (value is null) { _loadedTuning = new TuningConfig(new StickConfig(), new StickConfig(), new TouchpadConfig(), new GyroConfig()); SetStatus("No active tuning profile yet. Saving will create and activate Default.", InfoBarSeverity.Informational); return; }
            _loadedTuning = value;
            left.SetValue(value.LeftStick); right.SetValue(value.RightStick); touch.IsOn = value.Touchpad.MouseEmulation;
            dpi.Value = value.Touchpad.MouseDpi; gyro.IsOn = value.Gyro.Enabled; sensitivity.Value = value.Gyro.Sensitivity;
            target.SelectedItem = value.Gyro.OutputTarget;
        }
        catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); }
    }

    private (StackPanel Container, Func<StickConfig> GetValue, Action<StickConfig> SetValue) StickEditor(string title)
    {
        var panel = new StackPanel { Spacing = 2 }; panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 0, 0, 8) });
        var deadzone = new Slider { Header = "Deadzone", Minimum = 0, Maximum = 1, Value = 0.08, StepFrequency = 0.01 };
        var anti = new Slider { Header = "Anti-deadzone", Minimum = 0, Maximum = 1, Value = 0, StepFrequency = 0.01 };
        var curve = new Slider { Header = "Curve exponent", Minimum = 0.2, Maximum = 3, Value = 1, StepFrequency = 0.1 };
        panel.Children.Add(deadzone); panel.Children.Add(anti); panel.Children.Add(curve);
        panel.Children.Add(HelpText("Deadzone ignores small movement near center. Anti-deadzone boosts output once the stick leaves the deadzone. Curve exponent shapes how quickly output grows."));
        var diagram = new Canvas { Width = 144, Height = 144, Margin = new Thickness(4, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var outer = new Ellipse { Width = 112, Height = 112, Stroke = Brush("InkSecondaryBrush"), StrokeThickness = 1.5 };
        var antiRing = new Ellipse { Stroke = Brush("AccentFillColorDefaultBrush"), StrokeThickness = 2, Opacity = 0.8 };
        var deadRing = new Ellipse { Stroke = Brush("InkPrimaryBrush"), StrokeThickness = 2, Fill = Brush("CardBackgroundFillColorDefaultBrush"), Opacity = 0.95 };
        var center = new Ellipse { Width = 6, Height = 6, Fill = Brush("InkPrimaryBrush") };
        Canvas.SetLeft(outer, 16); Canvas.SetTop(outer, 16); Canvas.SetLeft(center, 69); Canvas.SetTop(center, 69);
        diagram.Children.Add(outer); diagram.Children.Add(antiRing); diagram.Children.Add(deadRing); diagram.Children.Add(center);
        void UpdateDiagram()
        {
            double antiRadius = 56 * (1 - anti.Value);
            double deadRadius = 56 * deadzone.Value;
            antiRing.Width = antiRadius * 2; antiRing.Height = antiRadius * 2;
            deadRing.Width = deadRadius * 2; deadRing.Height = deadRadius * 2;
            Canvas.SetLeft(antiRing, 72 - antiRadius); Canvas.SetTop(antiRing, 72 - antiRadius);
            Canvas.SetLeft(deadRing, 72 - deadRadius); Canvas.SetTop(deadRing, 72 - deadRadius);
        }
        deadzone.ValueChanged += (_, _) => UpdateDiagram(); anti.ValueChanged += (_, _) => UpdateDiagram(); UpdateDiagram();
        var line = new Line { X1 = 72, Y1 = 72, X2 = 72, Y2 = 72, Stroke = Brush("AccentFillColorDefaultBrush"), StrokeThickness = 2, Visibility = Visibility.Collapsed };
        var position = new Ellipse { Width = 8, Height = 8, Fill = Brush("AccentFillColorDefaultBrush"), Visibility = Visibility.Collapsed };
        diagram.Children.Add(line); diagram.Children.Add(position);
        Canvas.SetLeft(position, 68); Canvas.SetTop(position, 68);
        double currentX = 0, currentY = 0;
        void UpdatePosition()
        {
            bool active = Math.Sqrt(currentX * currentX + currentY * currentY) > deadzone.Value;
            line.Visibility = position.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            line.X2 = 72 + currentX * 56; line.Y2 = 72 + currentY * 56;
            Canvas.SetLeft(position, line.X2 - 4); Canvas.SetTop(position, line.Y2 - 4);
        }
        deadzone.ValueChanged += (_, _) => UpdatePosition();
        _stickUpdates.Add(motion =>
        {
            currentX = title == "Left stick" ? motion.LeftX : motion.RightX;
            currentY = title == "Left stick" ? motion.LeftY : motion.RightY;
            UpdatePosition();
        });
        panel.Children.Add(diagram);
        panel.Children.Add(HelpText("Diagram: gray outer ring is full travel, inner ring is the deadzone, and the colored ring marks the anti-deadzone threshold."));
        return (panel, () => new StickConfig((float)deadzone.Value, (float)anti.Value, (float)curve.Value), value => { deadzone.Value = value.Deadzone; anti.Value = value.AntiDeadzone; curve.Value = value.CurveExponent; });
    }

    private TextBlock HelpText(string text) => new() { Text = text, FontSize = 12, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620, Margin = new Thickness(0, 2, 0, 8) };

    private UIElement BuildGyroPreview()
    {
        var group = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Spacing = 6 };
        group.Children.Add(new TextBlock { Text = "LIVE GYROSCOPE ORIENTATION", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkSecondaryBrush") });
        var canvas = new Canvas { Width = 300, Height = 168, HorizontalAlignment = HorizontalAlignment.Left };
        var neutral = System.Numerics.Quaternion.Identity;
        var latest = System.Numerics.Quaternion.Identity;
        var recenter = new Button { Content = "Set neutral orientation" };
        recenter.Click += (_, _) => neutral = System.Numerics.Quaternion.Inverse(latest);
        group.Children.Add(recenter);
        group.Children.Add(HelpText("Rotate in any direction to inspect all three axes. Set neutral while holding the controller level. Gyro heading can drift; recenter when needed."));
        void Render(System.Numerics.Quaternion rotation)
        {
            canvas.Children.Clear();
            // Extruded controller outline: top, bottom and individually shaded side faces.
            var outline = new (float X, float Y)[] { (-100,-35),(-55,-48),(55,-48),(100,-35),(120,35),(95,60),(55,20),(-55,20),(-95,60),(-120,35) };
            var vertices = new List<System.Numerics.Vector3>();
            foreach (float depth in new[] { -12f, 12f })
                foreach (var point in outline)
                    vertices.Add(System.Numerics.Vector3.Transform(new(point.X, point.Y, depth), rotation));
            var faces = new List<int[]> { Enumerable.Range(0,10).ToArray(), Enumerable.Range(10,10).Reverse().ToArray() };
            for (int i=0;i<10;i++) faces.Add([i,(i+1)%10,(i+1)%10+10,i+10]);
            foreach (var face in faces.OrderBy(f => f.Average(i => vertices[i].Z)))
            {
                var polygon = new Polygon { Fill = Brush(face.Length > 4 ? "CardBackgroundFillColorDefaultBrush" : "LayerFillColorAltBrush"), Stroke = Brush("InkSecondaryBrush"), StrokeThickness = 1.5 };
                foreach (int index in face)
                {
                    var v = vertices[index]; double perspective = 450 / (450 - v.Z);
                    polygon.Points.Add(new Windows.Foundation.Point(150 + v.X * perspective, 80 + v.Y * perspective));
                }
                canvas.Children.Add(polygon);
            }
            foreach (var marker in new[] { new System.Numerics.Vector3(-45,5,14), new System.Numerics.Vector3(45,5,14), new System.Numerics.Vector3(0,-25,14) })
            {
                var v = System.Numerics.Vector3.Transform(marker, rotation); double scale = 450 / (450-v.Z);
                var dot = new Ellipse { Width = 14*scale, Height = 14*scale, Fill = Brush("AccentFillColorDefaultBrush") };
                Canvas.SetLeft(dot,150+v.X*scale-7*scale); Canvas.SetTop(dot,80+v.Y*scale-7*scale); canvas.Children.Add(dot);
            }
        }
        Render(latest);
        group.Children.Add(canvas);
        var readout = HelpText("Waiting for controller motionвЂ¦ Connect a DualSense to see live tilt."); group.Children.Add(readout);
        _gyroTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _gyroTimer.Tick += async (_, _) =>
        {
            try
            {
                var motion = await _api.GetMotionAsync();
                if (motion?.UpdatedAt is null) { readout.Text = "Waiting for controller motionвЂ¦ Connect a DualSense to see live tilt."; return; }
                foreach (var update in _stickUpdates) update(motion);
                latest = System.Numerics.Quaternion.Normalize(new(motion.Qx, motion.Qy, motion.Qz, motion.Qw));
                Render(System.Numerics.Quaternion.Inverse(System.Numerics.Quaternion.Normalize(neutral * latest)));
                readout.Text = $"Pitch {-motion.PitchDegrees:+0.0;-0.0;0.0}В°   В·   Roll {-motion.RollDegrees:+0.0;-0.0;0.0}В°";
            }
            catch { readout.Text = "Motion preview unavailable. Check the controller connection."; }
        };
        _gyroTimer.Start();
        return group;
    }

    private UIElement BuildIntegrations()
    {
        var root = PageStack(); root.Children.Add(InlineNote("Registered mods", "Local game integrations can send temporary trigger, haptic and lighting overrides. Expiring overrides revert automatically."));
        var panel = Panel(); panel.Children.Add(Kicker("LOCAL MODS"));
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 14) }; panel.Children.Add(list); _ = LoadMods(list);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var name = new TextBox { Header = "Mod name", PlaceholderText = "For example, GameEffects", Width = 260 };
        var version = new TextBox { Header = "Version", Text = "1.0.0", Width = 140 };
        var register = new Button { Content = "Register mod", VerticalAlignment = VerticalAlignment.Bottom };
        register.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) { SetStatus("Enter a mod name", InfoBarSeverity.Warning); return; } try { await _api.RegisterModAsync(name.Text.Trim(), version.Text.Trim()); SetStatus("Mod registered", InfoBarSeverity.Success); ShowPage("Integrations"); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
        row.Children.Add(name); row.Children.Add(version); row.Children.Add(register); panel.Children.Add(row); root.Children.Add(panel); return root;
    }

    private async Task LoadMods(StackPanel list)
    {
        try
        {
            var mods = await _api.GetModsAsync();
            if (mods.Count == 0) { list.Children.Add(new TextBlock { Text = "No mods registered.", FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); return; }
            foreach (var mod in mods)
            {
                var row = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 6, 0, 6) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = $"{mod.Name}  В·  v{mod.Version}  В·  API {mod.ApiContract}", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("InkPrimaryBrush") });
                var remove = new Button { Content = "Unregister" }; remove.Click += async (_, _) => { try { await _api.RemoveModAsync(mod.Id); SetStatus($"{mod.Name} unregistered", InfoBarSeverity.Success); ShowPage("Integrations"); } catch (Exception ex) { SetStatus(ShortError(ex), InfoBarSeverity.Error); } };
                Grid.SetColumn(remove, 1); row.Children.Add(remove); list.Children.Add(row);
            }
        }
        catch (Exception ex) { list.Children.Add(new TextBlock { Text = ShortError(ex), FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); }
    }

    private ComboBox ButtonPicker(string header, IEnumerable<string> values)
    {
        var picker = new ComboBox { Header = header, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values) picker.Items.Add(value); return picker;
    }

    private StackPanel PageStack() => new() { Spacing = 20, MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
    private StackPanel Panel() => new() { Background = Brush("PanelBackgroundBrush"), Padding = new Thickness(22), Spacing = 6 };
    private TextBlock Kicker(string text) => new() { Text = text, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 100, Foreground = Brush("AccentBrush") };
    private StackPanel InlineNote(string title, string text)
    {
        var p = new StackPanel { Spacing = 5, Padding = new Thickness(16, 14, 16, 14), Background = Brush("PanelBackgroundBrush") };
        p.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush") }); p.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 780 }); return p;
    }
    private StackPanel EmptyState(string title, string message)
    {
        var p = new StackPanel { Padding = new Thickness(24, 28, 24, 28), Spacing = 7, Background = Brush("PanelBackgroundBrush") };
        p.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush") }); p.Children.Add(new TextBlock { Text = message, FontSize = 14, Foreground = Brush("InkSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }); return p;
    }
    private StackPanel MetricPanel(string title, string value, string caption)
    {
        var p = Panel(); p.Children.Add(Kicker(title)); p.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkPrimaryBrush"), Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Segoe UI Variable") }); p.Children.Add(new TextBlock { Text = caption, FontSize = 13, Foreground = Brush("InkSecondaryBrush") }); return p;
    }
    private void AddMetricCell(Grid grid, int col, int row, string label, string value, string hint)
    {
        var p = MetricPanel(label, value, hint); Grid.SetColumn(p, col); Grid.SetRow(p, row); grid.Children.Add(p);
    }
    private void AddQuick(Grid grid, int col, string kicker, string description, string action, string page)
    {
        var p = Panel(); p.Children.Add(Kicker(kicker)); p.Children.Add(new TextBlock { Text = description, FontSize = 14, Foreground = Brush("InkPrimaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12) }); var b = new Button { Content = action }; b.Click += (_, _) => Navigate(page); p.Children.Add(b); Grid.SetColumn(p, col); grid.Children.Add(p);
    }
    private void Navigate(string tag)
    {
        foreach (var obj in AppNavigation.MenuItems.Concat(AppNavigation.FooterMenuItems)) if (obj is NavigationViewItem item && Equals(item.Tag, tag)) { AppNavigation.SelectedItem = item; return; }
    }
    private void SetStatus(string message, InfoBarSeverity severity)
    {
        if (_closed) return;
        StatusBar.Title = severity == InfoBarSeverity.Error ? "Could not complete action" : severity == InfoBarSeverity.Warning ? "Check your settings" : severity == InfoBarSeverity.Success ? "Done" : "Information";
        StatusBar.Message = message; StatusBar.Severity = InfoBarSeverity.Informational; StatusBar.IsIconVisible = false; StatusBar.IsOpen = true;
    }
    private Brush Brush(string key)
    {
        var resources = Application.Current.Resources;
        if (resources.TryGetValue(key, out var resource) && resource is Brush brush)
            return brush;

        if (resources.ThemeDictionaries.TryGetValue((Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark ? "Dark" : "Light", out var lightTheme) &&
            lightTheme is ResourceDictionary lightResources &&
            lightResources.TryGetValue(key, out resource) && resource is Brush lightBrush)
            return lightBrush;

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
    }
    private static string ShortError(Exception ex) => ex is HttpRequestException ? "Could not reach the service. Open Settings to check the API address, or choose Retry service." : ex is TaskCanceledException ? "The service took too long to respond. Try again." : ex.Message;
}
