using KleeneStar.Automation.WebAutomation;
using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.Test.Fixture;
using KleeneStar.Integration.WebIntegration;
using KleeneStar.Integration.WebManager;
using KleeneStar.Model;
using KleeneStar.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Net;
using System.Text;
using WebExpress.WebCore.WebMessage;
using WebExpress.WebCore.WebParameter;
using ObjectEntity = KleeneStar.Model.Entities.Object;

namespace KleeneStar.Integration.Test.WebManager
{
    /// <summary>
    /// Tests the connections to other systems: an incoming delivery is authenticated by the
    /// hook's token alone, reaches only the objects of the hook's workspace and raises each
    /// occurrence once; an outgoing call is signed, carries a delivery id that survives retries,
    /// and a receiver that fails makes the automation job retry it.
    /// </summary>
    [Collection("NonParallelTests")]
    public class UnitTestWebhookManager
    {
        private static readonly Guid WorkspaceId = Guid.Parse("B0B0B000-0000-4000-8000-000000000001");
        private static readonly Guid OtherWorkspaceId = Guid.Parse("B0B0B000-0000-4000-8000-000000000002");
        private static readonly Guid ClassId = Guid.Parse("B0B0B000-0000-4000-8000-000000000003");
        private static readonly Guid OtherClassId = Guid.Parse("B0B0B000-0000-4000-8000-000000000004");
        private static readonly Guid AgentId = Guid.Parse("B0B0B000-0000-4000-8000-000000000005");

        /// <summary>
        /// Seeds two workspaces with an object each and an agent.
        /// </summary>
        /// <param name="database">The per-test in-memory database name.</param>
        private static void Seed(string database)
        {
            IntegrationHubFixture.Initialize(database);

            using var db = IntegrationHubFixture.CreateDbContext(database);

            if (db.Workspaces.Any(x => x.Id == WorkspaceId))
            {
                return;
            }

            db.Workspaces.Add(new Workspace { Id = WorkspaceId, Key = "DEV", Name = "Development" });
            db.Workspaces.Add(new Workspace { Id = OtherWorkspaceId, Key = "FIN", Name = "Finance" });
            db.Classes.Add(new Class { Id = ClassId, Name = "Story", WorkspaceId = WorkspaceId });
            db.Classes.Add(new Class { Id = OtherClassId, Name = "Invoice", WorkspaceId = OtherWorkspaceId });
            db.Objects.Add(new ObjectEntity(Guid.NewGuid()) { Key = "DEV-1", Summary = "Login page", WorkspaceId = WorkspaceId, ClassId = ClassId });
            db.Objects.Add(new ObjectEntity(Guid.NewGuid()) { Key = "FIN-1", Summary = "Invoice run", WorkspaceId = OtherWorkspaceId, ClassId = OtherClassId });
            db.Identities.Add(new Identity { Id = AgentId, Name = "Agent", UserName = "agent", Email = "agent@example.com", PasswordHash = "x" });
            db.SaveChanges();
        }

        /// <summary>
        /// Adds an incoming webhook of the generic integration.
        /// </summary>
        /// <returns>The webhook.</returns>
        private static Webhook Incoming()
        {
            var webhook = new Webhook { WorkspaceId = WorkspaceId, Name = "ci-" + Guid.NewGuid().ToString("N")[..6], Direction = WebhookDirection.Incoming };

            IntegrationHub.WebhookManager.AddWebhook(webhook);

            return IntegrationHub.WebhookManager.GetWebhook(webhook.Id);
        }

        /// <summary>
        /// Builds a delivery of a JSON document.
        /// </summary>
        /// <param name="webhook">The webhook.</param>
        /// <param name="json">The document.</param>
        /// <returns>The delivery.</returns>
        private static IntegrationDelivery Delivery(Webhook webhook, string json)
        {
            return new IntegrationDelivery { Webhook = webhook, Body = Encoding.UTF8.GetBytes(json) };
        }

        /// <summary>
        /// A webhook is refused with every problem: a name with a blank, an outgoing one without an
        /// http address, an unknown integration, a short secret, a name used twice.
        /// </summary>
        [Fact]
        public void Validate_ReportsEveryProblem()
        {
            Seed(nameof(Validate_ReportsEveryProblem));

            Assert.Contains(IntegrationHub.WebhookManager.Validate(new Webhook { WorkspaceId = WorkspaceId, Name = "two words", Direction = WebhookDirection.Incoming }), x => x.Contains("name", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(IntegrationHub.WebhookManager.Validate(new Webhook { WorkspaceId = WorkspaceId, Name = "out", Direction = WebhookDirection.Outgoing, Url = "ftp://example.com" }), x => x.Contains("http", StringComparison.Ordinal));
            Assert.Contains(IntegrationHub.WebhookManager.Validate(new Webhook { WorkspaceId = WorkspaceId, Name = "in", Direction = WebhookDirection.Incoming, Integration = "unknown" }), x => x.Contains("integration", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(IntegrationHub.WebhookManager.Validate(new Webhook { WorkspaceId = WorkspaceId, Name = "in", Direction = WebhookDirection.Incoming, Secret = "short" }), x => x.Contains("16", StringComparison.Ordinal));

            var first = Incoming();
            Assert.Contains(IntegrationHub.WebhookManager.Validate(new Webhook { WorkspaceId = WorkspaceId, Name = first.Name.ToUpperInvariant(), Direction = WebhookDirection.Incoming }), x => x.Contains("already", StringComparison.Ordinal));
        }

        /// <summary>
        /// A new webhook gets a generated secret; only that secret opens an active incoming hook,
        /// and an update without a secret keeps it.
        /// </summary>
        [Fact]
        public void Authenticate_TakesOnlyTheTokenOfAnActiveIncomingHook()
        {
            Seed(nameof(Authenticate_TakesOnlyTheTokenOfAnActiveIncomingHook));

            var webhook = Incoming();

            Assert.Equal(64, webhook.Secret.Length);
            Assert.True(IntegrationHub.WebhookManager.Authenticate(webhook, webhook.Secret));
            Assert.False(IntegrationHub.WebhookManager.Authenticate(webhook, webhook.Secret + "x"));
            Assert.False(IntegrationHub.WebhookManager.Authenticate(webhook, null));
            Assert.False(IntegrationHub.WebhookManager.Authenticate(null, webhook.Secret));

            var secret = webhook.Secret;

            webhook.Secret = null;
            webhook.State = WebhookState.Disabled;
            IntegrationHub.WebhookManager.UpdateWebhook(webhook);

            var stored = IntegrationHub.WebhookManager.GetWebhook(webhook.Id);
            Assert.Equal(secret, stored.Secret);
            Assert.False(IntegrationHub.WebhookManager.Authenticate(stored, secret));
        }

        /// <summary>
        /// The endpoint answers an unknown hook and a wrong token alike with <c>404</c>, takes the
        /// token as bearer or query parameter, and answers a delivery with <c>202</c>.
        /// </summary>
        [Fact]
        public void Endpoint_AuthenticatesByTokenOnly()
        {
            Seed(nameof(Endpoint_AuthenticatesByTokenOnly));

            var webhook = Incoming();
            var endpoint = new global::KleeneStar.Integration.WWW.Api._1_.Hooks.Index();
            const string body = """{ "object": "DEV-1", "event": "pushed", "id": "c0ffee" }""";

            Assert.IsType<ResponseNotFound>(endpoint.Receive(HookRequest(Guid.NewGuid(), body, webhook.Secret, null)));
            Assert.IsType<ResponseNotFound>(endpoint.Receive(HookRequest(webhook.Id, body, "wrong-token-wrong-token", null)));
            Assert.IsType<ResponseNotFound>(endpoint.Receive(HookRequest(webhook.Id, body, null, null)));
            Assert.IsType<ResponseAccepted>(endpoint.Receive(HookRequest(webhook.Id, body, null, webhook.Secret)));
            Assert.IsType<ResponseAccepted>(endpoint.Receive(HookRequest(webhook.Id, body, webhook.Secret, null)));
        }

        /// <summary>
        /// Builds a request to the incoming endpoint.
        /// </summary>
        /// <param name="hook">The hook id.</param>
        /// <param name="body">The JSON body.</param>
        /// <param name="bearer">The token as bearer, or <see langword="null"/>.</param>
        /// <param name="query">The token as query parameter, or <see langword="null"/>.</param>
        /// <returns>The request.</returns>
        private static IRequest HookRequest(Guid hook, string body, string bearer, string query)
        {
            var content = Encoding.UTF8.GetBytes(body);
            var headers = new HeaderDictionary
            {
                ["Host"] = "localhost",
                ["Content-Type"] = "application/json",
                ["Content-Length"] = content.Length.ToString()
            };

            if (bearer is not null)
            {
                headers["Authorization"] = "Bearer " + bearer;
            }

            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature
            {
                Method = "POST",
                Protocol = "HTTP/1.1",
                Scheme = "http",
                RawTarget = "/kleenestar/api/1/hooks",
                QueryString = string.Empty,
                Headers = headers,
                Body = new MemoryStream(content)
            });
            features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
            {
                LocalIpAddress = IPAddress.Loopback,
                RemoteIpAddress = IPAddress.Loopback
            });
            features.Set<IHttpRequestIdentifierFeature>(new HttpRequestIdentifierFeature
            {
                TraceIdentifier = nameof(UnitTestWebhookManager)
            });

            var request = new WebExpress.WebCore.WebMessage.HttpContext(features, null!).Request;

            request.AddParameter(new Parameter("hook", hook.ToString(), ParameterScope.Parameter));

            if (query is not null)
            {
                request.AddParameter(new Parameter("token", query, ParameterScope.Parameter));
            }

            return request;
        }

        /// <summary>
        /// Answers outgoing calls with a set status and records what they carried.
        /// </summary>
        /// <param name="status">The status to answer with.</param>
        private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
        {
            /// <summary>Gets or sets the status to answer with.</summary>
            public HttpStatusCode Status { get; set; } = status;

            /// <summary>Gets the calls: delivery id, signature, body.</summary>
            public List<(string Delivery, string Signature, byte[] Content)> Calls { get; } = [];

            /// <inheritdoc/>
            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls.Add((
                    request.Headers.GetValues(WebhookManager.DeliveryHeader).Single(),
                    request.Headers.GetValues(WebhookManager.SignatureHeader).Single(),
                    request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult()));

                return new HttpResponseMessage(Status);
            }

            /// <inheritdoc/>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Send(request, cancellationToken));
            }
        }
    }
}
