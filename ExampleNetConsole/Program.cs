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

namespace ExampleNetConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Default", LogEventLevel.Debug)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            SelfLog.Enable(Console.Error);

            try
            {
                var hostAppBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder();// CreateApplicationBuilder();

                var enviroment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                hostAppBuilder.Configuration.AddJsonFile($"appsettings.{enviroment}.json", optional: true);
                hostAppBuilder.Services.Configure<Settings>(hostAppBuilder.Configuration);
                
                Logger serilog = new LoggerConfiguration()
                        .ReadFrom.Configuration(hostAppBuilder.Configuration)
                        .CreateLogger();
                hostAppBuilder.Logging.ClearProviders();
                hostAppBuilder.Logging.AddSerilog(serilog);

                hostAppBuilder.Services.AddHostedService<TimedHostedService>();


                //hostAppBuilder.Services.AddLogging(config =>
                //{
                //    config.ClearProviders();

                //    Logger logger = new LoggerConfiguration()
                //        .ReadFrom.Configuration(hostAppBuilder.Configuration)
                //        .CreateLogger();

                //    config.AddSerilog(logger);
                //});

                Settings settings = new();
                hostAppBuilder.Configuration.Bind(settings);
                serilog.Debug("Settings = {settings}", settings);

                hostAppBuilder.Build().Run();
            }
            catch (Exception ex)
            {
                Serilog.Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Serilog.Log.CloseAndFlush();
            }
        }

        private static void test()
        {
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseAllElasticApm();
            var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder();
            builder.UseAllElasticApm();
        }
    }
}