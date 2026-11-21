using KleeneStar.Automation.WebAutomation;
using KleeneStar.Integration.WebIntegration;

namespace KleeneStar.Integration.Test.WebIntegration
{
    /// <summary>
    /// Tests that the plugin's items are findable: the managers of the automation plugin and the
    /// integration manager collect only public sealed types with a public parameterless
    /// constructor, so an item that is not one is silently missing in a running host.
    /// </summary>
    public class UnitTestIntegrationItems
    {
        /// <summary>
        /// Every trigger, action and integration of the plugin is public sealed and constructible.
        /// </summary>
        [Fact]
        public void EveryItemIsFindable()
        {
            var items = typeof(KleeneStarPlugin).Assembly.GetTypes()
                .Where(x => !x.IsAbstract && (typeof(IAutomationTrigger).IsAssignableFrom(x) || typeof(IAutomationAction).IsAssignableFrom(x) || typeof(IIntegration).IsAssignableFrom(x)))
                .ToList();

            Assert.Equal(
                ["AutomicIntegration", "GenericIntegration", "GitLabIntegration", "IntegrationReceivedTrigger", "JenkinsIntegration", "WebhookAction"],
                items.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

            Assert.All(items, x =>
            {
                Assert.True(x.IsPublic && x.IsSealed, $"{x.FullName} must be public sealed, or no manager finds it.");
                Assert.NotNull(x.GetConstructor(Type.EmptyTypes));
            });
        }
    }
}
