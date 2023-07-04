using Serilog;
using Serilog.Events;
using Serilog.Debugging;
using Elastic.Apm.Extensions.Hosting;
using System.Collections;

namespace ExampleNet
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
                Serilog.Log.Logger.Information("Example .NET application is running");
                Serilog.Log.Logger.Information($".NET Version: {Environment.Version.ToString()}");
                OutputEnvironmentVariables();

                var enviroment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile($"appsettings.{enviroment}.json", optional: true)
                    .Build();
                var settings = configuration.GetRequiredSection("Settings").Get<Settings>();               

                IHostBuilder hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                    .ConfigureHostConfiguration(configBuilder => 
                    {
                        configBuilder.AddConfiguration(configuration);
                    })
                    .ConfigureLogging(loggingBuilder =>
                    {
                        loggingBuilder.ClearProviders();
                        Serilog.Log.Logger = new LoggerConfiguration()
                            .ReadFrom.Configuration(configuration)
                            .CreateLogger();
                        loggingBuilder.AddSerilog();
                    })
                    .ConfigureServices(services =>
                    { 
                        services.AddSingleton<Settings>((settings is null) ? new Settings() : settings);                        
                        services.Configure<HostOptions>(options =>
                        {
                            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
                            options.ShutdownTimeout = TimeSpan.FromSeconds(DefaultTimeOutSec);
                        });
                        services.AddHostedService<TimedHostedService>();
                        services.AddSingleton<StartupReadyCheck>();
                        services.AddHealthChecks().AddCheck<StartupReadyCheck>("StartupReadyCheck");
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.Configure(appBuilder =>
                        {
                            appBuilder.UseRouting();
                            appBuilder.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/", async context =>
                                {
                                    await context.Response.WriteAsync("Ok");
                                });
                            });
                            appBuilder.UseHealthChecks("/healthz");
                            appBuilder.UseHealthChecks("/readyz");
                            appBuilder.UseHealthChecks("/livez");
                        });
                    });

                hostBuilder.UseElasticApm();

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

        private static void OutputEnvironmentVariables()
        {
            var envVars = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>();
            envVars = envVars.OrderBy(x => (string)x.Key);

            foreach (var e in envVars)
            {
                Serilog.Log.Information("{variabel}:{value}", e.Key, e.Value);
            }
        }
    }
}