using System;

namespace VideoPostOrganizer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
  ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new MainForm());
    }
}
