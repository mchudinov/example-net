namespace ConsoleApp1
{
    public sealed class Config
    {
        private double _timeSpan;
        public required double TimeSpan
        {
            get { return _timeSpan; }
            set { _timeSpan = Convert.ToDouble(value); }
        }
    }
}
