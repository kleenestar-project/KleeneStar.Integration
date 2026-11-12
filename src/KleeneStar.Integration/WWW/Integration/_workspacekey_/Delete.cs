using KleeneStar.Core;
using KleeneStar.Core.WebAttribute;
using KleeneStar.Core.WebControl;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.WebIntegration;
using KleeneStar.Integration.WebManager;
using KleeneStar.Integration.WebPermission;
using System;
using WebExpress.WebApp.WebPage;
using WebExpress.WebApp.WebScope;
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebPage;
using WebExpress.WebUI.WebControl;
using WebExpress.WebUI.WebIcon;

namespace KleeneStar.Integration.WWW.Integration._workspacekey_
{
    /// <summary>
    /// Confirms the removal of a webhook (<c>?webhook=&lt;id&gt;</c>).
    /// </summary>
    [WebIcon<IconTrash>]
    [Title("kleenestar.integration:integration.webhook.delete.title")]
    [Scope<IScopeGeneral>]
    public sealed class Delete : IPage<VisualTreeWebApp>, IScopeGeneral
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public Delete()
        {
        }

        /// <summary>
        /// Processing of the resource.
        /// </summary>
        /// <param name="renderContext">The context for rendering the page.</param>
        /// <param name="visualTree">The visual tree of the web application.</param>
        public void Process(IRenderContext renderContext, VisualTreeWebApp visualTree)
        {
            // a plugin page guards itself: the core's route convention does not judge it
            IntegrationAuthorization.Demand(renderContext?.Request);

            var keyParameter = renderContext.Request.GetParameter<WorkspaceKeyParameter>();
            var workspace = CoreHub.WorkspaceManager.GetWorkspaceByKey(keyParameter?.Value);
            var webhook = Guid.TryParse(renderContext.Request.GetParameter("webhook")?.Value, out var webhookId)
                ? IntegrationHub.WebhookManager.GetWebhook(webhookId)
                : null;

            visualTree.Content.MainPanel.Headline.Title = "kleenestar.integration:integration.webhook.delete.title";

            if (workspace is null || webhook is null || webhook.WorkspaceId != workspace.Id)
            {
                return;
            }

            var name = webhook.Name;

            visualTree.Content.MainPanel.AddPrimary(new ControlText() { Text = _ => name, Format = _ => TypeFormatText.H5 });
            visualTree.Content.MainPanel.AddPrimary(new ControlText()
            {
                Text = _ => "kleenestar.integration:integration.webhook.delete.description",
                TextColor = _ => new PropertyColorText(TypeColorText.Secondary)
            });

            var form = new ControlModalForm("webhook-delete-form")
            {
                Uri = _ => CoreHub.GetUri<Api._1_.Webhooks._workspacekey_.Delete>()?.BindParameters(keyParameter),
                RedirectUri = _ => CoreHub.GetUri<Index>()?.BindParameters(keyParameter)
            };

            form.Add(new PresetHiddenInput("webhook-delete-id") { Name = _ => "id", Value = _ => webhook.Id.ToString() });
            form.AddPrimaryButton(new ControlFormItemButtonSubmit() { Text = _ => "webexpress.webapp:delete.label" });
            visualTree.Content.MainPanel.AddPrimary(form);
        }
    }
}
