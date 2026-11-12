using KleeneStar.Automation;
using KleeneStar.Automation.WebAutomation;
using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.WebAutomation;
using KleeneStar.Integration.WebIntegration;
using KleeneStar.Integration.WebManager;
using KleeneStar.Model;
using KleeneStar.Model.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WebExpress.WebCore;
using WebExpress.WebCore.WebComponent;

namespace KleeneStar.Integration.WebManager
{
    /// <summary>
    /// The connections of the workspaces to other systems.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An incoming delivery is not acted on here: the integration reads events out of it, and
    /// each event on an object of the webhook's workspace is raised as
    /// <c>integration.received</c> - the workspace's rules decide what follows, queued and
    /// retried like every automation. An event naming an object of another workspace is
    /// ignored: a connection reaches no further than the workspace it belongs to.
    /// </para>
    /// <para>
    /// An outgoing delivery runs inside an automation job (the <c>webhook</c> action), so a
    /// receiver that is down is retried with a growing delay and the job is parked after the
    /// last attempt. Every delivery carries an id that stays the same across retries and an
    /// HMAC-SHA256 signature of the body.
    /// </para>
    /// </remarks>
    public sealed class WebhookManager : IWebhookManager
    {
        /// <summary>
        /// The header carrying the signature of an outgoing delivery: <c>sha256=&lt;hex&gt;</c>.
        /// </summary>
        public const string SignatureHeader = "X-KleeneStar-Signature";

        /// <summary>
        /// The header carrying the id of an outgoing delivery.
        /// </summary>
        public const string DeliveryHeader = "X-KleeneStar-Delivery";

        /// <summary>
        /// The time an outgoing call may take before it counts as failed.
        /// </summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly IComponentHub _componentHub;
        private readonly IHttpServerContext _httpServerContext;
        private HttpClient _client;
        private HttpMessageHandler _transport;

        /// <inheritdoc/>
        public event EventHandler<Webhook> WebhookAdded;

        /// <inheritdoc/>
        public event EventHandler<Webhook> WebhookUpdated;

        /// <inheritdoc/>
        public event EventHandler<Webhook> WebhookRemoved;

        /// <summary>
        /// Gets or sets the handler outgoing calls are sent through - replaced by a test, or by
        /// a host that routes them through a proxy.
        /// </summary>
        public HttpMessageHandler Transport
        {
            get => _transport;
            set
            {
                _transport = value;
                _client = null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the manager. Invoked by WebExpress via reflection.
        /// </summary>
        /// <param name="componentHub">The component hub.</param>
        /// <param name="httpServerContext">The HTTP server context.</param>
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used via reflection.")]
        private WebhookManager(IComponentHub componentHub, IHttpServerContext httpServerContext)
        {
            _componentHub = componentHub;
            _httpServerContext = httpServerContext;
        }

        /// <inheritdoc/>
        public IReadOnlyList<Webhook> GetWebhooks(Guid workspaceId)
        {
            return ModelHub.GetWebhooks(workspaceId);
        }

        /// <inheritdoc/>
        public Webhook GetWebhook(Guid webhookId)
        {
            return ModelHub.GetWebhook(webhookId);
        }

        /// <inheritdoc/>
        public Webhook GetWebhook(Guid workspaceId, string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? null
                : ModelHub.GetWebhooks(workspaceId).FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> Validate(Webhook webhook)
        {
            var problems = new List<string>();

            if (webhook is null)
            {
                problems.Add("The webhook is missing.");
                return problems;
            }

            // the name is written into rule scripts, where a blank would end it
            if (string.IsNullOrWhiteSpace(webhook.Name) || webhook.Name.Any(char.IsWhiteSpace) || webhook.Name.Length > 64)
            {
                problems.Add("The webhook needs a name without blanks, of at most 64 characters.");
            }
            else if (ModelHub.GetWebhooks(webhook.WorkspaceId).Any(x => x.Id != webhook.Id && string.Equals(x.Name, webhook.Name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"The workspace already has a webhook named '{webhook.Name}'.");
            }

            if (webhook.Direction == WebhookDirection.Outgoing
                && (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                problems.Add("An outgoing webhook needs an absolute http or https address.");
            }

            if (webhook.Direction == WebhookDirection.Incoming && IntegrationHub.IntegrationManager.Get(webhook.Integration ?? IntegrationManager.Generic) is null)
            {
                problems.Add($"Unknown integration '{webhook.Integration}'.");
            }

            if (!string.IsNullOrEmpty(webhook.Secret) && webhook.Secret.Length < 16)
            {
                problems.Add("A secret needs at least 16 characters.");
            }

            return problems;
        }

        /// <inheritdoc/>
        public IWebhookManager AddWebhook(Webhook webhook)
        {
            Require(webhook);

            webhook.Id = webhook.Id == Guid.Empty ? Guid.NewGuid() : webhook.Id;
            webhook.Name = webhook.Name.Trim();
            webhook.Integration = webhook.Direction == WebhookDirection.Incoming ? webhook.Integration ?? IntegrationManager.Generic : null;
            webhook.Secret = string.IsNullOrEmpty(webhook.Secret) ? GenerateSecret() : webhook.Secret;
            webhook.Created = webhook.Created == default ? DateTime.UtcNow : webhook.Created;
            webhook.Updated = DateTime.UtcNow;

            ModelHub.AddWebhook(webhook);
            WebhookAdded?.Invoke(this, webhook);

            return this;
        }

        /// <inheritdoc/>
        public IWebhookManager UpdateWebhook(Webhook webhook)
        {
            Require(webhook);

            var stored = ModelHub.GetWebhook(webhook.Id);

            if (stored is null)
            {
                return this;
            }

            webhook.Name = webhook.Name.Trim();
            webhook.Integration = webhook.Direction == WebhookDirection.Incoming ? webhook.Integration ?? IntegrationManager.Generic : null;

            // an empty secret keeps the one in use: the form does not send it back
            webhook.Secret = string.IsNullOrEmpty(webhook.Secret) ? stored.Secret : webhook.Secret;
            webhook.LastDelivery = stored.LastDelivery;
            webhook.LastResult = stored.LastResult;

            ModelHub.UpdateWebhook(webhook);
            WebhookUpdated?.Invoke(this, webhook);

            return this;
        }

        /// <inheritdoc/>
        public IWebhookManager RemoveWebhook(Guid webhookId)
        {
            var webhook = ModelHub.GetWebhook(webhookId);

            if (webhook is null)
            {
                return this;
            }

            ModelHub.RemoveWebhook(webhookId);
            WebhookRemoved?.Invoke(this, webhook);

            return this;
        }

        /// <inheritdoc/>
        public bool Authenticate(Webhook webhook, string token)
        {
            if (webhook is not { Direction: WebhookDirection.Incoming, State: WebhookState.Active }
                || string.IsNullOrEmpty(webhook.Secret)
                || string.IsNullOrEmpty(token))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(webhook.Secret));
        }

        /// <inheritdoc/>
        public WebhookReceipt Receive(IntegrationDelivery delivery)
        {
            var webhook = delivery?.Webhook;

            if (webhook is not { Direction: WebhookDirection.Incoming, State: WebhookState.Active })
            {
                return new WebhookReceipt(0, 0, 0);
            }

            var integration = IntegrationHub.IntegrationManager.Get(webhook.Integration ?? IntegrationManager.Generic)
                ?? throw new InvalidOperationException($"Unknown integration '{webhook.Integration}'.");
            var events = integration.Translate(delivery) ?? [];
            var objects = 0;
            var queued = 0;

            foreach (var item in events)
            {
                Model.Entities.Object @object;

                // the caller is another system, not an account: the hook's token is its authority,
                // and it reaches the objects of the hook's workspace whatever their classification
                using (CoreHub.SecurityLevelManager?.BeginUnrestricted())
                {
                    @object = string.IsNullOrWhiteSpace(item?.ObjectKey) ? null : CoreHub.ObjectManager.GetObjectByKey(item.ObjectKey);
                }

                if (@object is null || @object.WorkspaceId != webhook.WorkspaceId || string.IsNullOrWhiteSpace(item.Occurrence))
                {
                    continue;
                }

                objects++;
                // without the automation plugin a delivery is accepted and queues nothing
                queued += AutomationHub.AutomationManager?.Raise(new AutomationEvent
                {
                    Trigger = IntegrationReceivedTrigger.TriggerKey,
                    ObjectId = @object.Id,
                    Subjects = [item.Event ?? string.Empty, webhook.Name],
                    Occurrence = $"hook:{webhook.Id:N}:{item.Occurrence}"
                }) ?? 0;
            }

            webhook.LastDelivery = DateTime.UtcNow;
            webhook.LastResult = $"{events.Count} events, {objects} objects, {queued} jobs";
            ModelHub.UpdateWebhook(webhook);

            return new WebhookReceipt(events.Count, objects, queued);
        }

        /// <inheritdoc/>
        public void Deliver(Webhook webhook, byte[] body, string deliveryId)
        {
            if (webhook is not { Direction: WebhookDirection.Outgoing })
            {
                throw new InvalidOperationException("The webhook is not an outgoing one.");
            }

            if (webhook.State != WebhookState.Active)
            {
                throw new InvalidOperationException($"The webhook '{webhook.Name}' is disabled.");
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new ByteArrayContent(body ?? [])
            };

            message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            message.Headers.TryAddWithoutValidation(DeliveryHeader, deliveryId);
            message.Headers.TryAddWithoutValidation(SignatureHeader, Sign(webhook.Secret, body ?? []));

            string result = null;

            try
            {
                using var response = Client.Send(message);

                result = $"{(int)response.StatusCode} {response.ReasonPhrase}";

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"The webhook '{webhook.Name}' answered {result}.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                result = ex.Message;

                throw new InvalidOperationException($"The webhook '{webhook.Name}' could not be reached: {ex.Message}", ex);
            }
            finally
            {
                // the outcome is recorded whether or not the call succeeded, so the page shows
                // what the other system said last
                webhook.LastDelivery = DateTime.UtcNow;
                webhook.LastResult = Truncate(result, 256);
                ModelHub.UpdateWebhook(webhook);
            }
        }

        /// <summary>
        /// Signs a body the way a receiver checks it: <c>sha256=</c> and the lower-case hex
        /// HMAC-SHA256 of the body under the secret.
        /// </summary>
        /// <param name="secret">The webhook's secret.</param>
        /// <param name="body">The body.</param>
        /// <returns>The signature.</returns>
        public static string Sign(string secret, byte[] body)
        {
            return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret ?? string.Empty), body ?? []));
        }

        /// <summary>
        /// Gets the client of the outgoing calls.
        /// </summary>
        private HttpClient Client => _client ??= new HttpClient(_transport ?? new SocketsHttpHandler(), disposeHandler: _transport is null)
        {
            Timeout = Timeout
        };

        /// <summary>
        /// Generates a secret no one has to make up.
        /// </summary>
        /// <returns>32 random bytes as hex.</returns>
        private static string GenerateSecret()
        {
            return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        }

        /// <summary>
        /// Refuses a webhook that is not valid.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <exception cref="ArgumentException">The webhook is not valid.</exception>
        private void Require(Webhook webhook)
        {
            var problems = Validate(webhook);

            if (problems.Count > 0)
            {
                throw new ArgumentException(string.Join(" ", problems), nameof(webhook));
            }
        }

        /// <summary>
        /// Shortens a text to a column's length.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="length">The length.</param>
        /// <returns>The text, at most <paramref name="length"/> characters long.</returns>
        private static string Truncate(string text, int length)
        {
            return text is null || text.Length <= length ? text : text[..length];
        }

        /// <summary>
        /// Releases the client of the outgoing calls.
        /// </summary>
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
