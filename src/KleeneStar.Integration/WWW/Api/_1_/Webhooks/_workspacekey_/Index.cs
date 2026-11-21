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
using System.Linq;
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebMessage;
using WebExpress.WebCore.WebRestApi;

namespace KleeneStar.Integration.WWW.Api._1_.Webhooks._workspacekey_
{
    /// <summary>
    /// REST endpoint over the webhooks of a workspace: <c>GET</c> lists them, <c>POST</c> adds
    /// one or - with an <c>id</c> - changes one. The URL is <c>/api/1/webhooks/{workspacekey}</c>.
    /// </summary>
    /// <remarks>
    /// Connecting a workspace to another system is workspace administration, the same gate the
    /// automation has (<see cref="JsonApi.Authorize"/>). The listing carries the secrets:
    /// whoever administers the workspace configures the other system with them.
    /// </remarks>
    [Title("kleenestar.integration:integration.api.webhooks.title")]
    [WorkspaceKeySegment]
    [Cache]
    public sealed class Index : IRestApi
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public Index()
        {
        }

        /// <summary>
        /// Handles <c>GET</c>: the webhooks of the workspace.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The webhooks as JSON.</returns>
        [Method(RequestMethod.GET)]
        public IResponse Retrieve(IRequest request)
        {
            var refusal = JsonApi.Authorize(request, out var workspace);

            if (refusal is not null)
            {
                return refusal;
            }

            return JsonApi.Json(IntegrationHub.WebhookManager.GetWebhooks(workspace.Id).Select(ToDto));
        }

        /// <summary>
        /// Handles <c>POST</c>: adds a webhook, or changes the one the body names.
        /// </summary>
        /// <param name="request">The request carrying the webhook.</param>
        /// <returns>The stored webhook, or the problems that kept it from being stored.</returns>
        [Method(RequestMethod.POST)]
        public IResponse Save(IRequest request)
        {
            var refusal = JsonApi.Authorize(request, out var workspace);

            if (refusal is not null)
            {
                return refusal;
            }

            var payload = JsonApi.Read<WebhookPayload>(request);

            if (payload is null)
            {
                return JsonApi.Refuse("The request body must describe a webhook.");
            }

            var id = JsonApi.ParseId(payload.Id);
            var existing = id is { } webhookId ? IntegrationHub.WebhookManager.GetWebhook(webhookId) : null;

            if (id is not null && (existing is null || existing.WorkspaceId != workspace.Id))
            {
                return new ResponseNotFound();
            }

            var webhook = existing ?? new Webhook { Id = Guid.NewGuid(), WorkspaceId = workspace.Id };

            webhook.Name = payload.Name?.Trim();
            webhook.Direction = string.Equals(payload.Direction, "incoming", StringComparison.OrdinalIgnoreCase)
                ? WebhookDirection.Incoming
                : WebhookDirection.Outgoing;
            webhook.Url = string.IsNullOrWhiteSpace(payload.Url) ? null : payload.Url.Trim();
            webhook.Integration = string.IsNullOrWhiteSpace(payload.Integration) ? null : payload.Integration.Trim();
            webhook.Secret = string.IsNullOrWhiteSpace(payload.Secret) ? null : payload.Secret.Trim();
            webhook.State = string.Equals(payload.State, "disabled", StringComparison.OrdinalIgnoreCase)
                ? WebhookState.Disabled
                : WebhookState.Active;

            var problems = IntegrationHub.WebhookManager.Validate(webhook);

            if (problems.Count > 0)
            {
                return JsonApi.Refuse(string.Join(" ", problems));
            }

            if (existing is null)
            {
                IntegrationHub.WebhookManager.AddWebhook(webhook);
            }
            else
            {
                IntegrationHub.WebhookManager.UpdateWebhook(webhook);
            }

            return JsonApi.Json(ToDto(IntegrationHub.WebhookManager.GetWebhook(webhook.Id)));
        }

        /// <summary>
        /// Projects a webhook onto its wire shape.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <returns>The DTO.</returns>
        internal static object ToDto(Webhook webhook)
        {
            return new
            {
                id = webhook.Id,
                name = webhook.Name,
                direction = webhook.Direction.ToString().ToLowerInvariant(),
                url = webhook.Url,
                integration = webhook.Integration,
                secret = webhook.Secret,
                state = webhook.State.ToString().ToLowerInvariant(),
                address = webhook.Direction == WebhookDirection.Incoming ? AddressOf(webhook) : null,
                lastDelivery = webhook.LastDelivery,
                lastResult = webhook.LastResult
            };
        }

        /// <summary>
        /// Returns the address another system posts to for an incoming webhook.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <returns>The address, relative to the server unless its external address is known.</returns>
        internal static string AddressOf(Webhook webhook)
        {
            var path = CoreHub.GetUri<Hooks.Index>()?.ToString() ?? "/api/1/hooks";

            return $"{path}?hook={webhook.Id}";
        }

        /// <summary>
        /// The JSON body of a webhook written through the page or a client.
        /// </summary>
        internal sealed class WebhookPayload
        {
            /// <summary>Gets or sets the id of the webhook to change, or empty for a new one.</summary>
            public string Id { get; set; }

            /// <summary>Gets or sets the name rules call the webhook by.</summary>
            public string Name { get; set; }

            /// <summary>Gets or sets the direction, <c>outgoing</c> or <c>incoming</c>.</summary>
            public string Direction { get; set; }

            /// <summary>Gets or sets the address of an outgoing webhook.</summary>
            public string Url { get; set; }

            /// <summary>Gets or sets the integration of an incoming webhook (default <see cref="IntegrationManager.Generic"/>).</summary>
            public string Integration { get; set; }

            /// <summary>Gets or sets the secret; empty generates one for a new webhook and keeps the one in use for an existing one.</summary>
            public string Secret { get; set; }

            /// <summary>Gets or sets the state, <c>active</c> or <c>disabled</c>.</summary>
            public string State { get; set; }
        }
    }
}
