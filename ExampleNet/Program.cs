using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Microsoft.Extensions.Logging;
using Serilog.Debugging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Elastic.Apm.Extensions.Hosting;

namespace ExampleNetConsole
{
    internal sealed class Program
    {
        private const int DefaultTimeOutSec = 5;

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
                    .AddJsonFile($"appsettings.{enviroment}.json", optional: true)
                    .Build();
                var settings = configuration.GetRequiredSection("Settings").Get<Settings>();               

                IHostBuilder hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
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
                        services.AddSingleton<Settings>((settings is null) ? new Settings() : settings);
                        services.Configure<HostOptions>(options =>
                        {
                            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
                            options.ShutdownTimeout = TimeSpan.FromSeconds((settings is null) ? DefaultTimeOutSec : settings.ShutdownTimeout);
                        });
                        services.AddHostedService<TimedHostedService>();
                    });

                    if ((settings is not null) && settings.UseApm) hostBuilder.UseElasticApm();

                    using var host = hostBuilder.Build();

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