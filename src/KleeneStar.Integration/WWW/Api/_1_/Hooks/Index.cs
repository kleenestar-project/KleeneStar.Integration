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
using System.Linq;
using System.Text;
using System.Text.Json;
using WebExpress.WebCore.WebAttribute;
using WebExpress.WebCore.WebMessage;
using WebExpress.WebCore.WebParameter;
using WebExpress.WebCore.WebRestApi;

namespace KleeneStar.Integration.WWW.Api._1_.Hooks
{
    /// <summary>
    /// The address other systems call: <c>POST /api/1/hooks?hook=&lt;id&gt;</c>, with the hook's
    /// token as <c>Authorization: Bearer</c>, or as <c>?token=</c> for a system that can only set
    /// the address (GitLab, Jenkins).
    /// </summary>
    /// <remarks>
    /// The caller is a system, not an account, and signs in with nothing but the token: an
    /// unknown hook, a disabled one and a wrong token are all answered <c>404</c>, so the address
    /// tells nobody which hooks exist. The payload is translated by the hook's integration and
    /// answered <c>202</c> with what it raised - the consequences run as automation jobs.
    /// </remarks>
    [Title("kleenestar.integration:integration.api.hooks.title")]
    [Cache]
    public sealed class Index : IRestApi
    {
        /// <summary>
        /// The largest payload accepted; a delivery is a notice, not a transfer.
        /// </summary>
        public const int MaxBody = 1024 * 1024;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public Index()
        {
        }

        /// <summary>
        /// Handles <c>POST</c>: a delivery of another system.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns><c>202</c> with the receipt, <c>404</c>, or <c>413</c> for an oversized payload.</returns>
        [Method(RequestMethod.POST)]
        public IResponse Receive(IRequest request)
        {
            var webhook = Guid.TryParse(request?.GetParameter("hook")?.Value, out var id)
                ? IntegrationHub.WebhookManager.GetWebhook(id)
                : null;

            if (!IntegrationHub.WebhookManager.Authenticate(webhook, TokenOf(request)))
            {
                return new ResponseNotFound();
            }

            var body = (request as Request)?.Content ?? [];

            if (body.Length > MaxBody)
            {
                return new ResponsePayloadTooLarge();
            }

            var receipt = IntegrationHub.WebhookManager.Receive(new IntegrationDelivery
            {
                Webhook = webhook,
                Body = body,
                Query = (request.Parameters ?? []).OfType<Parameter>()
                    .Where(x => !string.Equals(x.Key, "token", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase)
            });

            return new ResponseAccepted
            {
                Content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    events = receipt.Events,
                    objects = receipt.Objects,
                    queued = receipt.Queued
                }))
            }.AddHeaderContentType("application/json");
        }

        /// <summary>
        /// Reads the token the caller presented, in the places the systems put it.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The token, or <see langword="null"/>.</returns>
        private static string TokenOf(IRequest request)
        {
            var authorization = request?.Header?.Authorization;

            if (string.Equals(authorization?.Type, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                return authorization.Token ?? authorization.RawData;
            }

            return request?.GetParameter("token")?.Value;
        }
    }
}
