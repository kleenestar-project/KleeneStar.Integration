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
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebMessage;
using WebExpress.WebCore.WebRestApi;

namespace KleeneStar.Integration.WWW.Api._1_.Webhooks._workspacekey_
{
    /// <summary>
    /// REST endpoint removing a webhook of a workspace. The URL is
    /// <c>/api/1/webhooks/{workspacekey}/delete</c>; the body names the webhook (<c>{ "id" }</c>).
    /// </summary>
    /// <remarks>
    /// Rules still calling the webhook fail from then on - visibly, as failed jobs - rather than
    /// being changed behind their authors' backs.
    /// </remarks>
    [Title("kleenestar.integration:integration.api.webhooks.delete.title")]
    [Cache]
    public sealed class Delete : IRestApi
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public Delete()
        {
        }

        /// <summary>
        /// Handles <c>POST</c>: removes the webhook the body names.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns><c>204</c>, or <c>404</c> for a webhook of no workspace the route names.</returns>
        [Method(RequestMethod.POST)]
        public IResponse Remove(IRequest request)
        {
            var refusal = JsonApi.Authorize(request, out var workspace);

            if (refusal is not null)
            {
                return refusal;
            }

            var id = JsonApi.ParseId(JsonApi.Read<JsonApi.IdPayload>(request)?.Id);
            var webhook = id is { } webhookId ? IntegrationHub.WebhookManager.GetWebhook(webhookId) : null;

            if (webhook is null || webhook.WorkspaceId != workspace.Id)
            {
                return new ResponseNotFound();
            }

            IntegrationHub.WebhookManager.RemoveWebhook(webhook.Id);

            return new ResponseNoContent();
        }
    }
}
