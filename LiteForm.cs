using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace Harmonia;

internal sealed class LiteForm : Form
{
    private sealed class Preferences
    {
        public string LeftId { get; set; } = "";
        public string RightId { get; set; } = "";
        public int LeftVolume { get; set; } = 100;
        public int RightVolume { get; set; } = 100;
    }

    private sealed record Device(string Id, string Name) { public override string ToString() => Name; }
    private readonly LiteStereoEngine engine = new();
    private readonly ComboBox left = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 345 };
    private readonly ComboBox right = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 345 };
    private readonly TrackBar leftVolume = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 25, Width = 345 };
    private readonly TrackBar rightVolume = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 25, Width = 345 };
    private readonly Label status = new() { Text = "Ready", AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly Label meters = new() { Text = "L  ░░░░░░░░░░    R  ░░░░░░░░░░", ForeColor = Color.LightSkyBlue, AutoSize = true };
    private readonly Button start = new() { Text = "Start routing", Width = 140, Height = 38 };
    private readonly Button stop = new() { Text = "Stop", Width = 92, Height = 38, Enabled = false };
    private readonly Button refresh = new() { Text = "Refresh devices", Width = 145, Height = 38 };
    private readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 150 };
    private readonly NotifyIcon tray;
    private readonly Image brandImage;
    private readonly string configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harmonia", "lite-settings.json");
    private bool quitting;
    private bool loading;
    private Preferences preferences = new();

    public LiteForm()
    {
        Text = "Harmonia Lite — Stereo Routing";
        Size = new Size(800, 565);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 19, 29);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? SystemIcons.Application;
        using (var stream = typeof(LiteForm).Assembly.GetManifestResourceStream("Harmonia.Assets.harmonia-logo.png")
            ?? throw new InvalidOperationException("Missing Harmonia logo resource."))
        using (var image = new Bitmap(stream)) brandImage = new Bitmap(image);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(25), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        header.Controls.Add(new PictureBox { Image = brandImage, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(70, 70), Margin = new Padding(0, 0, 12, 0) });
        header.Controls.Add(new Label { Text = "HARMONIA  /  LITE", AutoSize = true, Font = new Font("Segoe UI Semibold", 21), Padding = new Padding(0, 17, 0, 0) });
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Route Windows stereo audio to two separate devices.\nEach device plays its assigned left or right channel.",
            AutoSize = true, ForeColor = Color.Silver, Dock = DockStyle.Fill
        }, 0, 1);

        var inputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inputs.Controls.Add(new Label { Text = "LEFT SPEAKER / DEVICE", AutoSize = true, ForeColor = Color.LightSkyBlue }, 0, 0);
        inputs.Controls.Add(new Label { Text = "RIGHT SPEAKER / DEVICE", AutoSize = true, ForeColor = Color.LightSkyBlue }, 1, 0);
        inputs.Controls.Add(left, 0, 1);
        inputs.Controls.Add(right, 1, 1);
        inputs.Controls.Add(new Label { Text = "Left volume", AutoSize = true, ForeColor = Color.Gainsboro }, 0, 2);
        inputs.Controls.Add(new Label { Text = "Right volume", AutoSize = true, ForeColor = Color.Gainsboro }, 1, 2);
        inputs.Controls.Add(leftVolume, 0, 3);
        inputs.Controls.Add(rightVolume, 1, 3);
        root.Controls.Add(inputs, 0, 2);
        root.Controls.Add(new Label {
            Text = "The Windows default playback device is the capture source, not an available output.\nChange your default audio device if you need to route to it.",
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Silver
        }, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        footer.Controls.Add(start);
        footer.Controls.Add(stop);
        footer.Controls.Add(refresh);
        status.Padding = new Padding(12, 9, 0, 0);
        footer.Controls.Add(status);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Lite", null, (_, _) => Restore());
        menu.Items.Add("Stop routing", null, (_, _) => StopRouting());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { quitting = true; Close(); });
        tray = new NotifyIcon { Text = "Harmonia Lite", Icon = Icon, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Restore();

        preferences = ReadPreferences();
        loading = true;
        leftVolume.Value = Math.Clamp(preferences.LeftVolume, 0, 100);
        rightVolume.Value = Math.Clamp(preferences.RightVolume, 0, 100);
        loading = false;
        engine.LeftGain = leftVolume.Value / 100f;
        engine.RightGain = rightVolume.Value / 100f;
        RefreshDevices();
        left.SelectedIndexChanged += (_, _) => SavePreferences();
        right.SelectedIndexChanged += (_, _) => SavePreferences();
        leftVolume.Scroll += (_, _) => { engine.LeftGain = leftVolume.Value / 100f; SavePreferences(); };
        rightVolume.Scroll += (_, _) => { engine.RightGain = rightVolume.Value / 100f; SavePreferences(); };
        start.Click += (_, _) => StartRouting();
        stop.Click += (_, _) => StopRouting();
        refresh.Click += (_, _) => RefreshDevices();
        meterTimer.Tick += (_, _) =>
        {
            if (!engine.IsRunning) return;
            var peaks = engine.Peaks;
            status.Text = $"Routing   L {Math.Min(100, (int)(peaks.Left * 100))}%   R {Math.Min(100, (int)(peaks.Right * 100))}%";
        };
        meterTimer.Start();
    }

    private Preferences ReadPreferences()
    {
        try { return File.Exists(configPath) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(configPath)) ?? new() : new(); }
        catch { return new(); }
    }

    private void SavePreferences()
    {
        if (loading) return;
        preferences.LeftId = (left.SelectedItem as Device)?.Id ?? "";
        preferences.RightId = (right.SelectedItem as Device)?.Id ?? "";
        preferences.LeftVolume = leftVolume.Value;
        preferences.RightVolume = rightVolume.Value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Failure to save must not break playback. */ }
    }

    private void RefreshDevices()
    {
        if (engine.IsRunning) return;
        loading = true;
        try
        {
            var available = engine.GetDevices().Where(d => d.Id != engine.DefaultDeviceId).ToArray();
            left.Items.Clear(); right.Items.Clear();
            foreach (var device in available)
            {
                left.Items.Add(new Device(device.Id, device.Name));
                right.Items.Add(new Device(device.Id, device.Name));
            }
            for (int i = 0; i < left.Items.Count; i++)
            {
                if (((Device)left.Items[i]!).Id == preferences.LeftId) left.SelectedIndex = i;
                if (((Device)right.Items[i]!).Id == preferences.RightId) right.SelectedIndex = i;
            }
            if (left.SelectedIndex < 0 && left.Items.Count > 0) left.SelectedIndex = 0;
            if (right.SelectedIndex < 0 && right.Items.Count > 1) right.SelectedIndex = 1;
            status.Text = available.Length < 2 ? "Two available non-default outputs required" : "Ready";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Device discovery", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { loading = false; }
        SavePreferences();
    }

    private void StartRouting()
    {
        try
        {
            engine.Start((left.SelectedItem as Device)?.Id ?? "", (right.SelectedItem as Device)?.Id ?? "");
            SavePreferences();
            status.Text = engine.Description;
            left.Enabled = right.Enabled = refresh.Enabled = start.Enabled = false;
            stop.Enabled = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot start routing", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void StopRouting()
    {
        engine.Stop();
        status.Text = "Stopped";
        left.Enabled = right.Enabled = refresh.Enabled = start.Enabled = true;
        stop.Enabled = false;
    }

    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        meterTimer.Stop(); meterTimer.Dispose();
        tray.Visible = false; tray.Dispose();
        engine.Dispose(); brandImage.Dispose();
        base.OnFormClosed(e);
    }
}
