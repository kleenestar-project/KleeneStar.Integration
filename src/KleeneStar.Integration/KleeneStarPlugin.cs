using KleeneStar.Core;
using KleeneStar.Model.Entities;
using System;
using WebExpress.WebCore;
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebComponent;
using WebExpress.WebCore.WebPlugin;

namespace KleeneStar.Integration
{
    /// <summary>
    /// Plugin entry point of the webhooks and integrations: incoming webhooks through which other
    /// systems report events on objects (raised as the automation trigger
    /// <c>integration.received</c>), outgoing webhooks the automation calls with the action
    /// <c>webhook &lt;name&gt;</c>, and the integrations that read GitLab, Jenkins, Automic or a
    /// generic JSON document.
    /// </summary>
    /// <remarks>
    /// It extends the automation plugin, so it depends on it; the core knows neither. Its pages
    /// and endpoints are routed under the plugin's id
    /// (<c>/kleenestar/kleenestar.integration/…</c>), its trigger, action and integrations are
    /// found by the managers of their kind because they are public sealed types.
    /// </remarks>
    [Name("kleenestar.integration:plugin.name")]
    [Description("kleenestar.integration:plugin.description")]
    [Application<KleeneStarApplication>()]
    [Dependency("webexpress.webapp")]
    [Dependency("kleenestar.core")]
    [Dependency("kleenestar.automation")]
    public sealed class KleeneStarPlugin : IPlugin
    {
        private readonly IHttpServerContext _httpServerContext;

        /// <summary>
        /// Initializes a new instance of the class. Invoked by WebExpress via reflection.
        /// </summary>
        /// <param name="httpServerContext">The server context, for the log.</param>
        /// <param name="componentHub">The component hub the plugin's managers are resolved from.</param>
        public KleeneStarPlugin(IHttpServerContext httpServerContext, IComponentHub componentHub)
        {
            _httpServerContext = httpServerContext;
            IntegrationHub.ComponentHub = componentHub;
        }

        /// <summary>
        /// Records the webhooks' configuration in the audit log - what the core's
        /// <c>AuditManager.Connect</c> does for its own managers, here because the core cannot
        /// name this one.
        /// </summary>
        public void Run()
        {
            try
            {
                var webhooks = IntegrationHub.WebhookManager
                    ?? throw new InvalidOperationException("The webhook manager is not registered.");

                webhooks.WebhookAdded += (_, x) => Record(AuditAction.Created, x, AuditSeverity.Notice);
                webhooks.WebhookUpdated += (_, x) => Record(AuditAction.Updated, x, AuditSeverity.Notice);
                webhooks.WebhookRemoved += (_, x) => Record(AuditAction.Deleted, x, AuditSeverity.Critical);
            }
            catch (Exception ex)
            {
                _httpServerContext?.Log?.Exception(ex);
            }
        }

        /// <summary>
        /// Releases the plugin.
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Records a change of a webhook without letting a failure reach the write that raised it.
        /// </summary>
        /// <param name="action">What happened.</param>
        /// <param name="webhook">The webhook.</param>
        /// <param name="severity">How it is to be judged.</param>
        private void Record(AuditAction action, Webhook webhook, AuditSeverity severity)
        {
            try
            {
                CoreHub.AuditManager?.RecordChange(AuditCategory.Configuration, action, webhook, severity);
            }
            catch (Exception ex)
            {
                _httpServerContext?.Log?.Exception(ex);
            }
        }
    }
}
