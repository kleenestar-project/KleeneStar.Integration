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
using KleeneStar.Model.Entities;
using System;
using WebExpress.WebApp.WebPage;
using WebExpress.WebApp.WebScope;
using WebExpress.WebCore.Internationalization;
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebPage;
using WebExpress.WebUI.WebControl;
using WebExpress.WebUI.WebIcon;

namespace KleeneStar.Integration.WWW.Integration._workspacekey_
{
    /// <summary>
    /// Writes a webhook: a new one, or - with <c>?webhook=&lt;id&gt;</c> - an existing one.
    /// </summary>
    /// <remarks>
    /// An existing incoming webhook shows the address and the token the other system is
    /// configured with. The secret field stays empty: left empty, the secret in use is kept, so
    /// changing the name does not break a configured system.
    /// </remarks>
    [WebIcon<IconLink>]
    [Title("kleenestar.integration:integration.webhook.title")]
    [Scope<IScopeGeneral>]
    public sealed class Webhook : IPage<VisualTreeWebApp>, IScopeGeneral
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public Webhook()
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

            string Translate(string key) => I18N.Translate(renderContext, key);

            var keyParameter = renderContext.Request.GetParameter<WorkspaceKeyParameter>();
            var workspace = CoreHub.WorkspaceManager.GetWorkspaceByKey(keyParameter?.Value);
            var webhook = Guid.TryParse(renderContext.Request.GetParameter("webhook")?.Value, out var webhookId)
                ? IntegrationHub.WebhookManager.GetWebhook(webhookId)
                : null;

            visualTree.Content.MainPanel.Headline.Title = "kleenestar.integration:integration.webhook.title";

            if (workspace is null || (webhook is not null && webhook.WorkspaceId != workspace.Id))
            {
                return;
            }

            if (webhook is { Direction: WebhookDirection.Incoming })
            {
                var address = $"{Translate("kleenestar.integration:integration.webhook.address")}: {Api._1_.Webhooks._workspacekey_.Index.AddressOf(webhook)}";
                var token = $"{Translate("kleenestar.integration:integration.webhook.token")}: {webhook.Secret}";

                visualTree.Content.MainPanel.AddPrimary(new ControlText() { Text = _ => address, Format = _ => TypeFormatText.Code });
                visualTree.Content.MainPanel.AddPrimary(new ControlText() { Text = _ => token, Format = _ => TypeFormatText.Code });
                visualTree.Content.MainPanel.AddPrimary(new ControlText()
                {
                    Text = _ => "kleenestar.integration:integration.webhook.incoming.help",
                    TextColor = _ => new PropertyColorText(TypeColorText.Secondary),
                    Margin = _ => new PropertySpacingMargin(PropertySpacing.Space.None, PropertySpacing.Space.None, PropertySpacing.Space.Three, PropertySpacing.Space.None)
                });
            }

            var name = new ControlFormItemInputText("webhook-name")
            {
                Name = _ => "name",
                Label = _ => "kleenestar.integration:integration.webhook.name",
                Help = _ => "kleenestar.integration:integration.webhook.name.help",
                Required = _ => true
            };
            var direction = new ControlFormItemInputCombo("webhook-direction")
            {
                Name = _ => "direction",
                Label = _ => "kleenestar.integration:integration.webhook.direction"
            };

            direction.Add
            (
                new ControlFormItemInputComboItem() { Value = _ => "outgoing", Text = _ => Translate("kleenestar.integration:integration.webhook.direction.outgoing") },
                new ControlFormItemInputComboItem() { Value = _ => "incoming", Text = _ => Translate("kleenestar.integration:integration.webhook.direction.incoming") }
            );

            var url = new ControlFormItemInputText("webhook-url")
            {
                Name = _ => "url",
                Label = _ => "kleenestar.integration:integration.webhook.url",
                Help = _ => "kleenestar.integration:integration.webhook.url.help"
            };
            var integration = new ControlFormItemInputCombo("webhook-integration")
            {
                Name = _ => "integration",
                Label = _ => "kleenestar.integration:integration.webhook.integration"
            };

            foreach (var item in IntegrationHub.IntegrationManager.All)
            {
                var key = item.Key;
                var label = Translate(item.Label);

                integration.Add(new ControlFormItemInputComboItem() { Value = _ => key, Text = _ => label });
            }

            var secret = new ControlFormItemInputText("webhook-secret")
            {
                Name = _ => "secret",
                Label = _ => "kleenestar.integration:integration.webhook.secret",
                Help = _ => "kleenestar.integration:integration.webhook.secret.help"
            };
            var state = new ControlFormItemInputCombo("webhook-state")
            {
                Name = _ => "state",
                Label = _ => "kleenestar.integration:integration.webhook.state"
            };

            state.Add
            (
                new ControlFormItemInputComboItem() { Value = _ => "active", Text = _ => Translate("kleenestar.integration:integration.webhook.state.active") },
                new ControlFormItemInputComboItem() { Value = _ => "disabled", Text = _ => Translate("kleenestar.integration:integration.webhook.state.disabled") }
            );

            var form = new ControlModalForm("webhook-form")
            {
                Uri = _ => CoreHub.GetUri<Api._1_.Webhooks._workspacekey_.Index>()?.BindParameters(keyParameter),
                RedirectUri = _ => CoreHub.GetUri<Index>()?.BindParameters(keyParameter)
            };

            form.Add(new PresetHiddenInput("webhook-id") { Name = _ => "id", Value = _ => webhook?.Id.ToString() ?? string.Empty });
            form.Add(name, direction, url, integration, secret, state);

            if (webhook is not null)
            {
                form.Initialize(e =>
                {
                    e.Context.SetValue(name, new ControlFormInputValueString(webhook.Name));
                    e.Context.SetValue(direction, new ControlFormInputValueString(webhook.Direction == WebhookDirection.Incoming ? "incoming" : "outgoing"));
                    e.Context.SetValue(url, new ControlFormInputValueString(webhook.Url));
                    e.Context.SetValue(integration, new ControlFormInputValueString(webhook.Integration ?? IntegrationManager.Generic));
                    e.Context.SetValue(state, new ControlFormInputValueString(webhook.State == WebhookState.Active ? "active" : "disabled"));
                });
            }

            form.AddPrimaryButton(new ControlFormItemButtonSubmit() { Text = _ => "kleenestar.integration:integration.webhook.save" });
            visualTree.Content.MainPanel.AddPrimary(form);
        }
    }
}
