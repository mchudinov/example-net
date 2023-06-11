using System.Diagnostics.CodeAnalysis;

namespace ExampleNetConsole
{
    public sealed class Settings
    {
        private double _workerTimeSpan;
        private double _workerShutdownTimeout;

        public required double WorkerTimeSpan
        {
            get { return _workerTimeSpan; }
            set { _workerTimeSpan = Convert.ToDouble(value); }
        }

        public required double WorkerShutdownTimeout
        {
            get { return _workerShutdownTimeout; }
            set { _workerShutdownTimeout = Convert.ToDouble(value); }
        }

        [SetsRequiredMembers]
        public Settings()
        {
            WorkerTimeSpan = 1;
            WorkerShutdownTimeout = 1;
        }
    }
}
