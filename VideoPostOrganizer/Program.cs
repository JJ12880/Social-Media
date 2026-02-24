using System;

namespace VideoPostOrganizer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new MainForm());
    }
}
