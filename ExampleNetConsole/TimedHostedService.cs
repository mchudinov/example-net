using System.Collections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExampleNetConsole
{
    public class TimedHostedService : IHostedService, IDisposable
    {
        private ulong _executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private readonly Config _config;
        private Timer? _timer = null;
        static Random _random = new();
        static System.Diagnostics.Stopwatch _watch = new();

        public TimedHostedService(ILogger<TimedHostedService> logger, IOptions<Config> config)
        {
            _logger = logger;
            _config = config.Value;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _watch.Start();

            _logger.LogInformation("Timed Hosted Service is running.");
            _logger.LogInformation("TimedSpan = {TimeSpan}", _config.TimeSpan);

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(_config.TimeSpan));

            OutputEnvironmentVariables();

            return Task.CompletedTask;
        }

        private void DoWork(object? state)
        {
            var count = Interlocked.Increment(ref _executionCount);
            
            var lvl = RandomEnumValue<LogLevel>();
            _logger.Log(lvl, "LogLevel={LogLevel} Count={Count} Uptime={Uptime}s Period={Period}", lvl, count, _watch.Elapsed.TotalSeconds, _watch.Elapsed.ToString(@"d\d\,\ hh\:mm\:ss"));
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

        static T RandomEnumValue<T>()
        {
            var v = Enum.GetValues(typeof(T));
            return (T)v.GetValue(_random.Next(v.Length));
        }
    }
}
