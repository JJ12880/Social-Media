using System;
using WinFormsApplication = System.Windows.Forms.Application;

namespace VideoPostOrganizer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.ApplicationConfiguration.Initialize();
        WinFormsApplication.Run(new MainForm());
    }
}
