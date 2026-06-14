using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    internal class SidecarTests
    {
        [TestCase("mysave.json.gz",  ExpectedResult = "mysave")]
        [TestCase("mysave.info.gz",  ExpectedResult = "mysave")]
        [TestCase("mysave.json",     ExpectedResult = "mysave")]
        [TestCase("mysave.gz",       ExpectedResult = "mysave")]
        [TestCase("mysave",          ExpectedResult = "mysave")]
        [TestCase("my.save.json.gz", ExpectedResult = "my.save")]
        [TestCase("UPPER.JSON.GZ",   ExpectedResult = "UPPER")]
        public string StripSaveExtension_ReturnsBaseName(string input)
            => LWCacheHelper.StripSaveExtension(input);

        [Test]
        public void StripSaveExtension_EmptyString_ReturnsEmpty()
            => Assert.That(LWCacheHelper.StripSaveExtension(""), Is.EqualTo(""));

        [Test]
        public void StripSaveExtension_LongestExtensionWinsOverShorter()
        {
            // "x.json.gz" should strip ".json.gz" not just ".gz"
            Assert.That(LWCacheHelper.StripSaveExtension("x.json.gz"), Is.EqualTo("x"));
        }
    }
}
