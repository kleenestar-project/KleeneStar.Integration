using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.WebIntegration;
using KleeneStar.Integration.WebManager;
using KleeneStar.Model.Entities;
using System;
using System.Collections.Generic;
using WebExpress.WebCore.WebComponent;

namespace KleeneStar.Integration.WebManager
{
    /// <summary>
    /// The connections of the workspaces to other systems: the webhooks other systems call, and
    /// the ones the automation calls.
    /// </summary>
    public interface IWebhookManager : IComponentManager
    {
        /// <summary>
        /// An event that fires when a webhook is added.
        /// </summary>
        event EventHandler<Webhook> WebhookAdded;

        /// <summary>
        /// An event that fires when a webhook is changed.
        /// </summary>
        event EventHandler<Webhook> WebhookUpdated;

        /// <summary>
        /// An event that fires when a webhook is removed.
        /// </summary>
        event EventHandler<Webhook> WebhookRemoved;

        /// <summary>
        /// Returns the webhooks of a workspace.
        /// </summary>
        /// <param name="workspaceId">The workspace.</param>
        /// <returns>The webhooks, ordered by name.</returns>
        IReadOnlyList<Webhook> GetWebhooks(Guid workspaceId);

        /// <summary>
        /// Returns a webhook.
        /// </summary>
        /// <param name="webhookId">The webhook.</param>
        /// <returns>The webhook, or <see langword="null"/>.</returns>
        Webhook GetWebhook(Guid webhookId);

        /// <summary>
        /// Returns a webhook of a workspace by the name rules call it by.
        /// </summary>
        /// <param name="workspaceId">The workspace.</param>
        /// <param name="name">The name.</param>
        /// <returns>The webhook, or <see langword="null"/>.</returns>
        Webhook GetWebhook(Guid workspaceId, string name);

        /// <summary>
        /// Checks a webhook before it is stored.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <returns>The problems; empty when the webhook may be stored.</returns>
        IReadOnlyList<string> Validate(Webhook webhook);

        /// <summary>
        /// Adds a webhook; one without a secret is given a generated one.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <returns>The manager, for chaining.</returns>
        /// <exception cref="ArgumentException">The webhook is not valid.</exception>
        IWebhookManager AddWebhook(Webhook webhook);

        /// <summary>
        /// Writes the changes of a webhook.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <returns>The manager, for chaining.</returns>
        /// <exception cref="ArgumentException">The webhook is not valid.</exception>
        IWebhookManager UpdateWebhook(Webhook webhook);

        /// <summary>
        /// Removes a webhook.
        /// </summary>
        /// <param name="webhookId">The webhook.</param>
        /// <returns>The manager, for chaining.</returns>
        IWebhookManager RemoveWebhook(Guid webhookId);

        /// <summary>
        /// Determines whether a caller presented the token of an incoming webhook.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <param name="token">The token as presented.</param>
        /// <returns><see langword="true"/> for an active incoming webhook and its token.</returns>
        bool Authenticate(Webhook webhook, string token);

        /// <summary>
        /// Translates a delivery to an incoming webhook into automation events on the objects of
        /// its workspace.
        /// </summary>
        /// <param name="delivery">The delivery; its webhook has been authenticated.</param>
        /// <returns>What the delivery raised.</returns>
        WebhookReceipt Receive(IntegrationDelivery delivery);

        /// <summary>
        /// Posts a document to an outgoing webhook, signed with its secret.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <param name="body">The document.</param>
        /// <param name="deliveryId">The id the receiver discards a repeated delivery by.</param>
        /// <exception cref="InvalidOperationException">The webhook cannot be called, or the other
        /// system did not accept the delivery - the automation job retries it.</exception>
        void Deliver(Webhook webhook, byte[] body, string deliveryId);
    }

    /// <summary>
    /// What a delivery to an incoming webhook raised.
    /// </summary>
    /// <param name="Events">The events the integration read out of the payload.</param>
    /// <param name="Objects">The events that named an object of the webhook's workspace.</param>
    /// <param name="Queued">The automation jobs they queued.</param>
    public sealed record WebhookReceipt(int Events, int Objects, int Queued);
}
