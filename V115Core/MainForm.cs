using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Text;
using ToolTikTokV12.Controls;
using ToolTikTokV11.Models;
using ToolTikTokV11.Services;
using ToolTikTokV11.Utils;

namespace ToolTikTokV11;

public sealed partial class MainForm : Form
{
    readonly StartupOptions _startupOptions;
    readonly bool _managedMode;
    readonly string _baseDir;
    readonly Logger _log;
    readonly SettingsService _settingsService;
    readonly TikTokProfileService _profileService;
    readonly ChromeController _chrome;
    readonly ToolTikTokV12.Services.ChromeProfileNameSyncService _chromeProfileNameSync = new();
    readonly AutomationEngine _engine;
    readonly RuntimeStatsTracker _runtimeStats;
    AppSettings _settings;
    TikTokProfileCatalog _profileCatalog = new();
    bool _loadingProfiles;

    readonly TextBox _xp1 = new(), _xp2 = new(), _xpPeriodic = new(), _xpHover = new();
    readonly CheckBox _switchNeedsHover = new() { Text = "Nút chuyển live cần hover để hiện", AutoSize = true };
    readonly CheckBox _useArrowDown = new() { Text = "Chuyển LIVE bằng phím ↓ qua CDP (khuyên dùng)", AutoSize = true, Checked = true };
    readonly NumericUpDown _hoverDelay = Num(0, 3000);
    readonly NumericUpDown _delayMin = Num(0, 60000), _delayMax = Num(0, 60000), _loopMin = Num(0, 600000), _loopMax = Num(0, 600000);
    readonly ComboBox _periodicMin = Combo("Không dùng", "5", "10", "15", "20", "30");
    readonly NumericUpDown _timerStop = Num(0, 1440);
    readonly TextBox _contents = new() { Multiline = true, ScrollBars = ScrollBars.Both, AcceptsReturn = true, AcceptsTab = true, WordWrap = false };
    readonly Label _contentCount = new() { AutoSize = true, Text = "Số nội dung hợp lệ: 0" };
    readonly Label _chromeState = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _chromePageState = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _toggleChromeWindowBtn = new() { AutoSize = true, Height = 30, Margin = new Padding(4) };
    readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly CheckBox _strictXPath = new() { Text = "Chỉ XPath/CDP — không dùng tọa độ/chuột fallback", AutoSize = true, Checked = true };
    readonly Label _runState = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "Trạng thái: Sẵn sàng", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _runDetail = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "Bước: —", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _roundState = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "Vòng: 0", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _sessionRuntimeState = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "⏱ Phiên hiện tại: 00:00:00", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _todayRuntimeState = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "Hôm nay: 00:00:00", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _totalRuntimeState = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "Tổng thời gian chạy: 0h 00m", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _periodicState = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "↓ + F5 định kỳ: chưa chạy.", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _lastError = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.Firebrick, Text = "Lỗi: không có", TextAlign = ContentAlignment.MiddleLeft };
    readonly DataGridView _regions = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
    readonly TextBox _logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = false, Dock = DockStyle.Fill };
    readonly TextBox _errorBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
    readonly ToolTip _toolTip = new() { AutoPopDelay = 12000, InitialDelay = 300, ReshowDelay = 200, ShowAlways = true };
    readonly Button _runStopButton = new();
    readonly Button _pauseResumeButton = new();

    readonly CheckBox _viewerEnabled = new() { Text = "Bật kiểm tra người xem" };
    readonly TextBox _viewerXp = new();
    readonly NumericUpDown _viewerThreshold = Num(0, 1000000000), _viewerConfirm = Num(1, 20), _viewerInterval = Num(1, 86400), _viewerWait = Num(0, 60), _viewerMaxF5 = Num(1, 9999), _viewerRetries = Num(1, 20);
    readonly Label _viewerTest = new() { AutoSize = true, Text = "Chưa thử" };

    readonly CheckBox _oldEnabled = new() { Text = "Bật Live cũ" };
    readonly TextBox _oldXp = new(), _oldActionXp = new();
    readonly ComboBox _oldKeep = Combo("5", "10", "20", "30");
    readonly NumericUpDown _oldVariation = Num(0, 255);
    readonly Label _oldTest = new() { AutoSize = true, Text = "Chưa chụp thử" };
    readonly Label _oldDiagSummary = new() { AutoSize = true, Text = "Số ảnh Live cũ active: 0" };
    readonly Label _oldDiagCapturedAt = new() { AutoSize = true, Text = "Lần lưu Live cũ gần nhất: —" };
    readonly Label _oldDiagMatchAt = new() { AutoSize = true, Text = "Lần match gần nhất: —" };
    readonly Label _oldDiagMatch = new() { AutoSize = true, Text = "Kết quả match gần nhất: —" };
    readonly Label _oldDiagMatchImage = new() { AutoSize = true, Text = "Ảnh match gần nhất: —" };
    readonly Label _oldDiagScore = new() { AutoSize = true, Text = "Điểm sai khác / score: —" };
    readonly DataGridView _oldLiveGrid = new() { Dock = DockStyle.Top, Height = 180, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly System.Windows.Forms.Timer _periodicUiTimer = new() { Interval = 1000, Enabled = true };
    readonly System.Windows.Forms.Timer _logUiTimer = new() { Interval = 100, Enabled = true };
    readonly ConcurrentQueue<string> _pendingLogLines = new();
    string[] _oldLiveEntryIds = Array.Empty<string>();
    string _chromeStateText = "Trạng thái Chrome: ⚪ Chưa mở Chrome";
    string _chromePageStateText = "TikTok: —";
    Color _chromeStateColor = Color.DimGray;
    Color _chromePageStateColor = Color.DimGray;
    bool _chromeStatusPinned;
    bool _wasChromeConnected;
    bool _shutdownStarted;
    bool _shutdownComplete;
    bool _allowClose;
    bool _startStopCommandInFlight;
    bool _stopCommandInFlight;
    bool _pauseResumeCommandInFlight;
    int _hoveredTabIndex = -1;

    static readonly Color ActiveTabColor = Color.FromArgb(26, 83, 145);
    static readonly Color ActiveTabUnderlineColor = Color.FromArgb(113, 181, 237);
    static readonly Color InactiveTabColor = Color.FromArgb(248, 249, 251);
    static readonly Color HoveredTabColor = Color.FromArgb(226, 239, 253);
    static readonly Color TabBorderColor = Color.FromArgb(214, 220, 230);
    static readonly Color InactiveTabTextColor = Color.FromArgb(55, 65, 81);

    const int HOTKEY_START = 101, HOTKEY_PAUSE = 102, HOTKEY_STOP = 103;

    public MainForm() : this(new StartupOptions()) { }

    public MainForm(StartupOptions startupOptions)
    {
        _startupOptions = startupOptions ?? new StartupOptions();
        _managedMode = _startupOptions.ManagedMode;
        _baseDir = _managedMode
            ? (string.IsNullOrWhiteSpace(_startupOptions.DataRoot)
                ? Path.Combine(AppContext.BaseDirectory, "profiles", _startupOptions.ProfileName)
                : Path.GetFullPath(_startupOptions.DataRoot))
            : RuntimeDataPath.Resolve(AppContext.BaseDirectory);
        Directory.CreateDirectory(_baseDir);

        var ctorSw = System.Diagnostics.Stopwatch.StartNew();
        Text = _managedMode
            ? $"Tool TikTok V12.5 — V11.5 Worker — {_startupOptions.ProfileName}"
            : "Tool TikTok V11.5 — CDP ArrowDown / XPath";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        Width = 930; Height = 700; MinimumSize = new Size(760, 580); StartPosition = FormStartPosition.CenterScreen;
        if (_startupOptions.Embedded)
        {
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
        }
        BackColor = SystemColors.Control;
        _log = new Logger(_baseDir); _settingsService = new SettingsService(_baseDir); _profileService = new TikTokProfileService(_baseDir);
        var settingsSw = System.Diagnostics.Stopwatch.StartNew();
        _settings = _settingsService.Load();
        settingsSw.Stop();
        _chrome = new ChromeController(_log); _engine = new AutomationEngine(_baseDir, _chrome, _log); _runtimeStats = new RuntimeStatsTracker(_baseDir, _log);
        var profileSw = System.Diagnostics.Stopwatch.StartNew();
        if (_managedMode)
        {
            _profileCatalog = new TikTokProfileCatalog
            {
                SelectedProfile = _startupOptions.ProfileName,
                Profiles =
                [
                    new TikTokProfileEntry
                    {
                        Name = _startupOptions.ProfileName,
                        ProfilePath = Path.GetFullPath(_startupOptions.ProfilePath),
                        Managed = false
                    }
                ]
            };
        }
        else
        {
            _profileCatalog = _profileService.Load();
        }
        profileSw.Stop();
        ApplyManagedStartupOverrides();
        ApplySelectedProfileToSettings(logSelection: false);
        _log.Info($"[PERF] Settings load: {settingsSw.ElapsedMilliseconds} ms");
        _log.Info($"[PERF] Profile load: {profileSw.ElapsedMilliseconds} ms");
        _log.Info($"BaseDirectory={AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)}");
        _log.Info($"StartupPath={Application.StartupPath}");
        _log.Info($"WorkingDirectory={Environment.CurrentDirectory}");
        _log.Info($"DataRoot={_baseDir}");
        _log.Info($"ConfigPath={_settingsService.IniPath}");
        _log.Info($"ContentPath={_settingsService.ContentPath}");
        _log.Info($"SelectedProfile={CurrentProfileName}");
        _log.Info($"ProfilePath={CurrentProfilePath}");
        _log.Info($"CDPPort={_settings.ChromePort}");
        _log.Info($"ImageDirectory={Path.Combine(_baseDir, "anh_mau_chup")}");
        InitOldLiveGrid();
        BuildUi();
        InitializeContentEditor();
        LoadToUi();
        if (_managedMode)
        {
            _profileCombo.Enabled = false;
            _toolTip.SetToolTip(_profileCombo, "Profile này được Manager V12.5 khóa cố định cho worker hiện tại.");
        }
        _log.LineWritten += OnLog; _engine.Status += OnEngineStatus; _engine.Problem += OnEngineProblem; _engine.RunStateChanged += OnEngineRunStateChanged; _engine.StateChanged += OnEngineState;
        Application.ApplicationExit += OnApplicationExit;
        _periodicUiTimer.Tick += (_, _) => RefreshUiStatusLabels();
        _logUiTimer.Tick += (_, _) => FlushPendingLogLines();
        Shown += (_, _) =>
        {
            _log.Info($"[PERF] Form shown: {ctorSw.ElapsedMilliseconds} ms");
            // V12.5 runs many V11.5 workers in parallel. Global F8/F9/Esc would collide
            // across processes, so managed workers use their own UI/Manager commands only.
            if (!_managedMode) RegisterGlobalHotkeys();
        };
        FormClosing += OnClosing;
        RefreshUiStatusLabels();
        ctorSw.Stop();
        _log.Info($"[PERF] MainForm constructor: {ctorSw.ElapsedMilliseconds} ms");
    }

    static NumericUpDown Num(decimal min, decimal max) => new() { Minimum = min, Maximum = max, Width = 90 };
    static ComboBox Combo(params string[] values) { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 }; c.Items.AddRange(values); if (values.Length > 0) c.SelectedIndex = 0; return c; }
    void InitOldLiveGrid()
    {
        _oldLiveGrid.Columns.Clear();
        _oldLiveGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Image", HeaderText = "Ảnh" });
        _oldLiveGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Age", HeaderText = "Tuổi" });
        _oldLiveGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Remaining", HeaderText = "Còn hiệu lực" });
    }

    void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5), DrawMode = TabDrawMode.OwnerDrawFixed };
        ConfigureTabAppearance(tabs);
        tabs.TabPages.Add(BuildGeneralTab()); tabs.TabPages.Add(BuildRegionsTab()); tabs.TabPages.Add(BuildViewerTab()); tabs.TabPages.Add(BuildOldLiveTab()); tabs.TabPages.Add(BuildDiagnosticsTab()); tabs.TabPages.Add(BuildLogTab());
        Controls.Add(tabs);
        // Khu vực chạy được tách nhiều dòng để không kéo dài giao diện theo chiều ngang.
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 224, ColumnCount = 2, RowCount = 1, Padding = new Padding(6) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
        ConfigureRunControlButton(_runStopButton, async (_, _) => await HandleStartStopAsync());
        ConfigureRunControlButton(_pauseResumeButton, (_, _) => HandlePauseResume());
        var save = Btn("💾 Lưu", (_, _) => SaveFromUi());
        var export = Btn("Xuất", (_, _) => ExportConfig());
        var import = Btn("Nhập", (_, _) => ImportConfig());
        actions.Controls.AddRange([_runStopButton, _pauseResumeButton, save, export, import]);

        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = new Padding(10, 0, 0, 0) };
        for (var row = 0; row < 8; row++) status.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5F));
        status.Controls.Add(_runState, 0, 0);
        status.Controls.Add(_runDetail, 0, 1);
        status.Controls.Add(_roundState, 0, 2);
        status.Controls.Add(_sessionRuntimeState, 0, 3);
        status.Controls.Add(_todayRuntimeState, 0, 4);
        status.Controls.Add(_totalRuntimeState, 0, 5);
        status.Controls.Add(_periodicState, 0, 6);
        status.Controls.Add(_lastError, 0, 7);
        bottom.Controls.Add(actions, 0, 0);
        bottom.Controls.Add(status, 1, 0);
        Controls.Add(bottom);
        ApplyStatusStyles();
        UpdateRunControlButtons();
    }

    void ConfigureTabAppearance(TabControl tabs)
    {
        tabs.DrawItem += (_, e) => DrawTab(tabs, e);
        tabs.SelectedIndexChanged += (_, _) => tabs.Invalidate();
        tabs.MouseMove += (_, e) => UpdateHoveredTab(tabs, e.Location);
        tabs.MouseLeave += (_, _) => SetHoveredTab(tabs, -1);

        // When the pointer enters the page content, clear the header hover state.
        tabs.ControlAdded += (_, e) =>
        {
            if (e.Control is TabPage page)
                page.MouseMove += (_, _) => SetHoveredTab(tabs, -1);
        };
    }

    void DrawTab(TabControl tabs, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;

        var active = e.Index == tabs.SelectedIndex;
        var hovered = !active && e.Index == _hoveredTabIndex;
        var background = active ? ActiveTabColor : hovered ? HoveredTabColor : InactiveTabColor;
        var foreground = active ? Color.White : InactiveTabTextColor;

        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        using (var borderPen = new Pen(active ? ActiveTabColor : TabBorderColor))
            e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        if (active)
        {
            var underlineHeight = Math.Min(3, e.Bounds.Height);
            using var underlineBrush = new SolidBrush(ActiveTabUnderlineColor);
            e.Graphics.FillRectangle(underlineBrush, e.Bounds.X, e.Bounds.Bottom - underlineHeight, e.Bounds.Width, underlineHeight);
        }

        using Font? activeFont = active ? new Font(tabs.Font, FontStyle.Bold) : null;
        var textBounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 2, e.Bounds.Width - 12, e.Bounds.Height - 4);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, activeFont ?? tabs.Font, textBounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    void UpdateHoveredTab(TabControl tabs, Point location)
    {
        var hoveredIndex = -1;
        for (var i = 0; i < tabs.TabCount; i++)
        {
            if (!tabs.GetTabRect(i).Contains(location)) continue;
            hoveredIndex = i;
            break;
        }
        SetHoveredTab(tabs, hoveredIndex);
    }

    void SetHoveredTab(TabControl tabs, int hoveredIndex)
    {
        if (_hoveredTabIndex == hoveredIndex) return;
        _hoveredTabIndex = hoveredIndex;
        tabs.Invalidate();
    }

    void InitializeContentEditor()
    {
        _contents.TextChanged += (_, _) => UpdateContentCount();
        _contents.KeyDown += OnContentsKeyDown;
        UpdateContentCount();
    }

    void ApplyStatusStyles()
    {
        _runState.Font = new Font(Font, FontStyle.Bold);
        _runState.ForeColor = Color.FromArgb(32, 98, 55);
        _runDetail.ForeColor = Color.FromArgb(34, 93, 168);
        _roundState.ForeColor = Color.FromArgb(78, 78, 78);
        _sessionRuntimeState.ForeColor = Color.FromArgb(28, 101, 153);
        _todayRuntimeState.ForeColor = Color.FromArgb(47, 96, 72);
        _totalRuntimeState.ForeColor = Color.FromArgb(77, 80, 91);
        UpdateRunControlButtons();
        _periodicState.ForeColor = Color.FromArgb(86, 76, 170);
        _lastError.ForeColor = Color.FromArgb(88, 88, 88);
        _chromeState.ForeColor = Color.DimGray;
        _chromePageState.ForeColor = Color.DimGray;
        _toggleChromeWindowBtn.Click += (_, _) => ToggleChromeWindow();
    }

    void RefreshUiStatusLabels()
    {
        UpdateRunControlButtons();
        RefreshRuntimeStatsLabels();
        RefreshPeriodicCountdownLabel();
        RefreshChromeStatus();
        RefreshOldLiveDiagnostics();
    }

    void RefreshRuntimeStatsLabels()
    {
        var snapshot = _runtimeStats.GetSnapshot();
        SetTextIfChanged(_sessionRuntimeState, "⏱ Phiên hiện tại: " + FormatRuntimeClock(snapshot.Session));
        SetTextIfChanged(_todayRuntimeState, "Hôm nay: " + FormatRuntimeClock(snapshot.Today));
        SetTextIfChanged(_totalRuntimeState, "Tổng thời gian chạy: " + FormatRuntimeTotal(snapshot.Total));
    }

    void ConfigureRunControlButton(Button button, EventHandler click)
    {
        button.AutoSize = false;
        button.Size = new Size(172, 42);
        button.Margin = new Padding(4, 4, 6, 4);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Resize += (_, _) => ApplyRoundedButtonRegion(button, 8);
        ApplyRoundedButtonRegion(button, 8);
        button.Click += click;
    }

    static void ApplyRoundedButtonRegion(Button button, int radius)
    {
        if (button.Width <= 0 || button.Height <= 0) return;
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(button.Width, button.Height)));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(button.Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(button.Width - diameter, button.Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, button.Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var oldRegion = button.Region;
        button.Region = new Region(path);
        oldRegion?.Dispose();
    }

    void UpdateRunControlButtons()
    {
        if (IsDisposed || Disposing) return;

        if (_startStopCommandInFlight)
        {
            var stopping = _stopCommandInFlight;
            SetRunControlAppearance(_runStopButton, stopping ? "■  Đang dừng..." : "▶  Đang bắt đầu...", Color.FromArgb(138, 145, 153), false);
            SetRunControlAppearance(_pauseResumeButton, "⏸  Tạm dừng", Color.FromArgb(138, 145, 153), false);
            _toolTip.SetToolTip(_runStopButton, stopping ? "Đang dừng an toàn; vui lòng chờ." : "Đang kiểm tra cấu hình và kết nối trước khi bắt đầu.");
            _toolTip.SetToolTip(_pauseResumeButton, "Không thể tạm dừng khi tool đang chuyển trạng thái.");
            return;
        }

        if (!_engine.Running)
        {
            SetRunControlAppearance(_runStopButton, "▶  Bắt đầu", Color.FromArgb(35, 137, 73), true);
            SetRunControlAppearance(_pauseResumeButton, "⏸  Tạm dừng", Color.FromArgb(138, 145, 153), false);
            _toolTip.SetToolTip(_runStopButton, "Bắt đầu automation (F8). Esc vẫn dừng an toàn khi tool đang chạy.");
            _toolTip.SetToolTip(_pauseResumeButton, "Nút tạm dừng chỉ hoạt động khi tool đang chạy (F9).");
            return;
        }

        if (_pauseResumeCommandInFlight)
        {
            SetRunControlAppearance(_runStopButton, "■  Dừng", Color.FromArgb(196, 58, 58), false);
            SetRunControlAppearance(_pauseResumeButton, "⏳  Đang đổi trạng thái...", Color.FromArgb(138, 145, 153), false);
            return;
        }

        if (_engine.Paused)
        {
            SetRunControlAppearance(_runStopButton, "■  Dừng", Color.FromArgb(196, 58, 58), true);
            SetRunControlAppearance(_pauseResumeButton, "▶  Tiếp tục", Color.FromArgb(35, 111, 186), true);
            _toolTip.SetToolTip(_runStopButton, "Dừng hẳn automation (Esc).");
            _toolTip.SetToolTip(_pauseResumeButton, "Tiếp tục automation đã tạm dừng (F9).");
            return;
        }

        SetRunControlAppearance(_runStopButton, "■  Dừng", Color.FromArgb(196, 58, 58), true);
        SetRunControlAppearance(_pauseResumeButton, "⏸  Tạm dừng", Color.FromArgb(222, 142, 31), true);
        _toolTip.SetToolTip(_runStopButton, "Dừng hẳn automation (Esc).");
        _toolTip.SetToolTip(_pauseResumeButton, "Tạm dừng automation (F9).");
    }

    static void SetRunControlAppearance(Button button, string text, Color color, bool enabled)
    {
        button.Text = text;
        button.Enabled = enabled;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = color;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color);
    }

    void SetChromeStatus(string chromeText, Color chromeColor, string tikTokText = "TikTok: —", Color? tikTokColor = null, bool pinUntilChange = true)
    {
        _chromeStateText = chromeText;
        _chromePageStateText = tikTokText;
        _chromeStateColor = chromeColor;
        _chromePageStateColor = tikTokColor ?? chromeColor;
        _chromeStatusPinned = pinUntilChange;
        RenderChromeStatus();
    }

    void RenderChromeStatus()
    {
        _chromeState.Text = _chromeStateText;
        _chromeState.ForeColor = _chromeStateColor;
        _chromePageState.Text = _chromePageStateText;
        _chromePageState.ForeColor = _chromePageStateColor;
    }

    void RefreshChromeStatus()
    {
        if (InvokeRequired) { BeginInvoke(new Action(RefreshChromeStatus)); return; }

        if (_chrome.Connected)
        {
            _chromeStatusPinned = false;
            _wasChromeConnected = true;
            var windowState = _chrome.GetManagedWindowState(CurrentProfilePath, _settings.ChromePort);
            var windowText = windowState == ChromeWindowState.Minimized ? "ĐÃ THU NHỎ" : "ĐANG HIỂN THỊ";
            _chromeStateText = $"Trạng thái Chrome: 🟢 ĐÃ KẾT NỐI — {windowText} — profile: {CurrentProfileName}";
            _chromePageStateText = string.IsNullOrWhiteSpace(_chrome.Page?.Title)
                ? "TikTok: ● ĐÃ KẾT NỐI"
                : $"TikTok: ● ĐÃ KẾT NỐI — {_chrome.Page?.Title}";
            _chromeStateColor = Color.FromArgb(31, 122, 68);
            _chromePageStateColor = Color.FromArgb(45, 121, 89);
        }
        else if (_chromeStatusPinned)
        {
            if (_wasChromeConnected && _chromeStateText.Contains("Đang", StringComparison.OrdinalIgnoreCase))
            {
                _chromeStateText = "Trạng thái Chrome: 🔴 Mất kết nối CDP";
                _chromePageStateText = "TikTok: —";
                _chromeStateColor = Color.Firebrick;
                _chromePageStateColor = Color.DimGray;
            }
        }
        else if (_wasChromeConnected)
        {
            _chromeStateText = "Trạng thái Chrome: 🔴 Mất kết nối CDP";
            _chromePageStateText = "TikTok: —";
            _chromeStateColor = Color.Firebrick;
            _chromePageStateColor = Color.DimGray;
        }
        else
        {
            _chromeStateText = "Trạng thái Chrome: ⚪ Chưa mở Chrome";
            _chromePageStateText = "TikTok: —";
            _chromeStateColor = Color.DimGray;
            _chromePageStateColor = Color.DimGray;
        }

        UpdateChromeWindowButtonText();
        RenderChromeStatus();
    }

    void UpdateChromeWindowButtonText()
    {
        var windowState = string.IsNullOrWhiteSpace(CurrentProfilePath)
            ? ChromeWindowState.NotFound
            : _chrome.GetManagedWindowState(CurrentProfilePath, _settings.ChromePort);
        _toggleChromeWindowBtn.Text = windowState == ChromeWindowState.Minimized ? "Khôi phục Chrome" : "Thu nhỏ Chrome";
        _toggleChromeWindowBtn.Enabled = windowState != ChromeWindowState.NotFound;
    }

    TabPage BuildGeneralTab()
    {
        var tab = new TabPage("Điều khiển / XPath");
        var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 8, 8, 0) };
        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.Controls.Add(BuildChromeGroup(), 0, 0);
        contentLayout.Controls.Add(BuildXPathGroup(), 0, 1);
        contentLayout.Controls.Add(BuildTimingGroup(), 0, 2);

        var contentGroup = new GroupBox
        {
            Text = "Danh sách nội dung — giữ nguyên cơ chế mỗi dòng = một nội dung",
            Dock = DockStyle.Top,
            Height = 240,
            MinimumSize = new Size(0, 220),
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 8)
        };
        var contentPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _contentCount.Margin = new Padding(0, 0, 0, 6);
        _contents.Dock = DockStyle.Fill;
        contentPanel.Controls.Add(_contentCount, 0, 0);
        contentPanel.Controls.Add(_contents, 0, 1);
        contentGroup.Controls.Add(contentPanel);
        contentLayout.Controls.Add(contentGroup, 0, 3);

        scrollPanel.Controls.Add(contentLayout);
        BindScrollContentWidth(scrollPanel, contentLayout);
        tab.Controls.Add(scrollPanel);
        return tab;
    }

    void BindScrollContentWidth(Panel scrollPanel, Control content)
    {
        void UpdateWidth()
        {
            if (scrollPanel.IsDisposed || content.IsDisposed) return;
            int width = scrollPanel.ClientSize.Width - scrollPanel.Padding.Horizontal - 1;
            if (scrollPanel.VerticalScroll.Visible) width -= SystemInformation.VerticalScrollBarWidth;
            width = Math.Max(320, width);
            if (content.Width != width) content.Width = width;
            content.MaximumSize = new Size(width, 0);
        }

        scrollPanel.Resize += (_, _) => UpdateWidth();
        scrollPanel.Layout += (_, _) => UpdateWidth();
        scrollPanel.HandleCreated += (_, _) => UpdateWidth();
    }

    Control BuildChromeGroup()
    {
        var g = new GroupBox { Text = "Chrome V11.5 (CDP)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        row1.Controls.Add(Btn("Mở Chrome", async (_, _) => await LaunchChromeAsync()));
        row1.Controls.Add(_toggleChromeWindowBtn);
        row1.Controls.Add(Btn("Kết nối TikTok", async (_, _) => await ConnectChromeAsync()));
        row1.Controls.Add(Btn("Mở thư mục profile", (_, _) => OpenSelectedProfileFolder()));

        var rowProfile = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        AddLabeled(rowProfile, "Profile TikTok", _profileCombo);
        rowProfile.Controls.Add(Btn("Thêm", (_, _) => AddProfile()));
        rowProfile.Controls.Add(Btn("Thêm profile có sẵn", (_, _) => AddExistingProfile()));
        rowProfile.Controls.Add(Btn("Mở thư mục", (_, _) => OpenSelectedProfileFolder()));
        rowProfile.Controls.Add(Btn("Xóa", (_, _) => DeleteProfile()));

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        row2.Controls.Add(_strictXPath);

        _chromeState.Height = 24;
        _chromeState.Margin = new Padding(0, 0, 0, 2);
        _chromePageState.Height = 24;
        _chromePageState.Margin = new Padding(0, 0, 0, 0);

        root.Controls.Add(row1, 0, 0);
        root.Controls.Add(rowProfile, 0, 1);
        root.Controls.Add(row2, 0, 2);
        root.Controls.Add(_chromeState, 0, 3);
        root.Controls.Add(_chromePageState, 0, 4);
        g.Controls.Add(root); return g;
    }

    Control BuildXPathGroup()
    {
        var g = new GroupBox { Text = "XPath thao tác chính — giao diện gọn, XPath đầy đủ chỉ xem khi cần", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        var downWrap = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 4) };
        downWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        downWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var downFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 2) };
        downFlow.Controls.Add(_useArrowDown);
        downFlow.Controls.Add(Btn("Thử ↓ CDP", async (_, _) => await TestArrowDownAsync()));
        var downText = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(780, 0),
            Margin = new Padding(0),
            Text = "F5 định kỳ, Live cũ và CLICK_F5 dùng ArrowDown CDP → chờ → F5.",
            TextAlign = ContentAlignment.MiddleLeft
        };
        downWrap.Controls.Add(downFlow, 0, 0);
        downWrap.Controls.Add(downText, 0, 1);
        t.Controls.Add(downWrap, 0, 0); t.SetColumnSpan(downWrap, 5);

        AddXPathRow(t, 1, "Điểm/ô nhập 1", _xp1, () => PickIntoAsync(_xp1), () => TestXPathAsync(_xp1, false), "Thử");
        AddXPathRow(t, 2, "Điểm/ô nhập 2", _xp2, () => PickIntoAsync(_xp2), () => TestXPathAsync(_xp2, false), "Thử");
        AddXPathRow(t, 3, "Nút LIVE (dự phòng)", _xpPeriodic, () => PickIntoAsync(_xpPeriodic, true), () => TestSwitchXPathAsync(), "Thử chuyển");
        AddXPathRow(t, 4, "Vùng hover (dự phòng)", _xpHover, () => PickIntoAsync(_xpHover), () => TestHoverXPathAsync(), "Thử hover");
        _xpPeriodic.TextChanged += (_, _) => RefreshAllRegionStatus();
        _useArrowDown.CheckedChanged += (_, _) => RefreshAllRegionStatus();

        var hoverFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 2) };
        hoverFlow.Controls.Add(_switchNeedsHover); AddLabeled(hoverFlow, "Chờ hover ms", _hoverDelay);
        t.Controls.Add(hoverFlow, 0, 5); t.SetColumnSpan(hoverFlow, 5);

        g.Controls.Add(t); return g;
    }

    void AddXPathRow(TableLayoutPanel t, int row, string name, TextBox box, Func<Task> pick, Func<Task> test, string testText)
    {
        var status = new Label { AutoSize = false, Width = 82, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        void Refresh()
        {
            status.Text = string.IsNullOrWhiteSpace(box.Text) ? "— Thiếu" : "✓ Đã có";
            status.ForeColor = string.IsNullOrWhiteSpace(box.Text) ? Color.Firebrick : Color.DarkGreen;
        }
        box.TextChanged += (_, _) => Refresh(); Refresh();
        t.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        t.Controls.Add(status, 1, row);
        t.Controls.Add(Btn("Lấy XPath", async (_, _) => await pick()), 2, row);
        t.Controls.Add(Btn(testText, async (_, _) => await test()), 3, row);
        t.Controls.Add(Btn("Xem/Sửa", (_, _) => ShowXPathEditor(name, box)), 4, row);
    }

    void ShowXPathEditor(string name, TextBox source)
    {
        using var f = new Form { Text = "XPath — " + name, Width = 720, Height = 230, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        ModernDialog.Apply(f);
        var edit = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = source.Text, Font = new Font("Consolas", 9F) };
        edit.BackColor = Color.White;
        edit.BorderStyle = BorderStyle.FixedSingle;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        var ok = new Button { Text = "Lưu XPath", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        ModernDialog.StylePrimaryButton(ok);
        ModernDialog.StyleSecondaryButton(cancel);
        bar.Controls.Add(ok); bar.Controls.Add(cancel); f.Controls.Add(edit); f.Controls.Add(bar); f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) == DialogResult.OK) source.Text = edit.Text.Trim();
    }

    Control BuildTimingGroup()
    {
        var g = new GroupBox { Text = "Khoảng chờ ngẫu nhiên", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 8, 10, 10) };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var timingTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            RowCount = 3,
            Margin = new Padding(0)
        };
        timingTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timingTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timingTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timingTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timingTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblHeaderFrom = new Label { Text = "Từ", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 0, 6, 4) };
        var lblHeaderTo = new Label { Text = "đến", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 0, 6, 4) };
        var lblDelay = new Label { Text = "Giữa thao tác (mili giây):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        var lblLoop = new Label { Text = "Sau mỗi vòng (mili giây):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        var lblDelayFrom = new Label { Text = "Từ", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 4, 6) };
        var lblDelayTo = new Label { Text = "đến", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 4, 6) };
        var lblLoopFrom = new Label { Text = "Từ", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 4, 6) };
        var lblLoopTo = new Label { Text = "đến", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 4, 6) };

        timingTable.Controls.Add(new Label { AutoSize = true, Text = "", Margin = new Padding(0) }, 0, 0);
        timingTable.Controls.Add(lblHeaderFrom, 1, 0);
        timingTable.Controls.Add(new Label { AutoSize = true, Text = "", Margin = new Padding(0) }, 2, 0);
        timingTable.Controls.Add(lblHeaderTo, 3, 0);
        timingTable.Controls.Add(new Label { AutoSize = true, Text = "", Margin = new Padding(0) }, 4, 0);

        timingTable.Controls.Add(lblDelay, 0, 1);
        timingTable.Controls.Add(lblDelayFrom, 1, 1);
        timingTable.Controls.Add(_delayMin, 2, 1);
        timingTable.Controls.Add(lblDelayTo, 3, 1);
        timingTable.Controls.Add(_delayMax, 4, 1);

        timingTable.Controls.Add(lblLoop, 0, 2);
        timingTable.Controls.Add(lblLoopFrom, 1, 2);
        timingTable.Controls.Add(_loopMin, 2, 2);
        timingTable.Controls.Add(lblLoopTo, 3, 2);
        timingTable.Controls.Add(_loopMax, 4, 2);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        AddLabeled(options, "Bấm + F5 lại sau (phút)", _periodicMin);
        AddLabeled(options, "Hẹn dừng (phút)", _timerStop);

        var delayTip = "Tool chọn ngẫu nhiên một khoảng chờ giữa giá trị Từ và đến giữa các thao tác trong một vòng.";
        var loopTip = "Tool chọn ngẫu nhiên khoảng nghỉ sau khi hoàn thành một vòng.";
        foreach (var control in new Control[] { lblDelay, lblDelayFrom, lblDelayTo, _delayMin, _delayMax })
            _toolTip.SetToolTip(control, delayTip);
        foreach (var control in new Control[] { lblLoop, lblLoopFrom, lblLoopTo, _loopMin, _loopMax })
            _toolTip.SetToolTip(control, loopTip);

        root.Controls.Add(timingTable, 0, 0);
        root.Controls.Add(options, 0, 1);
        g.Controls.Add(root);
        return g;
    }

    TabPage BuildRegionsTab()
    {
        var tab = new TabPage("Vùng quét ảnh"); ConfigureRegionGrid();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        top.Controls.Add(Btn("+ Thêm dòng", (_, _) => AddRegionRow()));
        top.Controls.Add(Btn("Xóa dòng", (_, _) => DeleteSelectedRegionRow()));
        top.Controls.Add(Btn("Lấy XPath vùng quét", async (_, _) => await PickRegionCellAsync("ScanXPath")));
        top.Controls.Add(Btn("Lấy XPath nút bấm", async (_, _) => await PickRegionCellAsync("ActionXPath")));
        top.Controls.Add(Btn("Quét thử dòng chọn", async (_, _) => await TestSelectedRegionAsync()));
        top.Controls.Add(Btn("Quét thử tất cả", async (_, _) => await TestAllRegionsAsync()));
        top.Controls.Add(Btn("Chụp XPath làm ảnh mẫu", async (_, _) => await CaptureSelectedRegionTemplateAsync()));
        top.Controls.Add(Btn("Thêm ảnh từ file...", (_, _) => AddImagesToSelectedRegion()));
        top.Controls.Add(Btn("Xem ảnh mẫu", (_, _) => { if (_regions.CurrentRow is not null) ViewImagesForRow(_regions.CurrentRow); }));
        top.Controls.Add(Btn("Xóa ảnh mẫu", (_, _) => { if (_regions.CurrentRow is not null) DeleteImagesFromRow(_regions.CurrentRow); }));
        top.Controls.Add(Btn("Xem chi tiết dòng", (_, _) => { if (_regions.CurrentRow is not null) ShowRegionDetails(_regions.CurrentRow); }));
        top.Controls.Add(new Label { AutoSize = true, Margin = new Padding(8, 10, 0, 0), Text = "Quét thường chạy trước Click 1/2; cột định kỳ vẫn giữ riêng cho vùng ưu tiên. XP chỉ hiện trạng thái." });
        tab.Controls.Add(_regions); tab.Controls.Add(top); return tab;
    }

    void ConfigureRegionGrid()
    {
        _regions.Columns.Clear(); _regions.RowHeadersWidth = 26;
        _regions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _regions.MultiSelect = false;
        _regions.RowTemplate.Height = 27;
        _regions.BackgroundColor = SystemColors.Window;
        _regions.BorderStyle = BorderStyle.FixedSingle;
        _regions.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
        _regions.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                (_regions.Columns[e.ColumnIndex].Name is "ScanXPathState" or "ActionXPathState" or "ImagesState"))
                ShowRegionDetails(_regions.Rows[e.RowIndex]);
        };
        _regions.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_regions.IsCurrentCellDirty) _regions.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _regions.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _regions.Columns[e.ColumnIndex].Name is "Action" or "ScanXPath" or "ActionXPath" or "Images")
                UpdateRegionStatus(_regions.Rows[e.RowIndex]);
        };

        // Chỉ giữ các cột cần để vận hành/cấu hình. Các nút Test/Xem/Xóa ảnh
        // đã được đưa lên thanh công cụ nên không lặp lại trên từng dòng.
        _regions.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Bật", Width = 38 });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Tên vùng", Width = 160 });
        _regions.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Periodic", HeaderText = "Định kỳ", Width = 58 });
        _regions.Columns.Add(new DataGridViewComboBoxColumn { Name = "PeriodMin", HeaderText = "Chu kỳ", Width = 62, DataSource = new[] { 10, 30, 60, 120, 240, 480 } });
        _regions.Columns.Add(new DataGridViewComboBoxColumn { Name = "Action", HeaderText = "Hành động", Width = 92, DataSource = new[] { "F5", "CLICK_F5", "STOP" } });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Variation", HeaderText = "Sai số", Width = 54 });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Consecutive", HeaderText = "Lỗi LT", Width = 54 });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ScanXPathState", HeaderText = "XP quét", Width = 72, ReadOnly = true });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ActionXPathState", HeaderText = "XP bấm", Width = 72, ReadOnly = true });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ImagesState", HeaderText = "Ảnh mẫu", Width = 76, ReadOnly = true });

        // Dữ liệu tương thích ngược vẫn được giữ ẩn. AfterClick không còn được runtime
        // dùng từ V12.5 pre-click scan, nhưng giữ giá trị để không phá file cấu hình cũ.
        _regions.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AfterClick", HeaderText = "Sau click", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ScanXPath", HeaderText = "XPath vùng quét", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "ActionXPath", HeaderText = "XPath nút bấm", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Images", HeaderText = "Ảnh mẫu (|)", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "RX1", HeaderText = "RX1", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "RY1", HeaderText = "RY1", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "RX2", HeaderText = "RX2", Visible = false });
        _regions.Columns.Add(new DataGridViewTextBoxColumn { Name = "RY2", HeaderText = "RY2", Visible = false });
    }

    TabPage BuildViewerTab()
    {
        var tab = new TabPage("Người xem"); var p = VerticalPanel();
        p.Controls.Add(_viewerEnabled); p.Controls.Add(XPathLine("XPath số người xem", _viewerXp, () => PickIntoAsync(_viewerXp), async () => await TestViewerAsync()));
        var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        AddLabeled(flow, "Ngưỡng", _viewerThreshold); AddLabeled(flow, "Xác nhận thấp", _viewerConfirm); AddLabeled(flow, "Chu kỳ giây", _viewerInterval);
        AddLabeled(flow, "Chờ sau F5 giây", _viewerWait); AddLabeled(flow, "Max ↓+F5", _viewerMaxF5); AddLabeled(flow, "Số lần đọc lại", _viewerRetries);
        p.Controls.Add(flow);
        var testViewerFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        testViewerFlow.Controls.Add(Btn("Đọc thử người xem", async (_, _) => await TestViewerAsync())); testViewerFlow.Controls.Add(_viewerTest); p.Controls.Add(testViewerFlow);
        p.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(980, 0), Text = "Ưu tiên đọc text trực tiếp từ XPath. Parser hiểu 4.3K=4300, 15.6K=15600, 2.5M=2500000. Khi bật Chỉ XPath, thiếu/không tìm thấy XPath sẽ báo rõ lỗi Người xem và bỏ qua, không dùng OCR tọa độ fallback." });
        tab.Controls.Add(p); return tab;
    }

    TabPage BuildOldLiveTab()
    {
        var tab = new TabPage("Live cũ"); var p = VerticalPanel(); p.Controls.Add(_oldEnabled);
        p.Controls.Add(XPathLine("XPath vùng nhận dạng", _oldXp, () => PickIntoAsync(_oldXp), () => TestXPathAsync(_oldXp, false)));
        p.Controls.Add(XPathLine("XPath nút chuyển live", _oldActionXp, () => PickIntoAsync(_oldActionXp, true), () => TestXPathAsync(_oldActionXp, false), true));
        var f = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        AddLabeled(f, "Giữ ảnh phút", _oldKeep); AddLabeled(f, "Sai số", _oldVariation);
        f.Controls.Add(Btn("Chụp mẫu test", async (_, _) => await CaptureOldTestAsync()));
        f.Controls.Add(Btn("Quét thử Live cũ", async (_, _) => await TestOldLiveMatchAsync()));
        f.Controls.Add(Btn("Xóa ảnh Live cũ active", (_, _) => ClearOldLivesManually()));
        f.Controls.Add(_oldTest); p.Controls.Add(f);
        p.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(980, 0), Text = "Test: đứng ở live A → Chụp mẫu test → Quét thử phải KHỚP; sang live B → Quét thử nên KHÔNG KHỚP. Logic chạy thật: tại mốc T-10s sẽ chụp thành công là thêm ngay vào danh sách Live cũ active. Ảnh active giữ nguyên qua Pause/Dừng/Start và chỉ tự xóa khi hết TTL; nút xóa chỉ xóa thủ công ảnh active, ảnh test vẫn tách biệt." });
        p.Controls.Add(_oldDiagSummary);
        p.Controls.Add(_oldDiagCapturedAt);
        p.Controls.Add(_oldDiagMatchAt);
        p.Controls.Add(_oldDiagMatch);
        p.Controls.Add(_oldDiagMatchImage);
        p.Controls.Add(_oldDiagScore);
        p.Controls.Add(_oldLiveGrid);
        tab.Controls.Add(p); return tab;
    }

    TabPage BuildDiagnosticsTab()
    {
        var t = new TabPage("Lỗi / chẩn đoán");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        top.Controls.Add(Btn("Kiểm tra toàn bộ XPath", async (_, _) => await CheckConfiguredXpathsAsync()));
        top.Controls.Add(Btn("Xóa danh sách lỗi", (_, _) => { _errorBox.Clear(); SetLastErrorText("Lỗi: không có", false); }));
        top.Controls.Add(new Label { AutoSize = true, Margin = new Padding(10, 10, 0, 0), Text = "Lỗi runtime ghi rõ vùng/XPath/thao tác. Lỗi lặp giống nhau được giới hạn tần suất để không spam log." });
        t.Controls.Add(_errorBox); t.Controls.Add(top); return t;
    }

    TabPage BuildLogTab() { var t = new TabPage("Nhật ký"); t.Controls.Add(_logBox); return t; }

    static FlowLayoutPanel VerticalPanel() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
    static Button Btn(string text, EventHandler handler) { var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(4) }; b.Click += handler; return b; }
    static Label Spacer(int w) => new() { Width = w, Height = 1 };
    static void AddLabeled(FlowLayoutPanel p, string text, Control c) { p.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(8, 8, 2, 0) }); p.Controls.Add(c); }
    void SetContentsLines(IEnumerable<string> lines)
    {
        var validLines = ContentLineHelper.GetValidLinesForSave(lines);
        _contents.Lines = validLines;
        UpdateContentCount();
    }

    List<string> GetAutomationContents()
        => ContentLineHelper.GetAutomationLinesFromText(_contents.Text);

    void UpdateContentCount()
    {
        var count = ContentLineHelper.GetDisplayLinesFromText(_contents.Text).Count;
        _contentCount.Text = $"Số nội dung hợp lệ: {count}";
    }

    void OnContentsKeyDown(object? sender, KeyEventArgs e)
    {
        if (!(e.Control && e.KeyCode == Keys.V) && !(e.Shift && e.KeyCode == Keys.Insert)) return;
        if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;

        var pasted = ContentLineHelper.NormalizeNewLines(Clipboard.GetText(TextDataFormat.UnicodeText));
        var start = _contents.SelectionStart;
        _contents.SelectedText = pasted;
        _contents.SelectionStart = start + pasted.Length;
        _contents.SelectionLength = 0;
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    Control XPathLine(string label, TextBox box, Func<Task> pick, Func<Task> test, bool clickablePick = false)
    {
        var t = new TableLayoutPanel { AutoSize = true, Width = 560, ColumnCount = 5, Margin = new Padding(0, 5, 0, 5) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        var status = new Label { AutoSize = false, Width = 78, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        void Refresh() { status.Text = string.IsNullOrWhiteSpace(box.Text) ? "— Thiếu" : "✓ Đã có"; status.ForeColor = string.IsNullOrWhiteSpace(box.Text) ? Color.Firebrick : Color.DarkGreen; }
        box.TextChanged += (_, _) => Refresh(); Refresh();
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0); t.Controls.Add(status, 1, 0);
        t.Controls.Add(Btn("Lấy XPath", async (_, _) => { if (clickablePick) await PickIntoAsync(box, true); else await pick(); }), 2, 0);
        t.Controls.Add(Btn("Thử", async (_, _) => await test()), 3, 0);
        t.Controls.Add(Btn("Xem/Sửa", (_, _) => ShowXPathEditor(label, box)), 4, 0); return t;
    }

    void LoadToUi()
    {
        _xp1.Text = _settings.XPathPoint1; _xp2.Text = _settings.XPathPoint2; _xpPeriodic.Text = _settings.XPathPeriodicAction; _xpHover.Text = _settings.XPathHoverArea;
        _switchNeedsHover.Checked = _settings.SwitchNeedsHover; _useArrowDown.Checked = _settings.UseArrowDownForLiveSwitch; _hoverDelay.Value = Clamp(_settings.HoverDelayMs, _hoverDelay);
        _strictXPath.Checked = _settings.StrictXPathOnly;
        _delayMin.Value = Clamp(_settings.DelayMinMs, _delayMin); _delayMax.Value = Clamp(_settings.DelayMaxMs, _delayMax); _loopMin.Value = Clamp(_settings.LoopMinMs, _loopMin); _loopMax.Value = Clamp(_settings.LoopMaxMs, _loopMax);
        SelectCombo(_periodicMin, _settings.PeriodicF5Minutes == 0 ? "Không dùng" : _settings.PeriodicF5Minutes.ToString()); _timerStop.Value = Clamp(_settings.TimerStopMinutes, _timerStop);
        SetContentsLines(_settingsService.LoadContents());
        _viewerEnabled.Checked = _settings.Viewer.Enabled; _viewerXp.Text = _settings.Viewer.XPath; _viewerThreshold.Value = Clamp(_settings.Viewer.Threshold, _viewerThreshold);
        _viewerConfirm.Value = Clamp(_settings.Viewer.ConfirmLow, _viewerConfirm); _viewerInterval.Value = Clamp(_settings.Viewer.IntervalSec, _viewerInterval); _viewerWait.Value = Clamp(_settings.Viewer.WaitAfterF5Sec, _viewerWait);
        _viewerMaxF5.Value = Clamp(_settings.Viewer.MaxF5, _viewerMaxF5); _viewerRetries.Value = Clamp(_settings.Viewer.OcrRetries, _viewerRetries);
        _oldEnabled.Checked = _settings.OldLive.Enabled; _oldXp.Text = _settings.OldLive.XPath; _oldActionXp.Text = _settings.OldLive.ActionXPath;
        SelectCombo(_oldKeep, _settings.OldLive.KeepMinutes.ToString()); _oldVariation.Value = Clamp(_settings.OldLive.Variation, _oldVariation);
        LoadProfilesToUi();
        LoadRegions();
        RefreshPeriodicCountdownLabel();
    }

    TikTokProfileEntry? CurrentProfileEntry => _profileCatalog.Profiles.FirstOrDefault(p => p.Name.Equals(_profileCatalog.SelectedProfile, StringComparison.OrdinalIgnoreCase));
    string CurrentProfileName => CurrentProfileEntry?.Name ?? "";
    string CurrentProfilePath => CurrentProfileEntry?.ProfilePath ?? "";

    void LoadProfilesToUi()
    {
        _loadingProfiles = true;
        try
        {
            _profileCombo.SelectedIndexChanged -= OnProfileSelectionChanged;
            _profileCombo.Items.Clear();
            foreach (var profile in _profileCatalog.Profiles)
                _profileCombo.Items.Add(profile.Name);
            if (!string.IsNullOrWhiteSpace(_profileCatalog.SelectedProfile))
            {
                var idx = _profileCombo.Items.IndexOf(_profileCatalog.SelectedProfile);
                _profileCombo.SelectedIndex = idx >= 0 ? idx : (_profileCombo.Items.Count > 0 ? 0 : -1);
            }
            else _profileCombo.SelectedIndex = _profileCombo.Items.Count > 0 ? 0 : -1;
            _profileCatalog.SelectedProfile = _profileCombo.SelectedItem?.ToString() ?? "";
        }
        finally
        {
            _profileCombo.SelectedIndexChanged += OnProfileSelectionChanged;
            _loadingProfiles = false;
        }
    }

    void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_managedMode || _loadingProfiles) return;
        _profileCatalog.SelectedProfile = _profileCombo.SelectedItem?.ToString() ?? "";
        _profileService.Save(_profileCatalog);
        ApplySelectedProfileToSettings();
        if (_chrome.Connected)
        {
            _ = DisconnectForProfileChangeAsync();
        }
    }

    void ApplyManagedStartupOverrides()
    {
        if (!_managedMode) return;
        _settings.ChromePort = _startupOptions.CdpPort;
        _settings.ChromeProfileDir = Path.GetFullPath(_startupOptions.ProfilePath);
    }

    void ApplySelectedProfileToSettings(bool logSelection = true)
    {
        _settings.ChromeProfileDir = CurrentProfilePath;
        if (!logSelection) return;
        _log.Info($"SelectedProfile={CurrentProfileName}");
        _log.Info($"ProfilePath={CurrentProfilePath}");
        _log.Info($"CDPPort={_settings.ChromePort}");
    }

    decimal Clamp(int v, NumericUpDown n) => Math.Clamp((decimal)v, n.Minimum, n.Maximum);
    static void SelectCombo(ComboBox c, string s) { var i = c.Items.IndexOf(s); c.SelectedIndex = i >= 0 ? i : 0; }

    void LoadRegions()
    {
        _regions.Rows.Clear();
        foreach (var r in _settings.ScanRegions) AddRegionRow(r);
    }

    void AddRegionRow(ScanRegion? source = null)
    {
        var rowIndex = _regions.Rows.Add();
        var row = _regions.Rows[rowIndex];
        var nextNumber = rowIndex + 1;
        row.Cells["Enabled"].Value = source?.Enabled ?? true;
        row.Cells["Name"].Value = source?.Name ?? $"Vùng mới {nextNumber}";
        row.Cells["Periodic"].Value = source?.PeriodicEnabled ?? false;
        row.Cells["PeriodMin"].Value = source?.PeriodicMinutes ?? 10;
        row.Cells["Action"].Value = source?.Action ?? "CLICK_F5";
        row.Cells["Variation"].Value = source?.Variation ?? 55;
        row.Cells["Consecutive"].Value = source?.ConsecutiveMax ?? 2;
        row.Cells["AfterClick"].Value = source?.AfterClick ?? false;
        row.Cells["ScanXPath"].Value = source?.ScanXPath ?? "";
        row.Cells["ActionXPath"].Value = source?.ActionXPath ?? "";
        row.Cells["Images"].Value = source is null ? "" : string.Join('|', source.Images);
        row.Cells["RX1"].Value = source?.RX1 ?? 0d;
        row.Cells["RY1"].Value = source?.RY1 ?? 0d;
        row.Cells["RX2"].Value = source?.RX2 ?? 0d;
        row.Cells["RY2"].Value = source?.RY2 ?? 0d;
        UpdateRegionStatus(row);

        if (source is null)
        {
            _regions.ClearSelection();
            row.Selected = true;
            _regions.CurrentCell = row.Cells["Name"];
            _regions.BeginEdit(true);
        }
    }

    void DeleteSelectedRegionRow()
    {
        var row = _regions.CurrentRow;
        if (row is null) return;
        var name = S(row, "Name", "vùng đang chọn");
        if (MessageBox.Show($"Xóa dòng “{name}” khỏi cấu hình?\n\nẢnh mẫu trên ổ đĩa sẽ KHÔNG bị xóa.",
            "Xóa dòng", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _regions.Rows.Remove(row);
    }

    void SaveFromUi()
    {
        try
        {
            _regions.EndEdit();
            ApplySelectedProfileToSettings(logSelection: false);
            _settings.XPathPoint1 = _xp1.Text.Trim(); _settings.XPathPoint2 = _xp2.Text.Trim(); _settings.XPathPeriodicAction = _xpPeriodic.Text.Trim();
            _settings.XPathHoverArea = _xpHover.Text.Trim(); _settings.SwitchNeedsHover = _switchNeedsHover.Checked; _settings.UseArrowDownForLiveSwitch = _useArrowDown.Checked; _settings.HoverDelayMs = (int)_hoverDelay.Value;
            _settings.StrictXPathOnly = _strictXPath.Checked;
            _settings.DelayMinMs = (int)_delayMin.Value; _settings.DelayMaxMs = (int)_delayMax.Value; _settings.LoopMinMs = (int)_loopMin.Value; _settings.LoopMaxMs = (int)_loopMax.Value;
            _settings.PeriodicF5Minutes = _periodicMin.Text == "Không dùng" ? 0 : int.TryParse(_periodicMin.Text, out var pm) ? pm : 0; _settings.TimerStopMinutes = (int)_timerStop.Value;
            _settings.Viewer.Enabled = _viewerEnabled.Checked; _settings.Viewer.XPath = _viewerXp.Text.Trim(); _settings.Viewer.Threshold = (int)_viewerThreshold.Value; _settings.Viewer.ConfirmLow = (int)_viewerConfirm.Value;
            _settings.Viewer.IntervalSec = (int)_viewerInterval.Value; _settings.Viewer.WaitAfterF5Sec = (int)_viewerWait.Value; _settings.Viewer.MaxF5 = (int)_viewerMaxF5.Value; _settings.Viewer.OcrRetries = (int)_viewerRetries.Value;
            _settings.OldLive.Enabled = _oldEnabled.Checked; _settings.OldLive.XPath = _oldXp.Text.Trim(); _settings.OldLive.ActionXPath = _oldActionXp.Text.Trim();
            _settings.OldLive.KeepMinutes = int.TryParse(_oldKeep.Text, out var km) ? km : 10; _settings.OldLive.Variation = (int)_oldVariation.Value;
            _settings.ScanRegions = ReadRegions();
            _settingsService.Save(_settings); _settingsService.SaveContents(_contents.Text); _log.Info("Đã lưu cấu hình V11 + XPath vào auto_chrome.ini.");
            UpdateContentCount();
            RefreshPeriodicCountdownLabel();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi lưu cấu hình", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void AddProfile()
    {
        if (_managedMode)
        {
            MessageBox.Show("V12.5 quản lý Profile ở cửa sổ Manager. Worker V11.5 không đổi danh sách profile.", "Profile V12.5", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        TikTokProfileEntry? entry = null;
        try
        {
            var name = PromptText("Thêm Profile TikTok", "Tên profile mới");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = ValidateNewProfileName(name);
            entry = _profileService.CreateManagedProfile(name);
            _profileCatalog = _profileService.Load();
            UpsertProfileEntry(entry);
            _profileCatalog.SelectedProfile = entry.Name;
            _profileService.Save(_profileCatalog);
            LoadProfilesToUi();
            ApplySelectedProfileToSettings();
            _log.Info($"[PROFILE_CREATED] name={entry.Name} path={entry.ProfilePath}");
        }
        catch (Exception ex)
        {
            if (entry is not null)
            {
                try
                {
                    var container = _profileService.GetProfileContainerPath(entry.Name);
                    if (Directory.Exists(container)) Directory.Delete(container, true);
                }
                catch { }
            }
            MessageBox.Show(ex.Message, "Thêm profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void AddExistingProfile()
    {
        if (_managedMode)
        {
            MessageBox.Show("V12.5 quản lý Profile ở cửa sổ Manager. Worker V11.5 không đổi danh sách profile.", "Profile V12.5", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Chọn thư mục Chrome user-data-dir đã tồn tại",
                UseDescriptionForTitle = true,
                SelectedPath = TikTokProfileService.LegacyImportedProfilePath
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var name = PromptText("Thêm Profile Có Sẵn", "Tên hiển thị", "TikTok cu");
            if (string.IsNullOrWhiteSpace(name)) return;
            var entry = _profileService.ImportExistingProfile(name, dlg.SelectedPath);
            UpsertProfileEntry(entry);
            _profileCatalog.SelectedProfile = entry.Name;
            _profileService.Save(_profileCatalog);
            LoadProfilesToUi();
            ApplySelectedProfileToSettings();
            _log.Info($"Đã thêm profile có sẵn: {entry.Name} -> {entry.ProfilePath}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Thêm profile có sẵn", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void DeleteProfile()
    {
        if (_managedMode)
        {
            MessageBox.Show("V12.5 quản lý Profile ở cửa sổ Manager. Worker V11.5 không đổi danh sách profile.", "Profile V12.5", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            if (CurrentProfileEntry is null) throw new InvalidOperationException("Chưa chọn profile để xóa.");
            if (_engine.Running) throw new InvalidOperationException("Hãy dừng tool trước khi xóa profile.");
            if (_chrome.Connected) throw new InvalidOperationException("Hãy đóng/kết nối lại Chrome sau khi đổi profile; hiện CDP vẫn đang mở.");
            var actionText = CurrentProfileEntry.Managed
                ? $"Xóa profile “{CurrentProfileName}” cùng toàn bộ dữ liệu Chrome của profile này?"
                : $"Xóa profile “{CurrentProfileName}” khỏi danh sách? Dữ liệu ở thư mục gốc sẽ không bị xóa.";
            if (MessageBox.Show(actionText, "Xóa profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var deleting = CurrentProfileName;
            _profileService.DeleteProfile(CurrentProfileEntry);
            RemoveProfileEntry(deleting);
            if (_profileCatalog.SelectedProfile.Equals(deleting, StringComparison.OrdinalIgnoreCase))
                _profileCatalog.SelectedProfile = _profileCatalog.Profiles.FirstOrDefault()?.Name ?? "";
            _profileService.Save(_profileCatalog);
            LoadProfilesToUi();
            ApplySelectedProfileToSettings();
            _log.Info("Đã xóa Profile TikTok: " + deleting);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Xóa profile", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void OpenSelectedProfileFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentProfilePath)) throw new InvalidOperationException("Chưa chọn Profile TikTok.");
            Directory.CreateDirectory(CurrentProfilePath);
            System.Diagnostics.Process.Start("explorer.exe", CurrentProfilePath);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Mở thư mục profile", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    async Task DisconnectForProfileChangeAsync()
    {
        try
        {
            _log.Info($"Đổi Profile TikTok: đã chọn {CurrentProfileName}; ngắt CDP hiện tại trước khi dùng profile mới.");
            await _chrome.DisconnectAsync(TimeSpan.FromSeconds(1.5));
            SetChromeStatus("Trạng thái Chrome: 🟠 Đã ngắt kết nối để đổi profile", Color.DarkOrange, "TikTok: —", Color.DimGray);
        }
        catch (Exception ex)
        {
            _log.Warn("Đổi profile: không ngắt được CDP hiện tại: " + ex.Message);
        }
    }

    string? PromptText(string title, string label, string initialValue = "")
    {
        using var f = new Form
        {
            Text = title,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(420, 190),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ShowInTaskbar = false
        };
        ModernDialog.Apply(f);
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label
        {
            AutoSize = true,
            Text = label,
            Margin = new Padding(0, 0, 0, 6)
        };
        ModernDialog.StylePrimaryLabel(lbl);
        var box = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Width = 390,
            MinimumSize = new Size(360, 0),
            Text = initialValue,
            Margin = new Padding(0)
        };
        ModernDialog.StyleTextInput(box);
        var spacer = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            MinimumSize = new Size(0, 10),
            Height = 10
        };
        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 10)
        };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        ModernDialog.StylePrimaryButton(ok);
        ModernDialog.StyleSecondaryButton(cancel);
        bar.Controls.Add(ok); bar.Controls.Add(cancel);
        root.Controls.Add(lbl, 0, 0);
        root.Controls.Add(box, 0, 1);
        root.Controls.Add(spacer, 0, 2);
        root.Controls.Add(bar, 0, 3);
        f.Controls.Add(root);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        f.Shown += (_, _) => box.Focus();
        return f.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
    }

    string ValidateNewProfileName(string rawName)
    {
        var name = _profileService.NormalizeName(rawName);
        if (_profileCatalog.Profiles.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Tên profile đã tồn tại: " + name);
        return name;
    }

    void UpsertProfileEntry(TikTokProfileEntry entry)
    {
        RemoveProfileEntry(entry.Name);
        _profileCatalog.Profiles.Add(entry);
        _profileCatalog.Profiles = _profileCatalog.Profiles.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    void RemoveProfileEntry(string name)
    {
        _profileCatalog.Profiles.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    List<ScanRegion> ReadRegions()
    {
        var list = new List<ScanRegion>(); int idx = 0;
        foreach (DataGridViewRow row in _regions.Rows)
        {
            if (row.IsNewRow) continue; var name = S(row, "Name"); if (string.IsNullOrWhiteSpace(name)) continue; idx++;
            list.Add(new ScanRegion
            {
                Index = idx, Enabled = B(row, "Enabled", true), Name = name, AfterClick = B(row, "AfterClick", true), PeriodicEnabled = B(row, "Periodic"),
                PeriodicMinutes = I(row, "PeriodMin", 10), Action = S(row, "Action", "F5"), Variation = Math.Clamp(I(row, "Variation", 55), 0, 255),
                ConsecutiveMax = Math.Clamp(I(row, "Consecutive", 1), 1, 4), ScanXPath = S(row, "ScanXPath"), ActionXPath = S(row, "ActionXPath"),
                Images = S(row, "Images").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                RX1 = D(row, "RX1"), RY1 = D(row, "RY1"), RX2 = D(row, "RX2"), RY2 = D(row, "RY2")
            });
        }
        return list;
    }
    static string S(DataGridViewRow r, string c, string f = "") => Convert.ToString(r.Cells[c].Value)?.Trim() is string s && s.Length > 0 ? s : f;
    static bool B(DataGridViewRow r, string c, bool f = false) => r.Cells[c].Value is bool b ? b : bool.TryParse(S(r, c), out var x) ? x : f;
    static int I(DataGridViewRow r, string c, int f = 0) => int.TryParse(S(r, c), out var v) ? v : f;
    static double D(DataGridViewRow r, string c) => double.TryParse(S(r, c), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    static string XPathState(string xp) => string.IsNullOrWhiteSpace(xp) ? "— Thiếu" : "✓ Có";
    string ActionXPathState(string action, string xp)
    {
        if (!action.Equals("CLICK_F5", StringComparison.OrdinalIgnoreCase)) return "—";
        if (_useArrowDown.Checked) return "↓ CDP";
        if (!string.IsNullOrWhiteSpace(xp)) return "✓ Riêng";
        return string.IsNullOrWhiteSpace(_xpPeriodic.Text) ? "— Thiếu" : "↪ Chung";
    }
    static string ImageState(IEnumerable<string> images)
    {
        var n = images.Count(x => !string.IsNullOrWhiteSpace(x));
        return n == 0 ? "—" : $"{n} ảnh";
    }
    void UpdateRegionStatus(DataGridViewRow row)
    {
        if (row.IsNewRow) return;
        var scan = S(row, "ScanXPath");
        var actionXp = S(row, "ActionXPath");
        var action = S(row, "Action", "F5");
        var images = S(row, "Images").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        row.Cells["ScanXPathState"].Value = XPathState(scan);
        row.Cells["ActionXPathState"].Value = ActionXPathState(action, actionXp);
        row.Cells["ImagesState"].Value = ImageState(images);
        row.Cells["ScanXPathState"].ToolTipText = string.IsNullOrWhiteSpace(scan) ? "Chưa cấu hình XPath vùng quét" : scan;
        var effectiveAction = !string.IsNullOrWhiteSpace(actionXp) ? actionXp : _xpPeriodic.Text.Trim();
        row.Cells["ActionXPathState"].ToolTipText = action.Equals("CLICK_F5", StringComparison.OrdinalIgnoreCase)
            ? (_useArrowDown.Checked ? "V11.5: CLICK_F5 đang được chuyển thành ArrowDown CDP + F5; không dùng XPath nút bấm." : (string.IsNullOrWhiteSpace(effectiveAction) ? "Chưa cấu hình XPath nút bấm" : effectiveAction))
            : "Hành động này không cần XPath nút bấm";
        row.Cells["ImagesState"].ToolTipText = images.Length == 0 ? "Chưa có ảnh mẫu" : string.Join(Environment.NewLine, images);
    }
    void RefreshAllRegionStatus()
    {
        foreach (DataGridViewRow row in _regions.Rows) if (!row.IsNewRow) UpdateRegionStatus(row);
    }
    void ShowRegionDetails(DataGridViewRow row)
    {
        var name = S(row, "Name", "không tên");
        var scan = S(row, "ScanXPath");
        var action = S(row, "ActionXPath");
        var images = S(row, "Images");
        MessageBox.Show($"Vùng: {name}\n\nXPath vùng quét:\n{(string.IsNullOrWhiteSpace(scan) ? "(trống)" : scan)}\n\nXPath nút bấm:\n{(string.IsNullOrWhiteSpace(action) ? "(trống — có thể dùng XPath nút chuyển live chung)" : action)}\n\nHover nút chuyển live: {(_switchNeedsHover.Checked ? "BẬT" : "TẮT")}\nXPath vùng hover chung:\n{(string.IsNullOrWhiteSpace(_xpHover.Text) ? "(trống)" : _xpHover.Text)}\n\nẢnh mẫu:\n{(string.IsNullOrWhiteSpace(images) ? "(chưa có)" : images.Replace("|", Environment.NewLine))}",
            "Chi tiết vùng", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void EnsureSelectedProfileReady(bool failIfInUse)
    {
        ApplySelectedProfileToSettings(logSelection: false);
        if (string.IsNullOrWhiteSpace(CurrentProfileName) || string.IsNullOrWhiteSpace(CurrentProfilePath))
            throw new InvalidOperationException("Hãy chọn Profile TikTok trước khi mở/kết nối Chrome.");
        var owner = _chrome.DescribeProfileOwners(CurrentProfilePath);
        // In V12.5 the Manager owns the profile/port mapping. A managed worker is allowed
        // to reconnect to the Chrome instance it previously launched on that assigned port.
        if (!_managedMode && failIfInUse && !string.IsNullOrWhiteSpace(owner))
            throw new InvalidOperationException($"Profile đang được sử dụng bởi Chrome khác. SelectedProfile={CurrentProfileName}; ProfilePath={CurrentProfilePath}; {owner}");
    }

    void LogSelectedProfileContext()
    {
        _log.Info($"SelectedProfile={CurrentProfileName}");
        _log.Info($"ProfilePath={CurrentProfilePath}");
        _log.Info($"CDPPort={_settings.ChromePort}");
    }

    void SyncChromeProfileNameBeforeLaunch()
    {
        if (string.IsNullOrWhiteSpace(CurrentProfileName) || string.IsNullOrWhiteSpace(_settings.ChromeProfileDir)) return;
        var result = _chromeProfileNameSync.SyncBeforeLaunch(_settings.ChromeProfileDir, CurrentProfileName);
        _log.Info($"[CHROME_PROFILE_NAME_SYNC] name={CurrentProfileName} updated={result.Updated} preferences={result.PreferencesPath}");
    }

    async Task LaunchChromeAsync()
    {
        if (_engine.Running) { MessageBox.Show("Hãy Dừng tool trước khi mở lại Chrome để tránh cắt ngang một vòng xử lý.", "Chrome V11.5", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            SaveFromUi();
            EnsureSelectedProfileReady(failIfInUse: true);
            LogSelectedProfileContext();
            SetChromeStatus("Trạng thái Chrome: 🟡 Đang mở Chrome...", Color.Goldenrod, "TikTok: —", Color.DimGray);
            await _chrome.LaunchAsync(_settings.ChromePort, _settings.ChromeProfileDir, SyncChromeProfileNameBeforeLaunch);
            await ConnectChromeAsync();
        }
        catch (Exception ex)
        {
            SetChromeStatus($"Trạng thái Chrome: 🔴 Lỗi mở Chrome: {ShortText(ex.Message, 90)}", Color.Firebrick, "TikTok: —", Color.DimGray);
            ShowUiProblem("CHROME_LAUNCH", "Chrome V11", ex);
        }
    }

    async Task<string> CloseChromeAsync()
    {
        if (_engine.Running)
        {
            SetChromeStatus("Trạng thái Chrome: ⚠ Hãy dừng automation trước khi đóng Chrome", Color.DarkOrange);
            return "automation_running";
        }

        try
        {
            // Close is inspection-only.  In particular, do not call SaveFromUi,
            // LaunchChromeAsync, EnsureChromeAsync, or EnsureSelectedProfileReady:
            // those belong to open/connect paths and must never run for Close.
            if (string.IsNullOrWhiteSpace(CurrentProfileName) || string.IsNullOrWhiteSpace(CurrentProfilePath))
                throw new InvalidOperationException("Chưa chọn Profile TikTok để đóng Chrome.");

            var result = await _chrome.CloseManagedBrowserAsync(CurrentProfilePath, _settings.ChromePort, manualRequest: true);
            if (!result.Closed)
            {
                SetChromeStatus("Trạng thái Chrome: 🔴 Chrome chưa đóng hoàn toàn", Color.Firebrick);
                return "close_failed";
            }

            SetChromeStatus(result.WasRunning
                    ? $"Trạng thái Chrome: ⚪ Đã đóng Chrome — {CurrentProfileName}"
                    : "Trạng thái Chrome: ⚪ Chrome của profile hiện tại chưa chạy",
                Color.DimGray, "TikTok: —", Color.DimGray);
            return result.WasRunning ? "closed" : "not_running";
        }
        catch (Exception ex)
        {
            SetChromeStatus("Trạng thái Chrome: 🔴 Không thể đóng Chrome", Color.Firebrick);
            ShowUiProblem("CHROME_CLOSE", "Đóng Chrome", ex, showDialog: false);
            return "close_failed";
        }
    }

    async Task ConnectChromeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            SaveFromUi();
            EnsureSelectedProfileReady(failIfInUse: true);
            LogSelectedProfileContext();
            SetChromeStatus("Trạng thái Chrome: 🟡 Đang kết nối CDP...", Color.Goldenrod, "TikTok: 🟡 Đang kết nối CDP...", Color.Goldenrod);
            await _chrome.ConnectAsync(_settings.ChromePort);
            _chrome.AttachManagedWindow(CurrentProfilePath, _settings.ChromePort);
            sw.Stop();
            _log.Info($"[PERF] CDP reconnect: {sw.ElapsedMilliseconds} ms");
            RefreshChromeStatus();
        }
        catch (Exception ex)
        {
            sw.Stop();
            SetChromeStatus("Trạng thái Chrome: 🔴 Mất kết nối CDP", Color.Firebrick, $"TikTok: 🔴 {ShortText(ex.Message, 90)}", Color.Firebrick);
            ShowUiProblem("CHROME_CONNECT", "Kết nối Chrome", ex, showDialog: true);
        }
    }

    async Task EnsureChromeAsync()
    {
        if (!_chrome.Connected)
        {
            SaveFromUi();
            EnsureSelectedProfileReady(failIfInUse: true);
            LogSelectedProfileContext();
            try
            {
                SetChromeStatus("Trạng thái Chrome: 🟡 Đang kết nối CDP...", Color.Goldenrod, "TikTok: 🟡 Đang kết nối CDP...", Color.Goldenrod);
                await _chrome.ConnectAsync(_settings.ChromePort);
                _chrome.AttachManagedWindow(CurrentProfilePath, _settings.ChromePort);
            }
            catch
            {
                SetChromeStatus("Trạng thái Chrome: 🟡 Đang mở Chrome...", Color.Goldenrod, "TikTok: —", Color.DimGray);
                await _chrome.LaunchAsync(_settings.ChromePort, _settings.ChromeProfileDir, SyncChromeProfileNameBeforeLaunch);
                SetChromeStatus("Trạng thái Chrome: 🟡 Đang kết nối CDP...", Color.Goldenrod, "TikTok: 🟡 Đang kết nối CDP...", Color.Goldenrod);
                await _chrome.ConnectAsync(_settings.ChromePort);
                _chrome.AttachManagedWindow(CurrentProfilePath, _settings.ChromePort);
            }
        }
        RefreshChromeStatus();
    }

    async Task PickIntoAsync(TextBox target, bool preferClickableAncestor = false)
    {
        // Tạm nhả hotkey Esc toàn cục để Esc có thể đi vào Chrome và hủy XPath picker thay vì dừng tool.
        UnregisterHotKey(Handle, HOTKEY_STOP);
        try
        {
            await EnsureChromeAsync();
            var xp = await _chrome.PickXPathAsync(TimeSpan.FromSeconds(45), preferClickableAncestor);
            if (!string.IsNullOrWhiteSpace(xp))
            {
                target.Text = xp;
                if (ReferenceEquals(target, _xpPeriodic)) RefreshAllRegionStatus();
                _log.Info("Đã lấy XPath: " + xp);
            }
            else _log.Warn("Đã hủy/hết thời gian lấy XPath.");
        }
        catch (Exception ex) { ShowUiProblem("XPATH_PICK", "Lấy XPath", ex); }
        finally { RegisterHotKey(Handle, HOTKEY_STOP, 0, (uint)Keys.Escape); }
    }

    void ToggleChromeWindow()
    {
        try
        {
            SaveFromUi();
            EnsureSelectedProfileReady(failIfInUse: false);
            var state = _chrome.GetManagedWindowState(CurrentProfilePath, _settings.ChromePort);
            if (state == ChromeWindowState.NotFound) throw new InvalidOperationException("Không tìm thấy cửa sổ Chrome V11 đang được tool quản lý.");
            bool ok = state == ChromeWindowState.Minimized
                ? _chrome.RestoreManagedWindow(CurrentProfilePath, _settings.ChromePort)
                : _chrome.MinimizeManagedWindow(CurrentProfilePath, _settings.ChromePort);
            if (!ok) throw new InvalidOperationException(state == ChromeWindowState.Minimized ? "Không khôi phục được cửa sổ Chrome." : "Không thu nhỏ được cửa sổ Chrome.");
            RefreshChromeStatus();
        }
        catch (Exception ex) { ShowUiProblem("CHROME_WINDOW", "Thu nhỏ / khôi phục Chrome", ex, showDialog: true); }
    }

    async Task TestXPathAsync(TextBox box, bool click)
    {
        try { await EnsureChromeAsync(); var ok = await _chrome.XPathExistsAsync(box.Text.Trim()); if (!ok) throw new Exception("Không tìm thấy XPath trên trang hiện tại."); if (click) await _chrome.ClickXPathAsync(box.Text.Trim()); MessageBox.Show(click ? "Đã tìm thấy và click XPath." : "XPath tồn tại trên trang.", "V11 XPath"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Thử XPath", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }


    async Task TestArrowDownAsync()
    {
        try
        {
            await EnsureChromeAsync();
            await _chrome.PressKeyAsync("ArrowDown", 1, 0);
            _log.Info("THỬ ↓ CDP: đã gửi ArrowDown ×1 vào trang TikTok.");
        }
        catch (Exception ex) { ShowUiProblem("ARROWDOWN_TEST", "Thử phím ↓ CDP", ex); }
    }

    async Task TestHoverXPathAsync()
    {
        try
        {
            await EnsureChromeAsync();
            var xp = _xpHover.Text.Trim();
            if (string.IsNullOrWhiteSpace(xp)) throw new InvalidOperationException("XPath vùng hover đang trống.");
            if (!await _chrome.XPathExistsAsync(xp)) throw new InvalidOperationException("[HOVER_TARGET_NOT_FOUND] Không tìm thấy XPath vùng hover LIVE: " + xp);
            var before = await _chrome.CountVisibleInteractiveOverXPathAsync(xp);
            await _chrome.HoverXPathAsync(xp);
            await Task.Delay(Math.Max(350, (int)_hoverDelay.Value));
            var after = await _chrome.CountVisibleInteractiveOverXPathAsync(xp);
            await Task.Delay(2500);
            MessageBox.Show($"Hover ảo đã gửi thành công.\nControl tương tác giao vùng LIVE: {before} → {after}.\n\nNếu nút chuyển LIVE vẫn không hiện, hãy lấy lại XPath vùng hover vào đúng player/video luôn tồn tại.", "Thử hover", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowUiProblem("XPATH_HOVER_TEST", "Thử hover LIVE", ex); }
    }

    async Task TestSwitchXPathAsync()
    {
        try
        {
            await EnsureChromeAsync();
            var actionXp = _xpPeriodic.Text.Trim();
            if (string.IsNullOrWhiteSpace(actionXp)) throw new InvalidOperationException("XPath nút chuyển live đang trống.");

            if (!await _chrome.XPathExistsAsync(actionXp))
            {
                if (!_switchNeedsHover.Checked) throw new InvalidOperationException("[LIVE_SWITCH_NOT_FOUND] Không tìm thấy XPath nút chuyển live và hover đang tắt: " + actionXp);
                var hoverXp = _xpHover.Text.Trim();
                if (string.IsNullOrWhiteSpace(hoverXp)) throw new InvalidOperationException("[HOVER_TARGET_MISSING] XPath vùng hover đang trống.");
                if (!await _chrome.XPathExistsAsync(hoverXp)) throw new InvalidOperationException("[HOVER_TARGET_NOT_FOUND] Không tìm thấy XPath vùng hover: " + hoverXp);
                var before = await _chrome.CountVisibleInteractiveOverXPathAsync(hoverXp);
                await _chrome.HoverXPathAsync(hoverXp);
                await Task.Delay((int)_hoverDelay.Value);
                var end = Environment.TickCount64 + 2500;
                while (!await _chrome.XPathExistsAsync(actionXp) && Environment.TickCount64 < end) await Task.Delay(100);
                if (!await _chrome.XPathExistsAsync(actionXp))
                {
                    var after = await _chrome.CountVisibleInteractiveOverXPathAsync(hoverXp);
                    if (after <= before) throw new InvalidOperationException("[HOVER_CONTROL_NOT_SHOWN] Hover đã gửi nhưng control LIVE không xuất hiện. Hãy bấm ‘Thử hover’ và lấy lại vùng hover.");
                    throw new InvalidOperationException("[LIVE_SWITCH_NOT_FOUND] Control đã xuất hiện nhưng XPath nút chuyển live không còn đúng. Hãy bấm Lấy XPath lại; V11.5 tự chọn clickable ancestor.");
                }
            }

            await _chrome.ClickXPathDomSmartAsync(actionXp);
            MessageBox.Show("Đã tìm thấy và click nút chuyển live bằng DOM/clickable ancestor. Không dùng chuột Windows.", "Thử nút chuyển live", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowUiProblem("LIVE_SWITCH_TEST", "Thử nút chuyển live", ex); }
    }

    async Task PickRegionCellAsync(string col)
    {
        if (_regions.CurrentRow is null || _regions.CurrentRow.IsNewRow) { MessageBox.Show("Hãy chọn một dòng vùng quét trước."); return; }
        var temp = new TextBox(); await PickIntoAsync(temp, col == "ActionXPath");
        if (temp.Text.Length > 0)
        {
            _regions.CurrentRow.Cells[col].Value = temp.Text;
            UpdateRegionStatus(_regions.CurrentRow);
        }
    }

    async Task CaptureSelectedRegionTemplateAsync()
    {
        if (_regions.CurrentRow is null || _regions.CurrentRow.IsNewRow) { MessageBox.Show("Hãy chọn một dòng vùng quét."); return; }
        try
        {
            await EnsureChromeAsync();
            var xp = S(_regions.CurrentRow, "ScanXPath");
            if (string.IsNullOrWhiteSpace(xp)) throw new InvalidOperationException("Hãy cấu hình XPath vùng quét trước khi chụp ảnh mẫu theo XPath.");
            var bytes = await _chrome.CaptureXPathAsync(xp);
            var dir = Path.Combine(_baseDir, "anh_mau_chup"); Directory.CreateDirectory(dir);
            var safe = string.Concat(S(_regions.CurrentRow, "Name", "vung").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var file = $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var full = Path.Combine(dir, file); File.WriteAllBytes(full, bytes);
            var rel = Path.Combine("anh_mau_chup", file);
            var old = S(_regions.CurrentRow, "Images");
            _regions.CurrentRow.Cells["Images"].Value = string.IsNullOrWhiteSpace(old) ? rel : old + "|" + rel;
            UpdateRegionStatus(_regions.CurrentRow);
            _log.Info("Đã chụp XPath làm ảnh mẫu: " + rel);
            MessageBox.Show("Đã lưu và thêm vào danh sách ảnh mẫu:\n" + rel, "V11");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Chụp ảnh mẫu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    async Task TestSelectedRegionAsync()
    {
        if (_regions.CurrentRow is null || _regions.CurrentRow.IsNewRow) { MessageBox.Show("Hãy chọn một dòng vùng quét."); return; }
        await TestRegionRowAsync(_regions.CurrentRow);
    }

    async Task<(bool found, string detail)> TestRegionRowCoreAsync(DataGridViewRow row)
    {
        await EnsureChromeAsync();
        var regions = ReadRegions();
        var rowName = S(row, "Name");
        var occurrence = 0;
        for (int i = 0; i <= row.Index && i < _regions.Rows.Count; i++)
            if (!_regions.Rows[i].IsNewRow && S(_regions.Rows[i], "Name") == rowName) occurrence++;
        var r = regions.Where(x => x.Name == rowName).Skip(Math.Max(0, occurrence - 1)).FirstOrDefault();
        if (r is null) return (false, "Không đọc được cấu hình dòng.");

        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(r.ScanXPath)) bytes = await _chrome.CaptureXPathAsync(r.ScanXPath);
        else if (_strictXPath.Checked) throw new InvalidOperationException($"Vùng “{r.Name}” chưa có XPath vùng quét. Chế độ Chỉ XPath đang bật nên không dùng tọa độ fallback.");
        else bytes = await _chrome.CaptureViewportAsync();
        using var src0 = ImageMatcher.FromBytes(bytes);
        using var src = string.IsNullOrWhiteSpace(r.ScanXPath) ? ImageMatcher.CropNormalized(src0, r.RX1, r.RY1, r.RX2, r.RY2) : new Bitmap(src0);
        int existing = 0;
        foreach (var rel in r.Images)
        {
            var path = Path.IsPathRooted(rel) ? rel : Path.Combine(_baseDir, rel);
            if (!File.Exists(path)) continue;
            existing++;
            using var tpl = new Bitmap(path);
            var m = ImageMatcher.FindMultiScale(src, tpl, r.Variation);
            if (m.Found) return (true, $"ĐÃ KHỚP: {Path.GetFileName(path)} | sai số={r.Variation} | x={m.X}, y={m.Y}");
        }
        return (false, existing == 0 ? "Không có file ảnh mẫu hợp lệ." : $"Không khớp {existing} ảnh mẫu (sai số={r.Variation}).");
    }

    async Task TestRegionRowAsync(DataGridViewRow row)
    {
        if (_engine.Running && !_engine.Paused) { MessageBox.Show("Hãy bấm F9 tạm dừng hoặc Dừng tool trước khi quét thử để tránh tranh chấp với vòng quét đang chạy."); return; }
        try
        {
            var (found, detail) = await TestRegionRowCoreAsync(row);
            var name = S(row, "Name");
            _log.Info($"QUÉT THỬ “{name}”: {detail}");
            MessageBox.Show(detail, "Quét thử — " + name, MessageBoxButtons.OK, found ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { var name = S(row, "Name", "không tên"); ShowUiProblem("REGION_TEST", $"Quét thử vùng “{name}”", ex); }
    }

    async Task TestAllRegionsAsync()
    {
        if (_engine.Running && !_engine.Paused) { MessageBox.Show("Hãy bấm F9 tạm dừng hoặc Dừng tool trước khi quét thử tất cả để tránh tranh chấp CDP."); return; }
        var lines = new List<string>();
        foreach (DataGridViewRow row in _regions.Rows)
        {
            if (row.IsNewRow || !B(row, "Enabled", true) || string.IsNullOrWhiteSpace(S(row, "Name"))) continue;
            var name = S(row, "Name");
            try
            {
                var (found, detail) = await TestRegionRowCoreAsync(row);
                lines.Add($"{(found ? "✓" : "—")} {name}: {detail}");
            }
            catch (Exception ex) { lines.Add($"! {name}: Lỗi {ex.Message}"); }
        }
        MessageBox.Show(lines.Count == 0 ? "Không có vùng bật để thử." : string.Join(Environment.NewLine, lines), "Kết quả quét thử tất cả");
    }

    void AddImagesToSelectedRegion()
    {
        if (_regions.CurrentRow is null || _regions.CurrentRow.IsNewRow) { MessageBox.Show("Hãy chọn một dòng vùng quét."); return; }
        using var dlg = new OpenFileDialog { Title = "Chọn ảnh mẫu", Filter = "Ảnh|*.png;*.bmp;*.jpg;*.jpeg|Tất cả file|*.*", Multiselect = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var dir = Path.Combine(_baseDir, "anh_mau_chup"); Directory.CreateDirectory(dir);
            var existing = S(_regions.CurrentRow, "Images").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var src in dlg.FileNames)
            {
                var safe = Path.GetFileName(src);
                var destName = $"import_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safe}";
                var dest = Path.Combine(dir, destName);
                File.Copy(src, dest, true);
                existing.Add(Path.Combine("anh_mau_chup", destName));
                Thread.Sleep(2);
            }
            _regions.CurrentRow.Cells["Images"].Value = string.Join('|', existing.Distinct(StringComparer.OrdinalIgnoreCase));
            UpdateRegionStatus(_regions.CurrentRow);
            var regionName = S(_regions.CurrentRow, "Name");
            _log.Info($"Đã thêm {dlg.FileNames.Length} ảnh mẫu vào vùng “{regionName}”.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Thêm ảnh mẫu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    List<(string rel, string full)> GetRowImages(DataGridViewRow row)
    {
        return S(row, "Images")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(rel => (rel, full: Path.IsPathRooted(rel) ? rel : Path.Combine(_baseDir, rel)))
            .ToList();
    }

    static Image LoadImageUnlocked(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var src = Image.FromStream(fs);
        return new Bitmap(src);
    }

    void ViewImagesForRow(DataGridViewRow row)
    {
        var images = GetRowImages(row).Where(x => File.Exists(x.full)).ToList();
        var name = S(row, "Name", "không tên");
        if (images.Count == 0) { MessageBox.Show($"Vùng “{name}” chưa có file ảnh mẫu hợp lệ.", "Xem ảnh", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        using var f = new Form { Text = $"Ảnh mẫu — {name}", Width = 780, Height = 620, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false };
        ModernDialog.Apply(f, fixedDialog: false);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), WrapContents = false };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 470 };
        ModernDialog.StyleSelectionInput(combo);
        combo.Items.AddRange(images.Select(x => Path.GetFileName(x.full)).Cast<object>().ToArray()); combo.SelectedIndex = 0;
        var pathLabel = new Label { AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(8), MaximumSize = new Size(740, 0) };
        var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 32, 32) };
        var imageLabel = new Label { Text = "Ảnh", AutoSize = true, Margin = new Padding(0, 8, 6, 0) };
        ModernDialog.StylePrimaryLabel(imageLabel);
        var openFolder = Btn("Mở thư mục", (_, _) => System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + images[combo.SelectedIndex].full + "\""));
        ModernDialog.StyleSecondaryButton(openFolder);
        top.Controls.Add(imageLabel); top.Controls.Add(combo);
        top.Controls.Add(openFolder);
        f.Controls.Add(pic); f.Controls.Add(pathLabel); f.Controls.Add(top);

        void LoadSelected()
        {
            try
            {
                pic.Image?.Dispose();
                var item = images[combo.SelectedIndex];
                pic.Image = LoadImageUnlocked(item.full);
                pathLabel.Text = $"{combo.SelectedIndex + 1}/{images.Count}  {item.rel}   |   {pic.Image.Width}×{pic.Image.Height}px";
            }
            catch (Exception ex) { pathLabel.Text = "Lỗi mở ảnh: " + ex.Message; }
        }
        combo.SelectedIndexChanged += (_, _) => LoadSelected();
        f.FormClosed += (_, _) => pic.Image?.Dispose();
        f.KeyPreview = true;
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        LoadSelected(); f.ShowDialog(this);
    }

    void DeleteImagesFromRow(DataGridViewRow row)
    {
        var images = GetRowImages(row);
        var name = S(row, "Name", "không tên");
        if (images.Count == 0) { MessageBox.Show($"Vùng “{name}” chưa có ảnh mẫu để xóa.", "Xóa ảnh"); return; }

        using var f = new Form { Text = $"Xóa ảnh mẫu khỏi vùng — {name}", Width = 620, Height = 420, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        ModernDialog.Apply(f);
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        ModernDialog.StyleSelectionList(list);
        foreach (var x in images) list.Items.Add($"{Path.GetFileName(x.full)}   [{x.rel}]", false);
        var note = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 8, 10, 0), Text = "Chọn ảnh cần bỏ khỏi vùng quét. Tool chỉ xóa khỏi cấu hình vùng, KHÔNG xóa file vật lý để tránh làm hỏng vùng khác đang dùng chung ảnh." };
        ModernDialog.StylePrimaryLabel(note);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        var ok = new Button { Text = "Xóa đã chọn", AutoSize = true, DialogResult = DialogResult.OK }; var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        var all = new Button { Text = "Chọn tất cả", AutoSize = true }; all.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
        ModernDialog.StyleDestructiveButton(ok);
        ModernDialog.StyleSecondaryButton(cancel);
        ModernDialog.StyleSecondaryButton(all);
        bottom.Controls.Add(ok); bottom.Controls.Add(cancel); bottom.Controls.Add(all); f.Controls.Add(list); f.Controls.Add(note); f.Controls.Add(bottom); f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var remove = Enumerable.Range(0, list.Items.Count).Where(list.GetItemChecked).ToHashSet();
        if (remove.Count == 0) return;
        var remain = images.Where((_, i) => !remove.Contains(i)).Select(x => x.rel).ToList();
        row.Cells["Images"].Value = string.Join('|', remain); UpdateRegionStatus(row);
        _log.Info($"Vùng “{name}”: đã xóa {remove.Count} ảnh khỏi danh sách quét; file vật lý được giữ lại.");
    }

    async Task TestViewerAsync()
    {
        try
        {
            await EnsureChromeAsync();
            var xp = _viewerXp.Text.Trim();
            if (string.IsNullOrWhiteSpace(xp)) throw new InvalidOperationException("[VIEWER_XPATH_MISSING] Chưa cấu hình XPath người xem");
            if (!await _chrome.XPathExistsAsync(xp)) throw new InvalidOperationException("[VIEWER_XPATH_NOT_FOUND] Không tìm thấy XPath người xem trên trang hiện tại: " + xp);
            var txt = await _chrome.GetTextAsync(xp);
            var parser = new TesseractOcr(_log);
            var n = parser.ParseViewerCount(txt);
            _viewerTest.Text = n >= 0 ? $"raw=\"{txt}\" | parse={n}" : $"[VIEWER_PARSE_FAILED] raw=\"{txt}\"";
        }
        catch (Exception ex) { _viewerTest.Text = "Lỗi: " + ex.Message; ShowUiProblem("VIEWER_TEST", "Đọc thử người xem", ex, showDialog: false); }
    }

    async Task CaptureOldTestAsync()
    {
        try
        {
            await EnsureChromeAsync(); var bytes = await _chrome.CaptureXPathAsync(_oldXp.Text.Trim()); var dir = Path.Combine(_baseDir, "live_cu_tam"); Directory.CreateDirectory(dir); var p = Path.Combine(dir, "live_cu_test_xpath.png"); File.WriteAllBytes(p, bytes); _oldTest.Text = "Đã chụp: " + p; _log.Info("Đã chụp thử XPath Live cũ: " + p);
        }
        catch (Exception ex) { _oldTest.Text = "Lỗi: " + ex.Message; ShowUiProblem("OLDLIVE_CAPTURE_TEST", "Chụp mẫu test Live cũ", ex, showDialog: false); }
    }

    async Task TestOldLiveMatchAsync()
    {
        try
        {
            await EnsureChromeAsync();
            var sample = Path.Combine(_baseDir, "live_cu_tam", "live_cu_test_xpath.png");
            if (!File.Exists(sample)) throw new FileNotFoundException("Chưa có mẫu test. Hãy đứng ở live cần lưu rồi bấm ‘Chụp mẫu test’ trước.");
            var xp = _oldXp.Text.Trim();
            if (string.IsNullOrWhiteSpace(xp)) throw new InvalidOperationException("XPath vùng nhận dạng Live cũ đang trống.");
            var bytes = await _chrome.CaptureXPathAsync(xp);
            using var current = ImageMatcher.FromBytes(bytes); using var tpl = new Bitmap(sample);
            var variation = (int)_oldVariation.Value; var match = ImageMatcher.FindMultiScale(current, tpl, variation);
            var msg = match.Found ? $"KHỚP — x={match.X}, y={match.Y}, sai số={variation}" : $"KHÔNG KHỚP — sai số={variation}";
            _oldTest.Text = msg; _log.Info("TEST LIVE CŨ: " + msg);
            MessageBox.Show(msg, "Quét thử Live cũ", MessageBoxButtons.OK, match.Found ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { _oldTest.Text = "Lỗi: " + ex.Message; ShowUiProblem("OLDLIVE_MATCH_TEST", "Quét thử Live cũ", ex); }
    }

    void ClearOldLivesManually()
    {
        if (_engine.Running)
        {
            MessageBox.Show("Hãy Dừng tool trước khi xóa thủ công ảnh Live cũ active.", "Live cũ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show("Xóa toàn bộ ảnh Live cũ active của profile hiện tại? Ảnh mẫu test không bị xóa.", "Xóa ảnh Live cũ", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            var removed = _engine.ClearOldLivesManually();
            _oldTest.Text = $"Đã xóa thủ công {removed} ảnh Live cũ active.";
            RefreshOldLiveDiagnostics();
        }
        catch (Exception ex) { ShowUiProblem("OLDLIVE_MANUAL_CLEAR", "Xóa ảnh Live cũ", ex); }
    }

    void ExportConfig()
    {
        if (_engine.Running && !_engine.Paused) { MessageBox.Show("Hãy bấm F9 tạm dừng hoặc Dừng tool trước khi xuất cấu hình để file được nhất quán."); return; }
        SaveFromUi();
        using var dlg = new SaveFileDialog { Title = "Xuất cấu hình V11", Filter = "Gói cấu hình ZIP|*.zip", FileName = $"TikTok_V11_Config_{DateTime.Now:yyyyMMdd_HHmm}.zip" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { _settingsService.ExportPackage(dlg.FileName); _log.Info("Đã xuất cấu hình: " + dlg.FileName); MessageBox.Show("Đã xuất cấu hình + nội dung + ảnh mẫu.\nKhông xuất Chrome profile/cookie.", "Xuất cấu hình"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Xuất cấu hình", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void ImportConfig()
    {
        if (_engine.Running) { MessageBox.Show("Hãy Dừng tool trước khi nhập cấu hình."); return; }
        using var dlg = new OpenFileDialog { Title = "Nhập cấu hình V11", Filter = "Gói cấu hình ZIP|*.zip" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show("Nhập cấu hình sẽ thay cấu hình hiện tại. Tool sẽ tự sao lưu INI/nội dung trước khi thay. Tiếp tục?", "Nhập cấu hình", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var backup = _settingsService.ImportPackage(dlg.FileName);
            _settings = _settingsService.Load();
            ApplyManagedStartupOverrides();
            LoadToUi();
            _log.Info("Đã nhập cấu hình: " + dlg.FileName + "; backup=" + backup);
            MessageBox.Show("Đã nhập cấu hình.\nBản cũ được sao lưu tại:\n" + backup, "Nhập cấu hình");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Nhập cấu hình", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    async Task HandleStartStopAsync()
    {
        if (_startStopCommandInFlight) return;
        if (!_engine.Running)
        {
            await StartAsync();
            return;
        }

        _startStopCommandInFlight = true;
        _stopCommandInFlight = true;
        UpdateRunControlButtons();
        try
        {
            _engine.Stop();
        }
        finally
        {
            _stopCommandInFlight = false;
            _startStopCommandInFlight = false;
            UpdateRunControlButtons();
        }
    }

    void HandlePauseResume()
    {
        if (_startStopCommandInFlight || _pauseResumeCommandInFlight || !_engine.Running) return;
        _pauseResumeCommandInFlight = true;
        UpdateRunControlButtons();
        try
        {
            _engine.TogglePause();
        }
        finally
        {
            _pauseResumeCommandInFlight = false;
            UpdateRunControlButtons();
        }
    }

    async Task StartAsync()
    {
        if (_engine.Running || _startStopCommandInFlight) return;
        _startStopCommandInFlight = true;
        UpdateRunControlButtons();
        try
        {
            SaveFromUi();
            await EnsureChromeAsync();
            if (!await ValidateCoreXpathsBeforeStartAsync()) return;
            _engine.Start(_settings, GetAutomationContents());
        }
        catch (Exception ex) { ShowUiProblem("START_FAILED", "Không thể bắt đầu", ex); }
        finally
        {
            _startStopCommandInFlight = false;
            UpdateRunControlButtons();
        }
    }

    void AppendProblem(string message)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendProblem(message))); return; }
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _errorBox.AppendText(line + Environment.NewLine);
        SetLastErrorText("Lỗi: " + message, true);
    }

    void SetLastErrorText(string text, bool hasError)
    {
        _lastError.Text = text;
        _lastError.ForeColor = hasError ? Color.Firebrick : Color.FromArgb(88, 88, 88);
    }

    static string ShortText(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text[..max] + "...";
    }

    void ShowUiProblem(string code, string context, Exception ex, bool showDialog = true)
    {
        var msg = $"[{code}] {context} — {ex.Message}";
        _log.Error(msg);
        AppendProblem(msg);
        if (showDialog) MessageBox.Show(msg, context, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void OnEngineProblem(string message) => AppendProblem(message);

    async Task<bool> ValidateCoreXpathsBeforeStartAsync()
    {
        var errors = new List<string>();
        async Task CheckRequired(string name, string xp)
        {
            if (string.IsNullOrWhiteSpace(xp)) { errors.Add($"{name}: XPath đang trống."); return; }
            if (!await _chrome.XPathExistsAsync(xp)) errors.Add($"{name}: không tìm thấy XPath trên trang hiện tại: {xp}");
        }
        await CheckRequired("Điểm/ô nhập 1", _settings.XPathPoint1);
        await CheckRequired("Điểm/ô nhập 2", _settings.XPathPoint2);

        var needsLiveSwitch = _settings.PeriodicF5Minutes > 0
            || _settings.OldLive.Enabled
            || _settings.ScanRegions.Any(r => r.Enabled && r.Action.Equals("CLICK_F5", StringComparison.OrdinalIgnoreCase));
        if (needsLiveSwitch && !_settings.UseArrowDownForLiveSwitch && _settings.SwitchNeedsHover)
            await CheckRequired("Vùng hover để hiện nút chuyển live", _settings.XPathHoverArea);

        if (errors.Count == 0) return true;
        foreach (var e in errors) AppendProblem("[XPATH_CORE] " + e);
        MessageBox.Show("Không thể bắt đầu vì XPath thao tác chính chưa hợp lệ:\n\n" + string.Join("\n", errors), "Kiểm tra XPath", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    async Task CheckConfiguredXpathsAsync()
    {
        try
        {
            SaveFromUi(); await EnsureChromeAsync();
            var lines = new List<string>();
            async Task<bool> Check(string name, string xp, bool required)
            {
                if (string.IsNullOrWhiteSpace(xp)) { lines.Add($"{(required ? "✗" : "—")} {name}: XPath trống"); return false; }
                bool ok = await _chrome.XPathExistsAsync(xp);
                lines.Add($"{(ok ? "✓" : "!")} {name}: {(ok ? "tìm thấy" : "KHÔNG TÌM THẤY HIỆN TẠI")} | {xp}");
                return ok;
            }
            await Check("Điểm/ô nhập 1", _settings.XPathPoint1, true);
            await Check("Điểm/ô nhập 2", _settings.XPathPoint2, true);

            bool hoverReady = true;
            if (!_settings.UseArrowDownForLiveSwitch && _settings.SwitchNeedsHover)
            {
                hoverReady = await Check("Vùng hover để hiện nút chuyển live", _settings.XPathHoverArea, true);
                if (hoverReady)
                {
                    try
                    {
                        await _chrome.HoverXPathAsync(_settings.XPathHoverArea);
                        await Task.Delay(Math.Clamp(_settings.HoverDelayMs, 0, 3000));
                        lines.Add("✓ Hover ảo: đã kích hoạt vùng LIVE bằng CDP (không di chuyển chuột Windows)");
                    }
                    catch (Exception ex) { hoverReady = false; lines.Add("! Hover ảo: THẤT BẠI — " + ex.Message); }
                }
            }

            async Task CheckSwitch(string name, string xp, bool required)
            {
                if (_settings.SwitchNeedsHover && !hoverReady)
                {
                    lines.Add($"! {name}: chưa thể kiểm tra vì vùng hover không hợp lệ");
                    return;
                }
                await Check(name, xp, required);
            }

            if (_settings.UseArrowDownForLiveSwitch)
                lines.Add("✓ Chuyển LIVE: ArrowDown CDP — không yêu cầu XPath nút/hover");
            else if (_settings.PeriodicF5Minutes > 0) await CheckSwitch("Nút chuyển live / F5 định kỳ", _settings.XPathPeriodicAction, true);
            if (_settings.Viewer.Enabled) await Check("Người xem", _settings.Viewer.XPath, _settings.StrictXPathOnly);
            if (_settings.OldLive.Enabled)
            {
                await Check("Live cũ — vùng nhận dạng", _settings.OldLive.XPath, _settings.StrictXPathOnly);
                if (!_settings.UseArrowDownForLiveSwitch)
                {
                    var oldAction = string.IsNullOrWhiteSpace(_settings.OldLive.ActionXPath) ? _settings.XPathPeriodicAction : _settings.OldLive.ActionXPath;
                    await CheckSwitch("Live cũ — nút chuyển live", oldAction, true);
                }
            }
            foreach (var r in _settings.ScanRegions.Where(x => x.Enabled))
            {
                await Check($"Vùng “{r.Name}” — vùng quét", r.ScanXPath, _settings.StrictXPathOnly);
                if (r.Action.Equals("CLICK_F5", StringComparison.OrdinalIgnoreCase) && !_settings.UseArrowDownForLiveSwitch)
                {
                    var ax = string.IsNullOrWhiteSpace(r.ActionXPath) ? _settings.XPathPeriodicAction : r.ActionXPath;
                    await CheckSwitch($"Vùng “{r.Name}” — nút CLICK_F5", ax, true);
                }
            }
            var text = string.Join(Environment.NewLine, lines);
            _errorBox.AppendText($"[{DateTime.Now:HH:mm:ss}] KIỂM TRA XPATH\r\n{text}\r\n\r\n");
            MessageBox.Show(text, "Kiểm tra toàn bộ XPath", MessageBoxButtons.OK, lines.Any(x => x.StartsWith("✗") || x.StartsWith("!")) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowUiProblem("XPATH_DIAGNOSTIC", "Kiểm tra toàn bộ XPath", ex); }
    }

    void OnLog(string line)
    {
        // Logger callbacks can be very frequent during image matching.  Queue
        // them and let one UI timer append a batch instead of posting one
        // BeginInvoke + redraw + ScrollToCaret for every PERF line.
        _pendingLogLines.Enqueue(line);
    }

    void FlushPendingLogLines()
    {
        if (_pendingLogLines.IsEmpty || _logBox.IsDisposed) return;

        var batch = new StringBuilder();
        var count = 0;
        while (count < 500 && _pendingLogLines.TryDequeue(out var line))
        {
            batch.AppendLine(line);
            count++;
        }
        if (batch.Length == 0) return;

        _logBox.AppendText(batch.ToString());
        if (_logBox.TextLength > 200000)
        {
            var keep = Math.Min(150000, _logBox.TextLength);
            _logBox.Text = _logBox.Text[^keep..];
        }
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    void OnEngineStatus(string s)
    {
        var p = s.Split('\n', 2); var title = p[0]; var body = p.Length > 1 ? p[1] : "";
        var detailText = "Bước: " + (string.IsNullOrWhiteSpace(body) ? "—" : body.Replace("\r", " ").Replace("\n", " • "));
        Volatile.Write(ref _managedDetailSnapshot, detailText);
        if (InvokeRequired) { BeginInvoke(new Action(() => OnEngineStatus(s))); return; }
        _runState.Text = "Trạng thái: " + title;
        _runDetail.Text = detailText;
        _runDetail.Tag = body;
        _roundState.Text = "Vòng: " + _engine.Rounds;
        _runState.ForeColor = title switch
        {
            "ĐANG CHẠY" => Color.FromArgb(32, 120, 48),
            "TẠM DỪNG" => Color.DarkOrange,
            "ĐÃ DỪNG" => Color.IndianRed,
            "LỖI" => Color.Firebrick,
            "CẢNH BÁO" => Color.DarkOrange,
            _ => Color.FromArgb(32, 98, 55)
        };
        _runDetail.ForeColor = Color.FromArgb(34, 93, 168);
        _roundState.ForeColor = Color.FromArgb(78, 78, 78);
        UpdateRunControlButtons();
    }
    void OnEngineState()
    {
        if (InvokeRequired) { BeginInvoke(new Action(OnEngineState)); return; }
        _roundState.Text = "Vòng: " + _engine.Rounds;
        RefreshUiStatusLabels();
    }

    void OnEngineRunStateChanged(AutomationRunState state)
    {
        // This event is raised by AutomationEngine immediately after its actual
        // running/paused flags change; it is not inferred from the UI text.
        _runtimeStats.ApplyEngineState(state);
    }

    void OnApplicationExit(object? sender, EventArgs e)
    {
        // Covers normal process teardown paths that bypass a visible FormClosing
        // interaction. A forced OS termination can still lose only the interval
        // after the most recent 30-second checkpoint.
        _runtimeStats.Flush();
        _runtimeStats.Dispose();
    }

    void RefreshPeriodicCountdownLabel()
    {
        string text;
        if (_engine.Running)
        {
            var snap = _engine.GetPeriodicF5Snapshot();
            if (!snap.Enabled) text = "↓ + F5 định kỳ: tắt.";
            else if (snap.Executing || snap.DueAt <= DateTime.Now) text = "↓ + F5 định kỳ: đang thực hiện...";
            else
            {
                var remain = snap.DueAt - DateTime.Now;
                if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
                int totalMinutes = (int)remain.TotalMinutes;
                text = $"↓ + F5 định kỳ: còn {totalMinutes:00}:{remain.Seconds:00}";
            }
        }
        else
        {
            text = _settings.PeriodicF5Minutes > 0
                ? "↓ + F5 định kỳ: chưa chạy."
                : "↓ + F5 định kỳ: tắt.";
        }

        _periodicState.Text = text;
        _periodicState.ForeColor = text.Contains("đang thực hiện", StringComparison.OrdinalIgnoreCase)
            ? Color.MediumPurple
            : text.Contains("tắt", StringComparison.OrdinalIgnoreCase)
                ? Color.SlateBlue
                : Color.FromArgb(86, 76, 170);
    }

    void RefreshOldLiveDiagnostics()
    {
        var snap = _engine.GetOldLiveDiagnosticsSnapshot();
        SetTextIfChanged(_oldDiagSummary, $"Số ảnh Live cũ active: {snap.ActiveCount}");
        SetTextIfChanged(_oldDiagCapturedAt, "Lần lưu Live cũ gần nhất: " + FormatDateTime(snap.LastCapturedAt));
        SetTextIfChanged(_oldDiagMatchAt, "Lần match gần nhất: " + FormatDateTime(snap.LastMatchAt));
        SetTextIfChanged(_oldDiagMatch, "Kết quả match gần nhất: " + (snap.LastMatchFound is null ? "—" : snap.LastMatchFound.Value ? "KHỚP" : "KHÔNG KHỚP"));
        SetTextIfChanged(_oldDiagMatchImage, "Ảnh match gần nhất: " + (string.IsNullOrWhiteSpace(snap.LastMatchImage) ? "—" : snap.LastMatchImage));
        SetTextIfChanged(_oldDiagScore, "Điểm sai khác / score: " + (snap.LastMatchScore is null ? "—" : snap.LastMatchScore.Value.ToString("F4")));

        // The entry list changes rarely, while Age/Remaining change every second.
        // Rebuild rows only when entry identity/order changes; otherwise update
        // just the two time cells to avoid a full DataGridView churn each tick.
        var ids = snap.Entries.Select(entry => entry.Id).ToArray();
        if (!_oldLiveEntryIds.SequenceEqual(ids, StringComparer.Ordinal))
        {
            _oldLiveGrid.Rows.Clear();
            foreach (var entry in snap.Entries)
                _oldLiveGrid.Rows.Add(entry.FileName, FormatDuration(entry.Age), FormatDuration(entry.Remaining));
            _oldLiveEntryIds = ids;
            return;
        }

        for (var i = 0; i < snap.Entries.Count && i < _oldLiveGrid.Rows.Count; i++)
        {
            var entry = snap.Entries[i];
            SetCellValueIfChanged(_oldLiveGrid.Rows[i].Cells["Age"], FormatDuration(entry.Age));
            SetCellValueIfChanged(_oldLiveGrid.Rows[i].Cells["Remaining"], FormatDuration(entry.Remaining));
        }
    }

    static void SetTextIfChanged(Label label, string text)
    {
        if (!string.Equals(label.Text, text, StringComparison.Ordinal)) label.Text = text;
    }

    static void SetCellValueIfChanged(DataGridViewCell cell, string value)
    {
        if (!string.Equals(Convert.ToString(cell.Value), value, StringComparison.Ordinal)) cell.Value = value;
    }

    static string FormatDateTime(DateTime? value) => value?.ToString("HH:mm:ss") ?? "—";
    static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }

    static string FormatRuntimeClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    static string FormatRuntimeTotal(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return $"{(long)value.TotalHours}h {value.Minutes:00}m";
    }

    void RegisterGlobalHotkeys()
    {
        RegisterHotKey(Handle, HOTKEY_START, 0, (uint)Keys.F8); RegisterHotKey(Handle, HOTKEY_PAUSE, 0, (uint)Keys.F9); RegisterHotKey(Handle, HOTKEY_STOP, 0, (uint)Keys.Escape);
    }
    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (id == HOTKEY_START) _ = StartAsync(); else if (id == HOTKEY_PAUSE) HandlePauseResume(); else if (id == HOTKEY_STOP) _engine.Stop();
        }
        base.WndProc(ref m);
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || _shutdownComplete) return;
        e.Cancel = true;
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        _ = ShutdownAsync();
    }

    async Task ShutdownAsync()
    {
        _log.Warn("[SHUTDOWN] bắt đầu");
        try { SaveFromUi(); } catch (Exception ex) { _log.Warn("[SHUTDOWN] lưu cấu hình thất bại: " + ex.Message); }

        _periodicUiTimer.Stop();
        _logUiTimer.Stop();
        FlushPendingLogLines();
        try { _periodicUiTimer.Dispose(); } catch { }
        try { _logUiTimer.Dispose(); } catch { }
        try
        {
            if (!_managedMode)
            {
                UnregisterHotKey(Handle, HOTKEY_START);
                UnregisterHotKey(Handle, HOTKEY_PAUSE);
                UnregisterHotKey(Handle, HOTKEY_STOP);
            }
        }
        catch { }

        _log.Warn("[SHUTDOWN] gửi cancellation AutomationEngine");
        _engine.Stop("Đóng ứng dụng");
        bool engineStopped = false;
        try
        {
            engineStopped = await _engine.WaitForStopAsync(TimeSpan.FromSeconds(2.5));
        }
        catch (Exception ex)
        {
            _log.Warn("[SHUTDOWN] chờ AutomationEngine lỗi: " + ex.Message);
        }
        if (engineStopped) _log.Warn("[SHUTDOWN] AutomationEngine đã dừng");
        else _log.Warn("[SHUTDOWN] timeout AutomationEngine");
        _runtimeStats.Flush();
        _runtimeStats.Dispose();

        _log.Warn("[SHUTDOWN] disconnect CDP");
        try
        {
            var disconnectTask = _chrome.DisconnectAsync(TimeSpan.FromSeconds(1.5));
            var done = await Task.WhenAny(disconnectTask, Task.Delay(TimeSpan.FromSeconds(1.5)));
            if (done != disconnectTask) _log.Warn("[SHUTDOWN] timeout disconnect CDP");
        }
        catch (Exception ex)
        {
            _log.Warn("[SHUTDOWN] disconnect CDP lỗi: " + ex.Message);
        }

        _log.Warn("[SHUTDOWN] hoàn tất");
        _shutdownComplete = true;
        _allowClose = true;
        if (!IsDisposed)
        {
            try { BeginInvoke(new Action(Close)); } catch { }
        }
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
