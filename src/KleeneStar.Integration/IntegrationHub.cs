using KleeneStar.Core;
using KleeneStar.Integration.WebManager;
using WebExpress.WebCore.WebComponent;

namespace KleeneStar.Integration
{
    /// <summary>
    /// The plugin's managers, resolved like the core's on <see cref="CoreHub"/>: WebExpress
    /// resolves a manager by its exact class, so they are reached here rather than on the core's
    /// hub, which knows no integration.
    /// </summary>
    public static class IntegrationHub
    {
        private static WebhookManager _webhookManager;
        private static IntegrationManager _integrationManager;

        /// <summary>
        /// Gets or sets the component hub; the core's when not set.
        /// </summary>
        public static IComponentHub ComponentHub { get; internal set; }

        /// <summary>
        /// Gets the webhooks: incoming ones that report events, outgoing ones the automation calls.
        /// </summary>
        public static IWebhookManager WebhookManager => _webhookManager ??= (ComponentHub ?? CoreHub.ComponentHub)?.GetComponentManager<WebhookManager>();

        /// <summary>
        /// Gets the catalog of integrations that read the payloads of other systems.
        /// </summary>
        public static IIntegrationManager IntegrationManager => _integrationManager ??= (ComponentHub ?? CoreHub.ComponentHub)?.GetComponentManager<IntegrationManager>();
    }
}
