using Serilog;
using Serilog.Events;
using Serilog.Debugging;
using Elastic.Apm.Extensions.Hosting;

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
                            options.ShutdownTimeout = TimeSpan.FromSeconds((settings is null) ? DefaultTimeOutSec : settings.ShutdownTimeout);
                        });
                        services.AddHostedService<TimedHostedService>();
                        services.AddSingleton<StartupReadyCheck>();
                        services.AddHealthChecks().AddCheck<StartupReadyCheck>("StartupReadyCheck");
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/", async context =>
                                {
                                    await context.Response.WriteAsync("Ok");
                                });
                            });
                            app.UseHealthChecks("/healthz");
                            app.UseHealthChecks("/readyz");
                            app.UseHealthChecks("/livez");
                        });
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