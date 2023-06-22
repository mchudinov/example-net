using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExampleNet
{
    public class TimedHostedService : IHostedService, IDisposable
    {
        private const int DefaultTimeSpanSec = 5;
        private ulong _executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private readonly Settings? _settings = new();
        private Timer? _timer = null;
        static readonly Random _random = new();
        static readonly System.Diagnostics.Stopwatch _watch = new();
        private readonly StartupReadyCheck _readyCheck;

        public TimedHostedService(ILogger<TimedHostedService> logger, IConfiguration configuration, StartupReadyCheck readyCheck)
        {
            _settings = configuration
                .GetRequiredSection("Settings")
                .Get<Settings>();

            _logger = logger;
            _readyCheck = readyCheck;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            try
            {
                _watch.Start();

                _logger.LogInformation("Timed Hosted Service is running.");
                _logger.LogInformation("TimedSpan = {TimeSpan}", _settings?.TimeSpan);

                _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds((_settings is null) ? DefaultTimeSpanSec : _settings.TimeSpan));

                OutputEnvironmentVariables();

                _readyCheck.StartupCompleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, ex.Message);
            }

            return Task.CompletedTask;
        }

        private void DoWork(object? state)
        {
            try
            {
                var count = Interlocked.Increment(ref _executionCount);
                var logLvl = RandomEnumValue<LogLevel>();
                var data = new Data(DateTime.Now.Second); 
                var period = _watch.Elapsed.ToString(@"d\d\,\ hh\:mm\:ss");
                _logger.Log(logLvl, "LogLevel={LogLevel} Count={Count} Uptime={Uptime}s Period={Period} Data={@Data}",
                    logLvl, count, _watch.Elapsed.TotalSeconds, period, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, ex.Message);
            }
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
        }

        private void OutputEnvironmentVariables()
        {
            var envVars = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>();
            envVars = envVars.OrderBy(x => (string)x.Key);
            foreach (var e in envVars)
            {
                _logger.LogInformation("{variabel}:{value}", e.Key, e.Value);
            }
        }

        static T RandomEnumValue<T>() where T : struct, Enum
        {
            var v = Enum.GetValues(typeof(T));
            return (v is null) ? default : (T)v.GetValue(_random.Next(v.Length));
        }
    }

    internal class Data
    {
        public Data(int seconds)
        {
            Seconds = seconds;
            ValueCos = Math.Round(Math.Cos(seconds)*1000);
            ValueSin = Math.Round(Math.Sin(seconds)*1000);
        }

        public int Seconds { get; private set; }

        public double ValueCos { get; private set; }

        public double ValueSin { get; private set; }
    }
}
