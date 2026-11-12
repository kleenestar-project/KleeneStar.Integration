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
using System.Globalization;
using WebExpress.WebApp.WebPage;
using WebExpress.WebApp.WebScope;
using WebExpress.WebCore.Internationalization;
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebPage;
using WebExpress.WebCore.WebUri;
using WebExpress.WebUI.WebControl;
using WebExpress.WebUI.WebIcon;

namespace KleeneStar.Integration.WWW.Integration._workspacekey_
{
    /// <summary>
    /// The connections of a workspace to other systems: its incoming webhooks, with the address
    /// and token another system is configured with, and its outgoing ones, which rules call.
    /// </summary>
    /// <remarks>
    /// The page is workspace administration (<see cref="IntegrationAuthorization"/>).
    /// </remarks>
    [WebIcon<IconLink>]
    [Title("kleenestar.integration:integration.manage.title")]
    [Description("kleenestar.integration:integration.manage.description")]
    [WorkspaceKeySegment]
    [Scope<IScopeGeneral>]
    public sealed class Index : IPage<VisualTreeWebApp>, IScopeGeneral
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public Index()
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

            visualTree.Content.MainPanel.Headline.Title = "kleenestar.integration:integration.manage.title";

            if (workspace is null)
            {
                return;
            }

            visualTree.Content.MainPanel.AddPrimary(new ControlText()
            {
                Text = _ => "kleenestar.integration:integration.manage.description",
                TextColor = _ => new PropertyColorText(TypeColorText.Secondary),
                Margin = _ => new PropertySpacingMargin(PropertySpacing.Space.None, PropertySpacing.Space.None, PropertySpacing.Space.Three, PropertySpacing.Space.None)
            });

            var newUri = CoreHub.GetUri<Webhook>()?.BindParameters(keyParameter);

            visualTree.Content.MainPanel.AddPrimary(new ControlButtonLink()
            {
                Text = _ => "kleenestar.integration:integration.webhook.add",
                Icon = _ => new IconPlus(),
                Uri = _ => newUri,
                BackgroundColor = _ => new PropertyColorButton(TypeColorButton.Primary),
                Margin = _ => new PropertySpacingMargin(PropertySpacing.Space.None, PropertySpacing.Space.None, PropertySpacing.Space.Three, PropertySpacing.Space.None)
            });

            var webhooks = IntegrationHub.WebhookManager.GetWebhooks(workspace.Id);

            if (webhooks.Count == 0)
            {
                visualTree.Content.MainPanel.AddPrimary(new ControlText()
                {
                    Text = _ => "kleenestar.integration:integration.webhook.empty",
                    TextColor = _ => new PropertyColorText(TypeColorText.Secondary)
                });

                return;
            }

            var table = new ControlTable() { Striped = _ => TypeStripedTable.Row }
                .AddColumn(Translate("kleenestar.integration:integration.webhook.name"))
                .AddColumn(Translate("kleenestar.integration:integration.webhook.direction"))
                .AddColumn(Translate("kleenestar.integration:integration.webhook.target"))
                .AddColumn(Translate("kleenestar.integration:integration.webhook.state"))
                .AddColumn(Translate("kleenestar.integration:integration.webhook.last"))
                .AddColumn("");

            foreach (var webhook in webhooks)
            {
                var editUri = CoreHub.GetUri<Webhook>()?.BindParameters(keyParameter)?.Add(new UriQuery("webhook", webhook.Id.ToString()));
                var deleteUri = CoreHub.GetUri<Delete>()?.BindParameters(keyParameter)?.Add(new UriQuery("webhook", webhook.Id.ToString()));
                var incoming = webhook.Direction == WebhookDirection.Incoming;
                var direction = Translate(incoming ? "kleenestar.integration:integration.webhook.direction.incoming" : "kleenestar.integration:integration.webhook.direction.outgoing");
                var target = incoming
                    ? $"{Api._1_.Webhooks._workspacekey_.Index.AddressOf(webhook)} · {webhook.Integration}"
                    : webhook.Url;
                var state = Translate(webhook.State == WebhookState.Active ? "kleenestar.integration:integration.webhook.state.active" : "kleenestar.integration:integration.webhook.state.disabled");
                var last = webhook.LastDelivery is { } delivered
                    ? $"{delivered.ToLocalTime().ToString("g", renderContext.Request.Culture ?? CultureInfo.InvariantCulture)} · {webhook.LastResult}"
                    : "-";
                var name = webhook.Name;
                var deleteLabel = Translate("webexpress.webapp:delete.label");

                table.AddRow
                (
                    new ControlTableCellPanel().Add(new ControlLink() { Text = _ => name, Uri = _ => editUri }),
                    new ControlTableCell() { Text = _ => direction },
                    new ControlTableCell() { Text = _ => target },
                    new ControlTableCell() { Text = _ => state },
                    new ControlTableCell() { Text = _ => last },
                    new ControlTableCellPanel().Add(new ControlLink() { Text = _ => deleteLabel, Uri = _ => deleteUri })
                );
            }

            visualTree.Content.MainPanel.AddPrimary(table);
        }
    }
}
