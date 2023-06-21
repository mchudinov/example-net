using System.Diagnostics.CodeAnalysis;

namespace ExampleNet
{
    public sealed class Settings
    {
        private double _timeSpan;
        private double _shutdownTimeout;

        public required bool UseApm { get; set; }

        public required double TimeSpan
        {
            get { return _timeSpan; }
            set { _timeSpan = Convert.ToDouble(value); }
        }

        public required double ShutdownTimeout
        {
            get { return _shutdownTimeout; }
            set { _shutdownTimeout = Convert.ToDouble(value); }
        }

        [SetsRequiredMembers]
        public Settings()
        {
            TimeSpan = 1;
            ShutdownTimeout = 1;
            UseApm = false;
        }
    }
}
