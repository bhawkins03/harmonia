using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Windows.Forms;

namespace Harmonia;

internal sealed class MainForm : Form
{
    // Enables double buffering for continuously updated speaker meters.
    private sealed class BufferedMapPanel : Panel
    {
        public BufferedMapPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }

    private sealed record DeviceChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed class Settings
    {
        public SpeakerLayout Layout { get; set; } = SpeakerLayout.DualStereo;
        public Dictionary<string, Dictionary<string, string>> Assignments { get; set; } = new();
    }

    private readonly AudioEngine audio = new();
    private readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 50 };
    private readonly float[] displayLevels = new float[6];
    private readonly ComboBox layout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TableLayoutPanel assignmentsPanel = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
    private readonly Dictionary<SpeakerRole, ComboBox> selectors = new();
    private readonly BufferedMapPanel map = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 29, 42) };
    private readonly Label status = new() { Text = "Stopped", AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly Button start = new() { Text = "Start routing", Width = 130, Height = 34 };
    private readonly Button stop = new() { Text = "Stop routing", Width = 130, Height = 34, Enabled = false };
    private readonly Button refresh = new() { Text = "Refresh devices", Width = 130, Height = 34 };
    private readonly Button snapshot = new() { Text = "Audio backup", Width = 115, Height = 34 };
    private readonly Button testRearL = new() { Text = "Test rear L", Width = 105, Height = 34 };
    private readonly Button testRearR = new() { Text = "Test rear R", Width = 105, Height = 34 };
    private readonly CheckBox strictInput = new() { Text = "Diagnostic: no stereo upmix (rear silent for stereo)", AutoSize = true, ForeColor = Color.Khaki, Padding = new Padding(0, 9, 0, 8) };
    private readonly NotifyIcon tray;
    private readonly Settings settings;
    private bool exiting;
    private bool loading;
    private readonly string configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harmonia", "settings.json");

    private SpeakerLayout CurrentLayout => layout.SelectedIndex switch
    {
        1 => SpeakerLayout.TripleStereoCenter,
        2 => SpeakerLayout.Surround51Beta,
        _ => SpeakerLayout.DualStereo
    };

    public MainForm()
    {
        settings = ReadSettings();
        Text = "Harmonia — Speaker Layouts";
        Size = new Size(1000, 665);
        MinimumSize = new Size(840, 540);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 19, 29);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);
        Icon = SystemIcons.Application;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        var heading = new Label { Text = "HARMONIA     /     SPEAKER CONFIGURATION", Font = new Font("Segoe UI Semibold", 19), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);

        var leftSide = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.Controls.Add(new Label { Text = "SPEAKER LAYOUT", AutoSize = true, Padding = new Padding(0, 0, 0, 8), ForeColor = Color.LightSkyBlue }, 0, 0);
        layout.Items.AddRange(new object[] { "Dual monitor (stereo)", "Triple monitor (stereo + center)", "(Beta) Surround 5.1" });
        options.Controls.Add(layout, 0, 1);
        var note = new Label
        {
            Text = "Auto: 6-channel PCM passthrough when the capture endpoint provides 5.1; otherwise stereo upmix.\nLFE optional. Default capture endpoint cannot be used as an output yet.",
            ForeColor = Color.Silver, AutoSize = true, MaximumSize = new Size(350, 0), Padding = new Padding(0, 12, 0, 12)
        };
        options.Controls.Add(note, 0, 2);
        options.Controls.Add(new Label { Text = "ASSIGN OUTPUT DEVICES", ForeColor = Color.LightSkyBlue, AutoSize = true, Padding = new Padding(0, 12, 0, 7) }, 0, 3);
        assignmentsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        assignmentsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.Controls.Add(assignmentsPanel, 0, 4);
        options.RowCount = 6;
        options.Controls.Add(strictInput, 0, 5);
        leftSide.Controls.Add(options);
        root.Controls.Add(leftSide, 0, 1);

        var rightSide = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        rightSide.Controls.Add(map);
        root.Controls.Add(rightSide, 1, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12) };
        footer.Controls.Add(start);
        footer.Controls.Add(stop);
        footer.Controls.Add(refresh);
        footer.Controls.Add(snapshot);
        footer.Controls.Add(testRearL);
        footer.Controls.Add(testRearR);
        var statusLabel = new Label { Text = "Status:", AutoSize = true, Padding = new Padding(15, 8, 0, 0) };
        footer.Controls.Add(statusLabel);
        status.Padding = new Padding(0, 8, 0, 0);
        footer.Controls.Add(status);
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);
        Controls.Add(root);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Harmonia", null, (_, _) => Restore());
        menu.Items.Add("Stop routing", null, (_, _) => StopRouting());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        tray = new NotifyIcon { Text = "Harmonia", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Restore();

        layout.SelectedIndexChanged += (_, _) => { if (!loading) { BuildSelectors(); SaveSelection(); map.Invalidate(); } };
        start.Click += (_, _) => StartRouting();
        stop.Click += (_, _) => StopRouting();
        refresh.Click += (_, _) => BuildSelectors();
        snapshot.Click += (_, _) => ShowRecoverySnapshot();
        testRearL.Click += async (_, _) => await TestRearAsync(true);
        testRearR.Click += async (_, _) => await TestRearAsync(false);
        map.Paint += DrawSpeakerMap;
        meterTimer.Tick += (_, _) =>
        {
            var peaks = audio.GetSignalPeaks();
            for (int i = 0; i < displayLevels.Length; i++)
                displayLevels[i] = audio.IsRunning ? Math.Max(peaks[i], displayLevels[i] * 0.70f) : 0;
            if (Visible && WindowState != FormWindowState.Minimized) map.Invalidate();
        };
        meterTimer.Start();
        loading = true;
        layout.SelectedIndex = (int)settings.Layout;
        loading = false;
        BuildSelectors();
    }

    private Settings ReadSettings()
    {
        try { return File.Exists(configPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(configPath)) ?? new Settings() : new Settings(); }
        catch { return new Settings(); }
    }

    private void SaveSelection()
    {
        if (loading) return;
        settings.Layout = CurrentLayout;
        var key = CurrentLayout.ToString();
        var saved = settings.Assignments.TryGetValue(key, out var old) ? old : new Dictionary<string, string>();
        foreach (var (role, combo) in selectors)
        {
            if (combo.SelectedItem is DeviceChoice device) saved[role.ToString()] = device.Id;
            else saved.Remove(role.ToString());
        }
        settings.Assignments[key] = saved;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Preferences are optional. Never touch Windows device settings. */ }
    }

    private void BuildSelectors()
    {
        if (audio.IsRunning)
        {
            MessageBox.Show("Stop routing before changing the speaker layout or refreshing devices.", "Harmonia");
            loading = true;
            layout.SelectedIndex = (int)settings.Layout;
            loading = false;
            return;
        }
        loading = true;
        try
        {
            var defaultId = audio.DefaultDeviceId;
            var devices = audio.GetDevices()
                .Select(x => new DeviceChoice(x.Id, x.Name + (x.Id == defaultId ? " [Windows default — capture source]" : ""))).ToArray();
            assignmentsPanel.SuspendLayout();
            assignmentsPanel.Controls.Clear();
            assignmentsPanel.RowStyles.Clear();
            selectors.Clear();
            var saved = settings.Assignments.TryGetValue(CurrentLayout.ToString(), out var p) ? p : new Dictionary<string, string>();
            var roles = AudioEngine.Roles(CurrentLayout);
            assignmentsPanel.RowCount = roles.Length;
            for (int i = 0; i < roles.Length; i++)
            {
                var role = roles[i];
                assignmentsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
                var label = new Label { Text = RoleName(role), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gainsboro };
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 10, 4, 6) };
                if (role == SpeakerRole.Subwoofer) combo.Items.Add(new DeviceChoice("", "(None — optional)"));
                combo.Items.AddRange(devices);
                string? remembered = saved.GetValueOrDefault(role.ToString());
                combo.SelectedItem = combo.Items.Cast<DeviceChoice>().FirstOrDefault(x => x.Id == remembered);
                if (combo.SelectedIndex < 0 && (role == SpeakerRole.Subwoofer || CurrentLayout != SpeakerLayout.DualStereo))
                    combo.SelectedIndex = role == SpeakerRole.Subwoofer ? 0 : -1;
                else if (combo.SelectedIndex < 0 && i < devices.Length)
                    combo.SelectedItem = devices[i];
                combo.SelectedIndexChanged += (_, _) => SaveSelection();
                selectors[role] = combo;
                assignmentsPanel.Controls.Add(label, 0, i);
                assignmentsPanel.Controls.Add(combo, 1, i);
            }
            assignmentsPanel.ResumeLayout();
            map.Invalidate();
        }
        catch (Exception ex) { status.Text = "Device discovery failed"; MessageBox.Show(ex.Message, "Harmonia"); }
        finally { loading = false; }
    }

    private static string RoleName(SpeakerRole role) => role switch
    {
        SpeakerRole.FrontLeft => "Front left",
        SpeakerRole.FrontRight => "Front right",
        SpeakerRole.Center => "Center",
        SpeakerRole.Subwoofer => "Subwoofer (LFE)",
        SpeakerRole.RearStereo => "Rear stereo pair",
        _ => role.ToString()
    };

    private void ShowRecoverySnapshot()
    {
        try
        {
            var info = AudioRecovery.Save();
            MessageBox.Show(
                $"Audio snapshot saved to:\n{AudioRecovery.SnapshotPath}\n\n" +
                $"Default device: {info.DefaultDeviceName}\n" +
                $"Capture format: {info.MixChannels} channels, {info.MixSampleRate} Hz ({info.MixEncoding})\n\n" +
                "This is a read-only recovery record. It does not change or automatically restore Windows audio configuration. " +
                "Automatic audio setting changes and restoration are intentionally disabled until the system supports verified, crash-safe rollback.",
                "Harmonia audio backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void StartRouting()
    {
        var selected = new Dictionary<SpeakerRole, string>();
        foreach (var (role, combo) in selectors)
            if (combo.SelectedItem is DeviceChoice device && !string.IsNullOrWhiteSpace(device.Id))
                selected[role] = device.Id;
        try
        {
            audio.Start(CurrentLayout, selected, strictInput.Checked);
            SaveSelection();
            status.Text = audio.InputFormatDescription;
            start.Enabled = false;
            stop.Enabled = true;
            refresh.Enabled = false;
            layout.Enabled = false;
            strictInput.Enabled = false;
            foreach (var combo in selectors.Values) combo.Enabled = false;
        }
        catch (Exception ex)
        {
            status.Text = "Unable to start";
            MessageBox.Show(ex.Message + "\n\nIf your headphones are the default Windows audio device, they appear in the list but cannot be used as an output with the current endpoint-loopback engine. Avoid feedback by selecting a separate capture endpoint or wait for process-exclusion capture support.", "Harmonia", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task TestRearAsync(bool leftChannel)
    {
        if (audio.IsRunning) { MessageBox.Show("Stop routing before testing a speaker.", "Harmonia"); return; }
        if (!selectors.TryGetValue(SpeakerRole.RearStereo, out var selector) ||
            selector.SelectedItem is not DeviceChoice device || string.IsNullOrWhiteSpace(device.Id))
        { MessageBox.Show("Choose Surround 5.1 and assign your rear stereo headphones first.", "Harmonia"); return; }
        testRearL.Enabled = testRearR.Enabled = false;
        try { await SpeakerDiagnostics.PlayToneAsync(device.Id, leftChannel, !leftChannel); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Speaker test failed"); }
        finally { testRearL.Enabled = testRearR.Enabled = true; }
    }

    private void StopRouting()
    {
        audio.Stop();
        status.Text = "Stopped";
        start.Enabled = true;
        stop.Enabled = false;
        refresh.Enabled = true;
        layout.Enabled = true;
        strictInput.Enabled = true;
        foreach (var combo in selectors.Values) combo.Enabled = true;
    }

    private void DrawSpeakerMap(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = map.ClientSize.Width, h = map.ClientSize.Height;
        if (w < 40 || h < 40) return;
        using var titleFont = new Font("Segoe UI Semibold", 13);
        using var labelFont = new Font("Segoe UI", 9);
        using var thin = new Pen(Color.FromArgb(50, 122, 146, 174), 2) { DashStyle = DashStyle.Dash };
        using var ring = new Pen(Color.FromArgb(47, 150, 174, 205), 1.5f);
        using var speakerBrush = new SolidBrush(Color.FromArgb(58, 145, 230));
        using var listenerBrush = new SolidBrush(Color.FromArgb(227, 235, 246));
        g.DrawString("TOP-DOWN SPEAKER MAP", titleFont, Brushes.WhiteSmoke, 20, 14);
        float cx = w / 2, cy = h / 2 + 18;
        float rx = Math.Min(w * .38f, 180), ry = Math.Min(h * .34f, 165);
        g.DrawEllipse(ring, cx - rx, cy - ry, rx * 2, ry * 2);
        g.DrawEllipse(ring, cx - rx * .6f, cy - ry * .6f, rx * 1.2f, ry * 1.2f);

        void Meter(float x, float y, float peak)
        {
            // Four ascending Wi-Fi-style bars above the corresponding speaker.
            int active = peak < .012f ? 0 : peak < .055f ? 1 : peak < .17f ? 2 : peak < .40f ? 3 : 4;
            for (int i = 0; i < 4; i++)
            {
                var rect = new RectangleF(x - 15 + i * 9, y - 26 - (i + 1) * 5, 6, (i + 1) * 5);
                using var brush = new SolidBrush(i < active ? (i == 3 ? Color.FromArgb(255, 189, 79) : Color.FromArgb(80, 230, 162)) : Color.FromArgb(55, 77, 96));
                g.FillRectangle(brush, rect);
            }
        }

        void Speaker(float x, float y, string text, int meterIndex)
        {
            g.DrawLine(thin, cx, cy, x, y);
            var width = 45f;
            var rect = new RectangleF(x - width / 2, y - 16, width, 32);
            using var bg = new SolidBrush(Color.FromArgb(39, 84, 122));
            using var border = new Pen(Color.FromArgb(80, 171, 245), 2);
            g.FillRectangle(bg, rect);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            g.FillEllipse(speakerBrush, x - 5, y - 5, 10, 10);
            Meter(x, y - 10, displayLevels[meterIndex]);
            var label = g.MeasureString(text, labelFont);
            g.DrawString(text, labelFont, Brushes.Gainsboro, x - label.Width / 2, y + 19);
        }

        Speaker(cx - rx * .80f, cy - ry * .75f, "L", 0);
        Speaker(cx + rx * .80f, cy - ry * .75f, "R", 1);
        if (CurrentLayout != SpeakerLayout.DualStereo)
            Speaker(cx, cy - ry, "Center", 2);
        if (CurrentLayout == SpeakerLayout.Surround51Beta)
        {
            Speaker(cx - rx * .69f, cy + ry * .82f, "Rear L", 4);
            Speaker(cx + rx * .69f, cy + ry * .82f, "Rear R", 5);
            Speaker(cx + rx * 1.02f, cy - ry * .12f, "LFE", 3);
        }
        g.FillEllipse(listenerBrush, cx - 12, cy - 15, 24, 24);
        using var body = new Pen(listenerBrush, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(body, cx, cy + 12, cx, cy + 28);
        var label2 = g.MeasureString("LISTENER", labelFont);
        g.DrawString("LISTENER", labelFont, Brushes.Gainsboro, cx - label2.Width / 2, cy + 34);
    }

    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Quit() { exiting = true; Close(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        meterTimer.Stop();
        meterTimer.Dispose();
        tray.Visible = false;
        tray.Dispose();
        audio.Dispose();
        base.OnFormClosed(e);
    }
}
