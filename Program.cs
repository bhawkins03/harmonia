using System.Windows.Forms;

namespace Harmonia;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Length > 0 && args[0] == "--lite-apply-update")
        {
            Environment.ExitCode = LiteUpdates.Apply(args);
            return;
        }
        Application.Run(new MainForm());
    }
}
