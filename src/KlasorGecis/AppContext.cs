using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace KlasorGecis;

internal sealed class FolderAppContext : ApplicationContext, IAppSettings
{
    private readonly FolderBar _bar = new();
    private readonly IExplorerSource _source;
    private readonly FolderAvailability _availability = new();
    private readonly NotifyIcon _tray;
    private ToolStripMenuItem _enabledMenu = null!;
    private ToolStripMenuItem _sourceMenu = null!;
    private ToolStripMenuItem _startupMenu = null!;
    private ToolStripMenuItem _adminMenu = null!;
    private ToolStripMenuItem _autoMenu = null!, _historyMenu = null!;
    private readonly PreferencesStore _preferences;
    private readonly AutoSwitchPolicy _autoPolicy = new();
    private readonly Stopwatch _autoClock = Stopwatch.StartNew();
    private readonly RefreshPolicy _refreshPolicy = new();
    private AutoSwitchIntent? _pendingAuto;
    private AutoSwitchIntent? _activeAuto;
    private bool _foregroundHookAvailable;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly HotkeyWindow _messages;
    private readonly ShortcutRegistration _shortcut;
    private readonly string _settingsPath;
    private readonly ContextMenuStrip _foldersMenu = new FolderPickerMenu { ShowImageMargin = false, ShowCheckMargin = true };
    private readonly Native.WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private readonly Dictionary<nint, long> _recency = [];
    private IReadOnlyList<FolderLocation> _folders = [];
    private FolderLocation? _selected;
    private DialogTarget? _target;
    private CancellationTokenSource? _navigation;
    private readonly CancellationTokenSource _lifetime = new();
    private long _sequence;
    private nint _lastForeground;
    private bool _refreshing, _queued, _busy, _enabled = true, _disposed, _choosingSource;
    private string? _sourceError;
    private string? _settingsError;
    private nint _hotkeyAttempt;
    private SettingsWindow? _settingsWindow;
    private readonly StartupSettingAction _startupAction = new(StartupSettings.IsEnabled, value =>
    {
        bool saved = StartupSettings.SetEnabled(value, out var error); return (saved, error);
    });

    internal FolderAppContext(string? settingsPath = null, IExplorerSource? source = null)
    {
        _source = source ?? new ExplorerSource();
        _ = _bar.Handle;
        _messages = new HotkeyWindow(id => { if (_shortcut?.Active == true && id == _shortcut.Id) Navigate(); }, QueueUpdate, DispatchUpdate);
        _settingsPath = settingsPath ?? ShortcutSettings.FilePath;
        _preferences = new PreferencesStore(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "folders.json"));
        Localizer.SetCurrent(_preferences.Current.Language);
        _bar.RefreshLanguage();
        _shortcut = new ShortcutRegistration(_messages.Handle, ShortcutSettings.Load(_settingsPath, out _settingsError));
        _bar.SetShortcut(_shortcut.Binding.DisplayText);
        _bar.NavigateRequested += (_, _) => Navigate();
        _bar.OtherFoldersRequested += (_, _) => ShowOtherFolders();
        _foldersMenu.Closed += (_, _) => { _choosingSource = false; QueueUpdate(); };
        var menu = CreateTrayMenu();
        _tray = new NotifyIcon { Text = "Klasör Geçiş", Icon = MakeIcon(), Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) =>
        {
            _tray.ShowBalloonTip(3500, "Klasör Geçiş", Localizer.T("Explorer'da bir klasör açın. Aç / Kaydet penceresindeki klasör satırına tıklayın veya ") + _shortcut.Binding.DisplayText + Localizer.T(" kullanın."), ToolTipIcon.Info);
        };
        _callback = OnWindowEvent;
        foreach (var range in new (uint Min, uint Max)[] { (3,3), (0x8001,0x8003), (0x800B,0x800B), (0x16,0x17) })
        {
            var hook = Native.SetWinEventHook(range.Min, range.Max, 0, _callback, 0, 0, 2);
            if (hook != 0) { _hooks.Add(hook); if (range.Min == 3) _foregroundHookAvailable = true; }
        }
        _autoMenu.Enabled = _foregroundHookAvailable;
        if (!_foregroundHookAvailable) _autoMenu.ToolTipText = Localizer.T("Pencere geçişleri izlenemedi; elle geçiş kullanılabilir.");
        // Seed startup preference using Explorer's current z-order. Foreground events supersede these values.
        long z = 0;
        Native.EnumWindows((h, _) =>
        {
            if (Native.Class(h) is "CabinetWClass" or "ExploreWClass") _recency[h] = --z;
            return true;
        }, 0);
        _timer.Tick += (_, _) => Reconcile();
        _timer.Start();
        Diagnostics.Write("started", "version=" + typeof(FolderAppContext).Assembly.GetName().Version + "; elevated=" + StartupSettings.IsAdministrator() + "; hooks=" + _hooks.Count);
        Localizer.Changed += OnLanguageChanged;
        QueueUpdate();
    }

    private ContextMenuStrip CreateTrayMenu()
    {
        _enabledMenu = new ToolStripMenuItem(Localizer.T("Etkin")) { Checked = true, CheckOnClick = false };
        _enabledMenu.Click += (_, _) =>
        {
            SetEnabled(!_enabled);
        };
        _sourceMenu = new ToolStripMenuItem(Localizer.T("Açık klasör bekleniyor")) { Enabled = false };
        _startupMenu = new ToolStripMenuItem(Localizer.T("Windows ile başlat")) { Checked = StartupSettings.IsEnabled(), CheckOnClick = false };
        _startupMenu.Click += (_, _) =>
        {
            ShowSettingsError(SetStartup(!StartupSettings.IsEnabled()));
        };
        _adminMenu = new ToolStripMenuItem(StartupSettings.IsAdministrator()
            ? Localizer.T("Yönetici modu etkin") : Localizer.T("Yönetici olarak yeniden başlat"))
        { Enabled = !StartupSettings.IsAdministrator() };
        _adminMenu.Click += (_, _) => RestartElevated();
        var log = new ToolStripMenuItem(Localizer.T("Tanılama kaydını aç"));
        log.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("notepad.exe", Diagnostics.LogPath) { UseShellExecute = false }); } catch (Exception e) { Diagnostics.Write("log-open", e.Message); } };
        var exit = new ToolStripMenuItem(Localizer.T("Çıkış")); exit.Click += (_, _) => ExitThread();
        _autoMenu = new ToolStripMenuItem(Localizer.T("Explorer'dan dönünce otomatik geç")) { Checked = _preferences.Current.AutoSwitchEnabled };
        _autoMenu.Click += (_, _) =>
        {
            ShowSettingsError(SetAutomatic(!_preferences.Current.AutoSwitchEnabled));
        };
        _historyMenu = new ToolStripMenuItem(Localizer.T("Son klasörleri hatırla")) { Checked = _preferences.Current.HistoryEnabled };
        _historyMenu.Click += (_, _) =>
        {
            ShowSettingsError(SetHistory(!_preferences.Current.HistoryEnabled));
        };
        _historyMenu.ToolTipText = Localizer.T("Kapalıyken yeni kayıt tutulmaz. Önceki kayıtlar Geçmişi temizle ile kaldırılır.");
        var clearHistory = new ToolStripMenuItem(Localizer.T("Geçmişi temizle"));
        clearHistory.Click += (_, _) => ShowSettingsError(ClearHistory());
        var shortcutMenu = new ToolStripMenuItem(Localizer.T("Kısayolu değiştir…"));
        shortcutMenu.Click += (_, _) =>
        {
            HideBar();
            ResetAutomatic();
            using var settings = new ShortcutDialog(_shortcut.Binding, ApplyShortcut);
            settings.ShowDialog();
        };
        var settingsMenu = new ToolStripMenuItem(Localizer.T("Ayarlar…")) { Name = "AppSettings" };
        settingsMenu.Click += (_, _) => ShowSettings();
        var menu = new ContextMenuStrip();
        AppTheme.Style(menu);
        menu.Opening += (_, _) => { ResetAutomatic(); _startupMenu.Checked = StartupSettings.IsEnabled(); };
        menu.Items.AddRange([_enabledMenu, _sourceMenu, new ToolStripSeparator(),
            shortcutMenu, _autoMenu, _historyMenu, clearHistory, new ToolStripSeparator(),
            _startupMenu, _adminMenu, new ToolStripSeparator(), settingsMenu, log, exit]);
        menu.RightToLeft = Localizer.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        _enabledMenu.Checked = _enabled; _autoMenu.Enabled = _foregroundHookAvailable;
        return menu;
    }

    private void OnLanguageChanged()
    {
        if (_disposed) return;
        HideBar(); ResetAutomatic(); _foldersMenu.Close();
        var previous = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = CreateTrayMenu(); previous?.Dispose();
        _bar.RefreshLanguage(); QueueUpdate();
    }

    internal string? ApplyShortcut(ShortcutBinding candidate)
    {
        string? error = _shortcut.Apply(candidate, _settingsPath);
        if (error is null)
        {
            _settingsError = null; _hotkeyAttempt = 0;
            _bar.SetShortcut(_shortcut.Binding.DisplayText); QueueUpdate();
        }
        return error;
    }

    internal void ShowSettings(string page = "general")
    {
        if (_disposed) return;
        HideBar(); ResetAutomatic(); _foldersMenu.Close();
        if (_settingsWindow is null || _settingsWindow.IsDisposed)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.FormClosed += (_, _) => { _settingsWindow = null; ResetAutomatic(); QueueUpdate(); };
            _settingsWindow.SelectPage(page); _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == FormWindowState.Minimized) _settingsWindow.WindowState = FormWindowState.Normal;
            _settingsWindow.SelectPage(page); _settingsWindow.Activate();
        }
    }

    public AppSettingsState ReadSettings()
    {
        var state = _preferences.Current;
        return new(_enabled, state.AutoSwitchEnabled, state.HistoryEnabled, StartupSettings.IsEnabled(),
            _foregroundHookAvailable, _shortcut.Binding, state.Favorites, state.Recent.Length,
            _selected is not null && SourceSelection.IsLive(_selected) ? _selected : null,
            Localizer.Message(_preferences.Error ?? _settingsError), state.Language);
    }
    public string? SetLanguage(string code)
    {
        if (!_preferences.SetLanguage(code, out var error)) return error;
        Localizer.SetCurrent(code);
        return null;
    }
    public string? SetEnabled(bool value)
    {
        _enabled = value; _enabledMenu.Checked = value; ResetAutomatic();
        if (!value) _navigation?.Cancel(); Reconcile(); return null;
    }
    public string? SetAutomatic(bool value)
    {
        if (value && !_foregroundHookAvailable) return Localizer.T("Pencere geçişleri izlenemedi; elle geçiş kullanılabilir.");
        _preferences.SetAutoSwitch(value, out var error); _autoMenu.Checked = _preferences.Current.AutoSwitchEnabled;
        if (!_autoMenu.Checked && _activeAuto is not null) _navigation?.Cancel();
        ResetAutomatic(); QueueUpdate(); return error;
    }
    public string? SetHistory(bool value)
    {
        _preferences.SetHistory(value, out var error); _historyMenu.Checked = _preferences.Current.HistoryEnabled;
        QueueUpdate(); return error;
    }
    public string? SetStartup(bool value)
    {
        string? error = _startupAction.Apply(value);
        _startupMenu.Checked = StartupSettings.IsEnabled(); return error;
    }
    public string? SetShortcut(ShortcutBinding value) => ApplyShortcut(value);
    public string? ClearHistory() { _preferences.ClearHistory(out var error); QueueUpdate(); return error; }
    public string? AddCurrentFavorite()
    {
        if (_selected is null || !SourceSelection.IsLive(_selected)) return Localizer.T("Açık bir Explorer klasörü seçilemedi.");
        _preferences.AddFavorite(_selected.Path, _selected.DisplayName, out var error); QueueUpdate(); return error;
    }
    public string? RemoveFavorite(string path) { _preferences.RemoveFavorite(path, out var error); QueueUpdate(); return error; }
    private static void ShowSettingsError(string? error)
    {
        if (error is not null) MessageBox.Show(error, "Klasör Geçiş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OnWindowEvent(nint hook, uint ev, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || hwnd == 0) return;
        if (ev == 0x8001 && objectId == 0 && childId == 0)
        {
            _autoPolicy.Destroyed(hwnd);
            if (_pendingAuto?.Target.Handle == hwnd || _pendingAuto?.Source.Handle == hwnd) _pendingAuto = null;
            if (_activeAuto?.Target.Handle == hwnd || _activeAuto?.Source.Handle == hwnd) _navigation?.Cancel();
        }
        if (ev == 3)
        {
            var identity = WindowIdentity.Read(hwnd);
            var kind = Native.Class(hwnd) switch { "CabinetWClass" or "ExploreWClass" => ForegroundKind.Explorer,
                "#32770" => ForegroundKind.Dialog, _ => ForegroundKind.Other };
            if (kind == ForegroundKind.Explorer) _refreshPolicy.Invalidate();
            _pendingAuto = identity is { } id ? _autoPolicy.Observe(new(id, kind), _autoClock.ElapsedMilliseconds,
                _enabled && _foregroundHookAvailable && _preferences.Current.AutoSwitchEnabled) : null;
            if (identity is null) _autoPolicy.Reset();
            // Preserve the event's real window even when several foreground changes coalesce.
            if (Native.Class(hwnd) is "CabinetWClass" or "ExploreWClass")
            {
                _recency[hwnd] = ++_sequence;
            }
            if (_busy && hwnd != _target?.Handle) _navigation?.Cancel();
        }
        if (ev == 3 || (objectId == 0 && (hwnd == _target?.Handle || hwnd == _selected?.WindowHandle || ev == 0x8001)))
            QueueUpdate();
    }

    private void QueueUpdate()
    {
        if (_queued || _disposed || _bar.IsDisposed) return;
        _queued = true;
        if (!_messages.PostUpdate()) _queued = false;
    }

    private void DispatchUpdate() { _queued = false; if (!_disposed) Reconcile(); }

    private void Reconcile()
    {
        if (_disposed) return;
        if (_choosingSource)
        {
            if (_target is null || !_target.IsCurrent()) _foldersMenu.Close();
            return;
        }
        var foreground = Native.GetForegroundWindow();
        // The source needs fast periodic polling while Explorer is being used.
        // Dialog navigation performs its own fresh read; avoid competing scans.
        bool active = Native.Class(foreground) is "CabinetWClass" or "ExploreWClass";
        bool changed = foreground != _lastForeground;
        if (changed)
        {
            _lastForeground = foreground;
            if (Native.Class(foreground) is "CabinetWClass" or "ExploreWClass")
            {
                _refreshPolicy.Invalidate();
                _recency[foreground] = ++_sequence;
            }
            if (_busy && foreground != _target?.Handle) _navigation?.Cancel();
        }
        if (!_enabled) { HideBar(); return; }
        if (_refreshPolicy.ShouldRefresh(_autoClock.ElapsedMilliseconds, active, _refreshing)) RefreshSources(active);

        var previousSelection = _selected;
        var previousTarget = _target?.Handle;
        var ordered = SourceSelection.Order(_folders, _recency, SourceSelection.IsLive);
        _selected = ordered.FirstOrDefault();
        _sourceMenu.Text = Localizer.Message(_sourceError ?? _preferences.Error ?? _settingsError) ?? (_selected is null ? Localizer.T("Açık klasör bekleniyor") : Localizer.F("Kaynak: {0}", _selected.DisplayName));
        _sourceMenu.ToolTipText = _sourceMenu.Text;
        if (_busy)
        {
            if (_target is null || !_target.IsCurrent()) HideBar();
            else PositionBar();
            return;
        }
        if (_target is null || _target.Handle != foreground || !_target.IsCurrent())
        {
            _target = DialogTarget.Detect(foreground);
            UnregisterShortcut();
        }
        if (previousTarget != _target?.Handle || previousSelection != _selected) _bar.SetStatus(null, false);
        if (_target is not null && _target.IsCurrent() && !_busy)
        {
            var pending = _pendingAuto; _pendingAuto = null;
            if (pending is not null && pending.Target.Matches(_target) && _enabled && _preferences.Current.AutoSwitchEnabled)
            {
                Navigate(autoIntent: pending);
                return;
            }
            if (_enabled && _foregroundHookAvailable && _preferences.Current.AutoSwitchEnabled && WindowIdentity.Read(_target.Handle) is { } id)
            {
                bool eligible = DialogInteraction.TryCapture(_target, ReadFilename(_target)) is not null;
                _autoPolicy.Observe(new(WindowIdentity.ForDialog(_target), ForegroundKind.Dialog, eligible), _autoClock.ElapsedMilliseconds,
                    _enabled && _foregroundHookAvailable && _preferences.Current.AutoSwitchEnabled);
            }
        }
        var folderState = _preferences.Current;
        bool hasSaved = folderState.Favorites.Length > 0 || (folderState.HistoryEnabled && folderState.Recent.Length > 0);
        if (_target is null || (_selected is null && !hasSaved)) { HideBar(); return; }
        if (_selected is null) UnregisterShortcut();
        if (_selected is not null && !_shortcut.Active && _hotkeyAttempt != _target.Handle)
        {
            _hotkeyAttempt = _target.Handle;
            if (!_shortcut.Register()) Diagnostics.Write("shortcut-unavailable", "The configured shortcut could not be registered.");
        }
        _bar.SetFolder(_selected, _shortcut.Active);
        _bar.SetOtherFoldersAvailable(ordered.Count > 0 || hasSaved);
        PositionBar();
    }

    private async void RefreshSources(bool active)
    {
        if (_refreshing || _disposed) return;
        _refreshing = true; _refreshPolicy.Started(_autoClock.ElapsedMilliseconds);
        bool success = false;
        try
        {
            var folders = await _source.GetFoldersAsync(_lifetime.Token);
            if (!_disposed) { _folders = folders; _sourceError = null; success = true; }
        }
        catch (OperationCanceledException) { if (!_disposed) _folders = []; }
        catch (TimeoutException)
        {
            if (!_disposed)
            {
                _folders = [];
                _sourceError = Localizer.T("Explorer yanıt vermiyor. Devam ederse Klasör Geçiş'i yeniden başlatın.");
            }
        }
        catch (Exception e)
        {
            if (!_disposed)
            {
                _folders = [];
                _sourceError = Localizer.T("Explorer klasörleri okunamadı. Klasör Geçiş'i yeniden başlatın.");
            }
            Diagnostics.Write("source-read", e.GetType().Name);
        }
        finally { _refreshPolicy.Completed(active, success); _refreshing = false; if (!_disposed) QueueUpdate(); }
    }

    private void PositionBar()
    {
        if (_target is null) return;
        var bounds = CoreRules.PlaceBar(Native.Frame(_target.Handle), Native.WorkArea(_target.Handle),
            (int)Native.GetDpiForWindow(_target.Handle));
        _bar.AttachTo(_target.Handle, bounds);
    }

    private void HideBar()
    {
        if (_bar.Visible) _bar.Hide();
        UnregisterShortcut();
    }

    private void UnregisterShortcut()
    {
        _shortcut.Unregister(); _hotkeyAttempt = 0;
    }

    private void ShowOtherFolders()
    {
        if (_busy || _target is null || !_target.IsCurrent()) return;
        ResetAutomatic();
        var target = _target;
        int maximumWidth = Math.Min((int)(700 * _bar.DeviceDpi / 96d), Screen.FromControl(_bar).WorkingArea.Width - 16);
        _foldersMenu.MaximumSize = new Size(maximumWidth, Math.Max(100, Screen.FromControl(_bar).WorkingArea.Height - 16));
        FolderMenu.Populate(_foldersMenu, SourceSelection.Order(_folders, _recency, SourceSelection.IsLive), _preferences,
            _preferences.Current.HistoryEnabled ? ApplicationIdentity(target) : null, _selected, request =>
        {
            _bar.BeginInvoke(() =>
            {
                if (!_disposed && _target == target && target.IsCurrent()) Navigate(request);
            });
        }, error => { _bar.SetStatus(error, error is not null); QueueUpdate(); }, maximumWidth,
            () => _bar.BeginInvoke(() => ShowSettings()));
        if (_foldersMenu.Items.Count == 0) return;
        _choosingSource = true;
        _foldersMenu.Show(_bar.MenuPosition, ToolStripDropDownDirection.AboveRight);
    }

    private static string? ReadFilename(DialogTarget target) => target.FilenameEdit != 0 && Native.TryReadText(target.FilenameEdit, out var name) ? name : null;
    private void ResetAutomatic() { _autoPolicy.Reset(); _pendingAuto = null; }

    private static string? ApplicationIdentity(DialogTarget target)
    {
        if (Native.ProcessCreation(target.ProcessId) != target.ProcessCreated) return null;
        string? path = Native.ProcessPath(target.ProcessId);
        return Native.ProcessCreation(target.ProcessId) == target.ProcessCreated ? path : null;
    }

    private async void Navigate(FolderRequest? requested = null, AutoSwitchIntent? autoIntent = null)
    {
        if (_busy || !_enabled || (requested is null && autoIntent is null && _selected is null) || _target is null || !_target.IsCurrent()) return;
        var target = _target;
        long historyEpoch = _preferences.HistoryEpoch;
        _activeAuto = autoIntent;
        ResetAutomatic();
        if (_choosingSource) _foldersMenu.Close();
        _busy = true;
        _navigation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _bar.SetStatus(Localizer.T("Klasöre geçiliyor…"), false);
        try
        {
            if (autoIntent is not null)
            {
                var releaseClock = Stopwatch.StartNew();
                while (!Native.ModifiersReleased() || (Native.GetAsyncKeyState(1) & 0x8000) != 0)
                {
                    if (!target.IsCurrent() || releaseClock.ElapsedMilliseconds > 1200) return;
                    await Task.Delay(20, _navigation.Token);
                }
            }
            using var automaticInput = autoIntent is not null ? new InputMonitor() : null;
            if (automaticInput is not null && !automaticInput.Available) return;
            long initialInput = automaticInput?.Epoch ?? 0;
            if (autoIntent is not null)
            {
                var settleClock = Stopwatch.StartNew();
                while (DialogInteraction.TryCapture(target, ReadFilename(target)) is null)
                {
                    if (!target.IsCurrent() || settleClock.ElapsedMilliseconds > 350 || !automaticInput!.Unchanged(initialInput)) return;
                    await Task.Delay(20, _navigation.Token);
                }
            }
            // A window or tab may have changed since painting; do not reuse an earlier in-flight scan.
            string destination;
            if (requested is not null && requested.Origin != FolderOrigin.OpenExplorer)
            {
                if (!CoreRules.TryNormalizePath(requested.Path, out var savedPath))
                {
                    if (!_disposed) _bar.SetStatus(Localizer.T("Kayıtlı klasöre erişilemiyor; geçiş yapılmadı."), true);
                    return;
                }
                destination = savedPath;
            }
            else
            {
                var fresh = await _source.GetFreshFoldersAsync(_navigation.Token);
                var ordered = SourceSelection.Order(fresh, _recency, SourceSelection.IsLive);
                var currentSource = autoIntent is not null
                    ? autoIntent.Source.IsLive ? ordered.FirstOrDefault(f => f.WindowHandle == autoIntent.Source.Handle && f.ProcessId == autoIntent.Source.ProcessId) : null
                    : requested?.ExplorerSource is { } chosen ? SourceSelection.Resolve(chosen, ordered) : ordered.FirstOrDefault();
                if (currentSource is null)
                {
                    if (!_disposed) _bar.SetStatus(Localizer.T("Kaynak klasör değişti veya kapandı; tekrar seçin."), true);
                    return;
                }
                _folders = fresh; _selected = currentSource;
                _bar.SetFolder(currentSource, _shortcut.Active);
                destination = currentSource.Path;
            }
            var availability = await _availability.CheckAsync(destination, _navigation.Token);
            if (availability != FolderAvailabilityStatus.Directory)
            {
                Diagnostics.Write("folder-unavailable", "origin=" + (requested?.Origin.ToString() ?? "OpenExplorer") + "; status=" + availability);
                if (!_disposed) _bar.SetStatus(Localizer.T("Bu konum erişilebilir bir klasör değil; geçiş yapılmadı."), true);
                return;
            }
            if (autoIntent is not null && (!autoIntent.Target.Matches(target) || !autoIntent.Source.IsLive || !automaticInput!.Unchanged(initialInput) ||
                !_preferences.Current.AutoSwitchEnabled)) return;
            if (!target.IsCurrent())
            {
                if (!_disposed) _bar.SetStatus(Localizer.T("Kaynak veya dosya penceresi değişti; tekrar deneyin."), true);
                return;
            }
            if (autoIntent is not null && string.Equals(target.CurrentFolder(), destination, StringComparison.OrdinalIgnoreCase)) return;
            var result = await DialogNavigator.NavigateAsync(target, destination, _navigation.Token);
            Diagnostics.Write("navigation", result.Code + "; trigger=" + (autoIntent is not null ? "automatic" : requested?.Origin.ToString() ?? "manual") +
                "; filenamePreserved=" + result.FilenamePreserved + "; filterPreserved=" + result.FilterPreserved);
            if (!_disposed) _bar.SetStatus(result.Success && result.Interaction != InteractionRestore.Skipped ? null : result.Message,
                !result.Success, informational: result.Success);
            if (result.Success && _preferences.Current.HistoryEnabled &&
                !_preferences.RecordSuccess(result.ActualFolder ?? destination, ApplicationIdentity(target), historyEpoch, out var historyError) && !_disposed)
                _bar.SetStatus(historyError ?? Localizer.T("Klasöre geçildi; geçmiş kaydedilemedi."), false, informational: true);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed) _bar.SetStatus(null, false);
        }
        catch (TimeoutException)
        {
            if (!_disposed)
            {
                _sourceError = Localizer.T("Explorer yanıt vermiyor. Devam ederse Klasör Geçiş'i yeniden başlatın.");
                _bar.SetStatus(Localizer.T("Explorer yanıt vermiyor; geçiş yapılmadı."), true);
            }
        }
        catch (Exception e)
        {
            Diagnostics.Write("navigation-exception", e.GetType().Name);
            if (!_disposed) _bar.SetStatus(Localizer.T("Geçiş tamamlanamadı; tekrar deneyin."), true);
        }
        finally
        {
            _navigation.Dispose(); _navigation = null; _busy = false; _activeAuto = null;
            if (!_disposed) { _bar.EndProgress(); QueueUpdate(); }
        }
    }

    protected override void ExitThreadCore()
    {
        Dispose(); base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true; _lifetime.Cancel(); _navigation?.Cancel();
            Localizer.Changed -= OnLanguageChanged;
            _timer.Stop(); _timer.Dispose();
            foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
            _settingsWindow?.Dispose();
            _foldersMenu.Dispose();
            UnregisterShortcut(); _messages.Dispose(); _source.Dispose(); _availability.Dispose();
            _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); var icon = _tray.Icon; _tray.Dispose(); icon?.Dispose();
            _bar.Dispose(); _lifetime.Dispose();
            Diagnostics.Write("stopped", "");
        }
        base.Dispose(disposing);
    }

    private void RestartElevated()
    {
        if (StartupSettings.IsAdministrator()) return;
        try
        {
            // This always invokes Windows UAC; never auto-elevate at login.
            var info = new ProcessStartInfo(StartupSettings.ExecutablePath)
            {
                Verb = "runas", UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(StartupSettings.ExecutablePath)
            };
            info.ArgumentList.Add("--replace-instance");
            if (ProfilePaths.IsExplicit) { info.ArgumentList.Add("--profile-dir"); info.ArgumentList.Add(ProfilePaths.DirectoryPath); }
            using var child = Process.Start(info);
            if (child is null) throw new InvalidOperationException(Localizer.T("Yönetici süreci açılamadı."));
            Diagnostics.Write("elevation", "UAC approved; transitioning to elevated session");
            ExitThread();
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Diagnostics.Write("elevation", "UAC cancelled");
        }
        catch (Exception e)
        {
            Diagnostics.Write("elevation-error", e.Message);
            MessageBox.Show(Localizer.T("Yönetici moduna geçilemedi: ") + e.Message,
                "Klasör Geçiş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Icon MakeIcon()
    {
        try
        {
            // Pull the same multi-size icon that is embedded in the EXE resources.
            using var icon = Icon.ExtractAssociatedIcon(StartupSettings.ExecutablePath);
            if (icon is not null) return (Icon)icon.Clone();
        }
        catch (Exception e) { Diagnostics.Write("tray-icon-fallback", e.Message); }
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dark = new SolidBrush(Color.FromArgb(23, 102, 112));
        using var fill = new SolidBrush(Color.FromArgb(51, 190, 169));
        graphics.FillRectangle(dark, 3, 6, 12, 7); graphics.FillRectangle(dark, 3, 10, 26, 17);
        graphics.FillPolygon(fill, [new Point(3,12), new Point(29,12), new Point(26,27), new Point(3,27)]);
        var handle = bitmap.GetHicon();
        try { using var wrapper = Icon.FromHandle(handle); return (Icon)wrapper.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Action<int> _navigate;
        private readonly Action _changed;
        private readonly Action _update;
        internal HotkeyWindow(Action<int> navigate, Action changed, Action update)
        {
            _navigate = navigate; _changed = changed; _update = update;
            CreateHandle(new CreateParams { Caption = "KlasorGecisMessageWindow", Parent = new nint(-3) });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && m.WParam is 1 or 2) _navigate((int)m.WParam);
            if (m.Msg is 0x7E or 0x2E0) _changed();
            if (m.Msg == 0x8001) _update();
            base.WndProc(ref m);
        }
        public void Dispose() => DestroyHandle();
        internal bool PostUpdate() => Native.PostMessage(Handle, 0x8001, 0, 0);
    }
}
