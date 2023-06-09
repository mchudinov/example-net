using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Debugging;

namespace ExampleNetConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Default", LogEventLevel.Debug)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            SelfLog.Enable(Console.Error);

            try
            {
                var host = Host.CreateApplicationBuilder();

                host.Services.AddLogging(config =>
                {
                    config.ClearProviders();

                    Logger logger = new LoggerConfiguration()
                        .ReadFrom.Configuration(host.Configuration)
                        .CreateLogger();

                    config.AddSerilog(logger);
                });

                host.Services.Configure<Config>(host.Configuration);

                host.Services.AddHostedService<TimedHostedService>();                

                host.Build().Run();          
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}