using KleeneStar.Automation;
using KleeneStar.Automation.WebAutomation;
using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebNavigator;
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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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

            if (webhook.Direction == WebhookDirection.Outgoing)
            {
                if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    problems.Add("An outgoing webhook needs an absolute http or https address.");
                }
                else if (!IntegrationHub.WebhookSettings.AllowPrivateNetwork
                    && (uri.IsLoopback || (IPAddress.TryParse(uri.IdnHost, out var literal) && !IsAllowedTarget([literal]))))
                {
                    // names are resolved when a call connects; a literal or local name is refused here
                    problems.Add("An outgoing webhook may not address this host or its private network.");
                }
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
        /// <remarks>
        /// The built-in transport follows no redirect (a redirect is a new target the receiver
        /// chooses) and connects only to addresses <see cref="IsAllowedTarget"/> accepts, checked
        /// when the connection opens, so a name that resolves elsewhere after validation is
        /// caught as well.
        /// </remarks>
        private HttpClient Client => _client ??= new HttpClient(_transport ?? CreateTransport(), disposeHandler: _transport is null)
        {
            Timeout = Timeout
        };

        /// <summary>
        /// Creates the built-in transport of the outgoing calls.
        /// </summary>
        /// <returns>The transport.</returns>
        private static SocketsHttpHandler CreateTransport()
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = ConnectAsync
            };
        }

        /// <summary>
        /// Opens the connection of an outgoing call, once the target lies outside this host and
        /// its private networks (unless <see cref="WebhookSettings.AllowPrivateNetwork"/>).
        /// </summary>
        /// <remarks>
        /// Through a proxy the connection goes to the proxy; the target judged is then the host of
        /// the request, which the proxy connects to.
        /// </remarks>
        /// <param name="context">The connection context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The stream of the connection.</returns>
        /// <exception cref="HttpRequestException">The target is inside this host's network.</exception>
        private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var endpoint = context.DnsEndPoint;
            var addresses = await ResolveAsync(endpoint.Host, cancellationToken);

            if (!IntegrationHub.WebhookSettings.AllowPrivateNetwork)
            {
                var target = context.InitialRequestMessage?.RequestUri?.IdnHost ?? endpoint.Host;
                var targetAddresses = string.Equals(target, endpoint.Host, StringComparison.OrdinalIgnoreCase)
                    ? addresses
                    : await ResolveAsync(target, cancellationToken);

                if (!IsAllowedTarget(targetAddresses))
                {
                    throw new HttpRequestException($"The address '{target}' lies inside this host or its private network, which outgoing webhooks may not reach.");
                }
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();

                throw;
            }
        }

        /// <summary>
        /// Resolves a host name, or reads an address literal.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The addresses.</returns>
        private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            return IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
        }

        /// <summary>
        /// Determines whether an outgoing webhook may connect to the given addresses: all of them
        /// lie outside this host and its private networks.
        /// </summary>
        /// <remarks>
        /// All addresses must qualify, so a name resolving to a public and an internal address
        /// cannot be used to slip through.
        /// </remarks>
        /// <param name="addresses">The addresses a target resolves to.</param>
        /// <returns><see langword="true"/> when the target may be reached.</returns>
        public static bool IsAllowedTarget(IReadOnlyCollection<IPAddress> addresses)
        {
            return addresses is { Count: > 0 } && addresses.All(FaviconResolver.IsPubliclyRoutable);
        }

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
