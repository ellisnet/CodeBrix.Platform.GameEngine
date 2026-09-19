using System;
using CodeBrix.Platform.UI.Hosting;

namespace KenneyAssetsDemo;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = CodeBrixPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWindowsWin32()
            .Build();

        host.Run();
    }
}
