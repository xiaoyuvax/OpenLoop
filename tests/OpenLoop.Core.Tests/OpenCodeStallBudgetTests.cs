using OpenLoop.Core.OpenCode;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public sealed class OpenCodeStallBudgetTests
    {
        private static string NewModel() => "opencode/stall-" + Guid.NewGuid().ToString("N");

        [Fact]
        public void LearningHalvesTheStalledPromptAndTightensTheIdleWindow()
        {
            string model = NewModel();
            Assert.Equal(0, OpenCodeStallBudget.Limit(model));
            Assert.Equal(0, OpenCodeStallBudget.IdleSeconds(model));

            OpenCodeStallBudget.Learn(model, 400_000);
            Assert.Equal(200_000, OpenCodeStallBudget.Limit(model));
            Assert.Equal(OpenCodeStallBudget.StallIdleSeconds, OpenCodeStallBudget.IdleSeconds(model));

            OpenCodeStallBudget.Learn(model, 500_000);
            Assert.Equal(200_000, OpenCodeStallBudget.Limit(model));
        }

        [Fact]
        public void SmallPromptsOnlyTightenTheIdleWindow()
        {
            string model = NewModel();

            OpenCodeStallBudget.Learn(model, 10_000);

            Assert.Equal(0, OpenCodeStallBudget.Limit(model));
            Assert.Equal(OpenCodeStallBudget.StallIdleSeconds, OpenCodeStallBudget.IdleSeconds(model));
        }
    }
}
