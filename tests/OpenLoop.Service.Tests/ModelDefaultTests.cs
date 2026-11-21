using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class ModelDefaultTests
    {
        [Fact]
        public void RecentBeatsSavedAndListOrder()
        {
            var settings = new AppSettings { Model = "a/saved", RecentModels = ["z/recent", "a/saved"] };

            Assert.Equal("z/recent", ModelCatalog.DefaultModel(settings, ["a/saved", "z/recent"]));
            Assert.Equal("z/recent", ModelCatalog.DefaultModel(settings));
        }

        [Fact]
        public void SkipsRecentsOutsideVisibleList()
        {
            var settings = new AppSettings { Model = "a/saved", RecentModels = ["paid/big", "b/free"] };

            Assert.Equal("b/free", ModelCatalog.DefaultModel(settings, ["a/saved", "b/free"]));
        }

        [Fact]
        public void FallsBackToSavedThenFirst()
        {
            Assert.Equal("a/saved", ModelCatalog.DefaultModel(new AppSettings { Model = "a/saved" }, ["a/saved", "b/x"]));
            Assert.Equal("b/x", ModelCatalog.DefaultModel(new AppSettings(), ["b/x", "a/saved"]));
            Assert.Equal("openrouter/free", ModelCatalog.DefaultModel(new AppSettings()));
        }
    }
}
