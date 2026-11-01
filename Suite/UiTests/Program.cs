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
        using var form = (Form)Activator.CreateInstance(type, [directory, (Func<(string Id, string Name)[]>)(() => [("available-left", "Test left output"), ("available-right", "Test right output")])])!;
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
            var map = (Panel)Field("map"); map.ClientSize = new System.Drawing.Size(500, 400);
            using (var preview = new System.Drawing.Bitmap(500, 400))
            using (var graphics = System.Drawing.Graphics.FromImage(preview))
                type.GetMethod("DrawSpeakerMap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form,
                    [map, new PaintEventArgs(graphics, new System.Drawing.Rectangle(0, 0, 500, 400))]);
            var targets = (System.Collections.IDictionary)Field("dropTargets");
            var role = targets.Keys.Cast<object>().Single(key => key.ToString() == "FrontLeft");
            var rectangle = (System.Drawing.RectangleF)targets[role]!;
            var point = map.PointToScreen(new System.Drawing.Point((int)(rectangle.X + rectangle.Width / 2), (int)(rectangle.Y + rectangle.Height / 2)));
            var data = new DataObject(); data.SetData(typeof(string), "available-left");
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map,
                [new DragEventArgs(data, 0, point.X, point.Y, DragDropEffects.Move, DragDropEffects.Move)]);
            using var afterDrop = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json")));
            Check(afterDrop.RootElement.GetProperty("Assignments").GetProperty("Surround51Beta").GetProperty("FrontLeft").GetString() == "available-left", "Drag/drop must persist assignment");
            Console.WriteLine("PASS: full layouts, virtual default, actual map drop/persistence, gains, unplugged mappings and Suite tray.");
        } finally {
            ((System.Windows.Forms.Timer)Field("meterTimer")).Dispose();
            ((NotifyIcon)Field("tray")).Dispose();
            ((IDisposable)Field("audio")).Dispose();
            Directory.Delete(directory, true);
        }
    }
}
