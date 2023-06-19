using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExampleNetConsole
{
    public class TimedHostedService : IHostedService, IDisposable
    {
        private const int DefaultTimeSpanSec = 5;
        private ulong _executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private readonly Settings? _settings;
        private Timer? _timer = null;
        static readonly Random _random = new();
        static readonly System.Diagnostics.Stopwatch _watch = new();

        public TimedHostedService(ILogger<TimedHostedService> logger, IConfiguration configuration)
        {
            _settings = configuration
                .GetRequiredSection("Settings")
                .Get<Settings>();

            _logger = logger;
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
                var data = new Data(_watch.Elapsed);
                _logger.Log(logLvl, "LogLevel={LogLevel} Count={Count} Uptime={Uptime}s Period={Period} Data={Data}", 
                    logLvl, count, _watch.Elapsed.TotalSeconds, _watch.Elapsed.ToString(@"d\d\,\ hh\:mm\:ss"), data);     
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
        public Data(TimeSpan timeSpan) 
        {
            TimeSpan = timeSpan;
            Value = Math.Cos(2 * Math.PI * timeSpan.TotalSeconds);
        }

        public TimeSpan TimeSpan { get; private set; }

        public long Ticks { get { return TimeSpan.Ticks; } }

        public double Value { get; private set; }
    }
}
