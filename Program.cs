using System.Windows.Forms;

namespace Harmonia;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LiteForm());
    }
}
