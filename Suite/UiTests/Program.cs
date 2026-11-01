using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program {
    [STAThread]
    private static void Main() {
        string directory = Path.Combine(Path.GetTempPath(), "HarmoniaUiTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), """
            {"Layout":2,"Assignments":{"Surround51Beta":{"FrontLeft":"disconnected-left","RearStereo":"remembered-headphones"}},"Gains":{"RearStereo":0.7},"CheckSuiteUpdates":false}
            """);
        var type = Assembly.Load("Harmonia").GetType("Harmonia.MainForm")!;
        using var form = (Form)Activator.CreateInstance(type, [directory, (Func<(string Id, string Name)[]>)(() => [])])!;
        object Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
        try {
            Check(((ComboBox)Field("layout")).Items.Count == 3, "Full layout selector");
            Check(((ComboBox)Field("inputBackend")).SelectedIndex == 0, "Virtual input must be primary");
            Check(((Panel)Field("map")).AllowDrop, "Map drag/drop preserved");
            var gains = (FlowLayoutPanel)Field("gainControls"); Check(gains.Controls.Count == 5, "5.1 gains");
            var rear = (NumericUpDown)gains.Controls[4].Controls[1]; Check(rear.Value == 70, "Saved gain");
            rear.Value = 55;
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json")));
            Check(saved.RootElement.GetProperty("Gains").GetProperty("RearStereo").GetSingle() == .55f, "Persist changed gain");
            Check(saved.RootElement.GetProperty("Assignments").GetProperty("Surround51Beta").GetProperty("RearStereo").GetString() == "remembered-headphones", "Preserve unplugged mappings");
            Check(((NotifyIcon)Field("tray")).Text == "Harmonia Suite", "Suite tray identity");
            Console.WriteLine("PASS: full layouts, virtual default, drag/drop, gains, persistent unplugged mappings and Suite tray.");
        } finally {
            ((System.Windows.Forms.Timer)Field("meterTimer")).Dispose();
            ((NotifyIcon)Field("tray")).Dispose();
            ((IDisposable)Field("audio")).Dispose();
            Directory.Delete(directory, true);
        }
    }
}
