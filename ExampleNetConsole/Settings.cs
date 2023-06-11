using System.Diagnostics.CodeAnalysis;

namespace ExampleNetConsole
{
    public sealed class Settings
    {
        private double _timeSpan;
        public required double TimeSpan
        {
            get { return _timeSpan; }
            set { _timeSpan = Convert.ToDouble(value); }
        }

        [SetsRequiredMembers]
        public Settings() => TimeSpan = 1;
    }
}
