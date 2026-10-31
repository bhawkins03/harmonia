using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace Harmonia;

internal sealed class MainForm : Form
{
    private readonly AudioEngine audio = new();
    private readonly ComboBox left = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox right = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label status = new() { Text = "Stopped", AutoSize = true, ForeColor = Color.DarkSlateGray };
    private readonly Button start = new() { Text = "Start routing", AutoSize = true };
    private readonly Button stop = new() { Text = "Stop routing", AutoSize = true, Enabled = false };
    private readonly NotifyIcon tray;
    private bool exiting;
    private readonly string configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Harmonia", "settings.json");

    public MainForm()
    {
        Text = "Harmonia";
        Size = new Size(525, 305);
        MinimumSize = new Size(480, 290);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        Icon = SystemIcons.Application;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 5 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int n = 0; n < 5; n++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, n == 4 ? 65 : 38));
        panel.Controls.Add(new Label { Text = "Left monitor", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        panel.Controls.Add(left, 1, 0);
        panel.Controls.Add(new Label { Text = "Right monitor", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        panel.Controls.Add(right, 1, 1);
        panel.Controls.Add(new Label { Text = "Status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        panel.Controls.Add(status, 1, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(start);
        buttons.Controls.Add(stop);
        var refresh = new Button { Text = "Refresh devices", AutoSize = true };
        buttons.Controls.Add(refresh);
        panel.Controls.Add(buttons, 1, 3);
        panel.SetColumnSpan(buttons, 1);
        var hint = new Label { Text = "Close window = minimize to tray. Use tray menu → Exit to stop Harmonia.\nWindows audio device settings are never changed.", Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = false };
        panel.Controls.Add(hint, 0, 4);
        panel.SetColumnSpan(hint, 2);
        Controls.Add(panel);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Harmonia", null, (_, _) => Restore());
        menu.Items.Add("Stop routing", null, (_, _) => StopRouting());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        tray = new NotifyIcon { Text = "Harmonia", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Restore();

        start.Click += (_, _) => StartRouting();
        stop.Click += (_, _) => StopRouting();
        refresh.Click += (_, _) => LoadDevices();
        left.SelectedIndexChanged += (_, _) => SaveSelection();
        right.SelectedIndexChanged += (_, _) => SaveSelection();
        LoadDevices();
    }

    private sealed record DeviceChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record Settings(string? LeftDeviceId = null, string? RightDeviceId = null);

    private Settings ReadSettings()
    {
        try { return File.Exists(configPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(configPath)) ?? new Settings() : new Settings(); }
        catch { return new Settings(); }
    }

    private void SaveSelection()
    {
        if (left.SelectedItem is not DeviceChoice l || right.SelectedItem is not DeviceChoice r) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(new Settings(l.Id, r.Id)));
        }
        catch { /* Settings are optional; routing continues without persistence. */ }
    }

    private void LoadDevices()
    {
        if (audio.IsRunning)
        {
            MessageBox.Show("Stop routing before refreshing devices.", "Harmonia");
            return;
        }
        try
        {
            var saved = ReadSettings();
            var defaultId = audio.DefaultDeviceId;
            var devices = audio.GetDevices().Where(x => x.Id != defaultId).Select(x => new DeviceChoice(x.Id, x.Name)).ToArray();
            left.Items.Clear();
            right.Items.Clear();
            left.Items.AddRange(devices);
            right.Items.AddRange(devices);
            if (devices.Length > 0)
            {
                left.SelectedItem = devices.FirstOrDefault(x => x.Id == saved.LeftDeviceId) ?? devices[0];
                right.SelectedItem = devices.FirstOrDefault(x => x.Id == saved.RightDeviceId) ?? devices[Math.Min(1, devices.Length - 1)];
            }
        }
        catch (Exception ex) { status.Text = "Device discovery failed"; MessageBox.Show(ex.Message, "Harmonia"); }
    }

    private void StartRouting()
    {
        if (left.SelectedItem is not DeviceChoice l || right.SelectedItem is not DeviceChoice r)
        {
            MessageBox.Show("Select both audio monitors.", "Harmonia"); return;
        }
        try
        {
            audio.Start(l.Id, r.Id);
            SaveSelection();
            status.Text = "Routing active";
            start.Enabled = false;
            stop.Enabled = true;
            left.Enabled = right.Enabled = false;
        }
        catch (Exception ex) { status.Text = "Unable to start"; MessageBox.Show(ex.Message, "Harmonia", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void StopRouting()
    {
        audio.Stop();
        status.Text = "Stopped";
        start.Enabled = true;
        stop.Enabled = false;
        left.Enabled = right.Enabled = true;
    }

    private void Restore()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        exiting = true;
        Close();
    }

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
        tray.Visible = false;
        tray.Dispose();
        audio.Dispose();
        base.OnFormClosed(e);
    }
}
