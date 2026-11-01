using System.Windows.Forms;

namespace Harmonia;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
#if HARMONIA_LITE
        Application.Run(new LiteForm());
#else
        Application.Run(new MainForm());
#endif
    }
}
