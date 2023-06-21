using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExampleNet.Tests
{
    [TestClass()]
    public class SettingsTests
    {
        [TestMethod()]
        public void SettingsTest()
        {
            var settings = new Settings();
            Assert.IsNotNull(settings);
            Assert.AreEqual(1.0, settings.ShutdownTimeout);
            Assert.AreEqual(1.0, settings.TimeSpan);
        }
    }
}