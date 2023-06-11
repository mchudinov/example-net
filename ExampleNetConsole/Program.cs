using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Debugging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Elastic.Apm.NetCoreAll;
using Elastic.CommonSchema;
using System.Configuration;
using Elastic.Apm.Config;
using Elastic.Apm.Api;

namespace ExampleNetConsole
{
    internal sealed class Program
    {
        static async Task Main(string[] args)
        {
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Default", LogEventLevel.Debug)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            SelfLog.Enable(Console.Error);

            try
            {
                var enviroment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile($"appsettings.{enviroment}.json", optional: false)
                    .Build();
                var settings = configuration
                    .GetRequiredSection("Settings")
                    .Get<Settings>();               

                using IHost host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                    .ConfigureHostConfiguration(builder => 
                    {
                        builder.AddConfiguration(configuration);
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        Serilog.Log.Logger = new LoggerConfiguration()
                            .ReadFrom.Configuration(configuration)
                            .CreateLogger();
                        logging.AddSerilog();
                    })
                    .ConfigureServices(services =>
                    { 
                        services.AddSingleton<Settings>(settings);
                        services.Configure<HostOptions>(options =>
                        {
                            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
                            options.ShutdownTimeout = TimeSpan.FromSeconds(settings.ShutdownTimeout);
                        });
                        services.AddHostedService<TimedHostedService>();
                    })
                    .UseAllElasticApm()
                    .Build();

                await host.RunAsync();            
            }
            catch (Exception ex)
            {
                Serilog.Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Serilog.Log.Information("Shut down complete");
                Serilog.Log.CloseAndFlush();
            }
        }
    }
}