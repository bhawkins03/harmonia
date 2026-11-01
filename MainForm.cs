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
    private readonly Dictionary<SpeakerRole, string> mappedDevices = new();
    private readonly Dictionary<string, string> deviceNames = new();
    private readonly FlowLayoutPanel deviceTiles = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.TopDown };
    private readonly Dictionary<SpeakerRole, RectangleF> dropTargets = new();
    private SpeakerRole? hoveringRole;
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
        var tilesGroup = new GroupBox { Text = "Available audio devices — drag onto map", ForeColor = Color.Gainsboro, Height = 220, Dock = DockStyle.Top };
        tilesGroup.Controls.Add(deviceTiles);
        options.Controls.Add(tilesGroup, 0, 4);
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
        map.AllowDrop = true;
        map.DragEnter += (_, e) => { if (!audio.IsRunning && e.Data?.GetDataPresent(typeof(string)) == true) e.Effect = DragDropEffects.Move; };
        map.DragOver += (_, e) =>
        {
            var point = map.PointToClient(new Point(e.X, e.Y));
            hoveringRole = HitTestRole(point);
            e.Effect = !audio.IsRunning && hoveringRole.HasValue && e.Data?.GetDataPresent(typeof(string)) == true
                ? DragDropEffects.Move : DragDropEffects.None;
            map.Invalidate();
        };
        map.DragLeave += (_, _) => { hoveringRole = null; map.Invalidate(); };
        map.DragDrop += (_, e) =>
        {
            var role = HitTestRole(map.PointToClient(new Point(e.X, e.Y)));
            hoveringRole = null;
            if (audio.IsRunning || role is null || e.Data?.GetData(typeof(string)) is not string id) return;
            if (!deviceNames.ContainsKey(id)) return;
            // One physical endpoint per role. The rear pair uses one stereo endpoint.
            foreach (var assigned in mappedDevices.Where(x => x.Value == id).Select(x => x.Key).ToArray()) mappedDevices.Remove(assigned);
            mappedDevices[role.Value] = id;
            SaveSelection();
            map.Invalidate();
        };
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
        settings.Assignments[CurrentLayout.ToString()] = mappedDevices.ToDictionary(x => x.Key.ToString(), x => x.Value);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void BuildSelectors()
    {
        if (audio.IsRunning)
        {
            MessageBox.Show("Stop routing before changing the layout.", "Harmonia");
            loading = true;
            layout.SelectedIndex = (int)settings.Layout;
            loading = false;
            return;
        }
        loading = true;
        try
        {
            deviceNames.Clear();
            deviceTiles.Controls.Clear();
            var defaultId = audio.DefaultDeviceId;
            foreach (var device in audio.GetDevices())
            {
                deviceNames[device.Id] = device.Name;
                var tile = new Label
                {
                    Text = device.Name + (device.Id == defaultId ? " (source)" : ""),
                    Tag = device.Id,
                    Width = 295,
                    Height = 43,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(39, 64, 94),
                    ForeColor = Color.WhiteSmoke,
                    Padding = new Padding(8),
                    Cursor = Cursors.Hand
                };
                tile.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left && !audio.IsRunning)
                        tile.DoDragDrop((string)tile.Tag!, DragDropEffects.Move);
                };
                deviceTiles.Controls.Add(tile);
            }
            mappedDevices.Clear();
            if (settings.Assignments.TryGetValue(CurrentLayout.ToString(), out var saved))
                foreach (var entry in saved)
                    if (Enum.TryParse<SpeakerRole>(entry.Key, out var role) &&
                        AudioEngine.Roles(CurrentLayout).Contains(role) &&
                        deviceNames.ContainsKey(entry.Value))
                        mappedDevices[role] = entry.Value;
            map.Invalidate();
        }
        catch (Exception ex) { status.Text = "Device discovery failed"; MessageBox.Show(ex.Message, "Harmonia"); }
        finally { loading = false; }
    }

    private SpeakerRole? HitTestRole(Point location)
    {
        foreach (var entry in dropTargets)
            if (entry.Value.Contains(location)) return entry.Key;
        return null;
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
        var selected = new Dictionary<SpeakerRole, string>(mappedDevices);
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
            deviceTiles.Enabled = false;
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
        if (!mappedDevices.TryGetValue(SpeakerRole.RearStereo, out var rearDeviceId))
        { MessageBox.Show("Choose Surround 5.1 and assign your rear stereo headphones first.", "Harmonia"); return; }
        testRearL.Enabled = testRearR.Enabled = false;
        try { await SpeakerDiagnostics.PlayToneAsync(rearDeviceId, leftChannel, !leftChannel); }
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
        deviceTiles.Enabled = true;
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
        g.DrawString("TOP-DOWN SPEAKER MAP — DRAG TO ASSIGN", titleFont, Brushes.WhiteSmoke, 20, 14);
        dropTargets.Clear();
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

        void Speaker(float x, float y, string text, int meterIndex, SpeakerRole role)
        {
            g.DrawLine(thin, cx, cy, x, y);
            var width = 45f;
            var rect = new RectangleF(x - width / 2, y - 16, width, 32);
            dropTargets[role] = new RectangleF(x - 41, y - 36, 82, 78);
            using var bg = new SolidBrush(Color.FromArgb(39, 84, 122));
            using var border = new Pen(hoveringRole == role ? Color.Gold : mappedDevices.ContainsKey(role) ? Color.LimeGreen : Color.FromArgb(80, 171, 245), 2);
            g.FillRectangle(bg, rect);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            g.FillEllipse(speakerBrush, x - 5, y - 5, 10, 10);
            Meter(x, y - 10, displayLevels[meterIndex]);
            var label = g.MeasureString(text, labelFont);
            g.DrawString(text, labelFont, Brushes.Gainsboro, x - label.Width / 2, y + 19);
            string assigned = mappedDevices.TryGetValue(role, out var deviceId) && deviceNames.TryGetValue(deviceId, out var name)
                ? name : "Drop device here";
            if (assigned.Length > 22) assigned = assigned[..19] + "...";
            var nameWidth = g.MeasureString(assigned, labelFont).Width;
            g.DrawString(assigned, labelFont, Brushes.LightSkyBlue, x - nameWidth / 2, y + 34);
        }

        Speaker(cx - rx * .80f, cy - ry * .75f, "L", 0, SpeakerRole.FrontLeft);
        Speaker(cx + rx * .80f, cy - ry * .75f, "R", 1, SpeakerRole.FrontRight);
        if (CurrentLayout != SpeakerLayout.DualStereo)
            Speaker(cx, cy - ry, "Center", 2, SpeakerRole.Center);
        if (CurrentLayout == SpeakerLayout.Surround51Beta)
        {
            Speaker(cx - rx * .69f, cy + ry * .82f, "Rear L", 4, SpeakerRole.RearStereo);
            Speaker(cx + rx * .69f, cy + ry * .82f, "Rear R", 5, SpeakerRole.RearStereo);
            Speaker(cx + rx * 1.02f, cy - ry * .12f, "LFE", 3, SpeakerRole.Subwoofer);
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
