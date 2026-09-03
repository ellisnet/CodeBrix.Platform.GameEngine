using CodeBrix.Platform.UI.Hosting;
using System;

namespace SpaceDuel.Brix;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = CodeBrixPlatformHostBuilder.Create()
            .App(() => new App())
            .UseLinuxX11()
            .Build();

        host.Run();
    }
}
