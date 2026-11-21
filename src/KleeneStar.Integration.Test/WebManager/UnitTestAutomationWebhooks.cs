using KleeneStar.Automation;
using KleeneStar.Automation.WebAutomation;
using KleeneStar.Automation.WebManager;
using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.Test.Fixture;
using KleeneStar.Integration.WebAutomation;
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
    public class UnitTestAutomationWebhooks
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
        /// Adds a rule.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="actions">The script.</param>
        /// <param name="parameter">The trigger parameter.</param>
        /// <returns>The rule.</returns>
        private static AutomationRule Rule(string trigger, string actions, string parameter = null)
        {
            var rule = new AutomationRule
            {
                Name = "rule " + Guid.NewGuid().ToString("N")[..6],
                WorkspaceId = WorkspaceId,
                Trigger = trigger,
                TriggerParameter = parameter,
                Actions = actions,
                RunAsId = AgentId,
                State = AutomationRuleState.Active
            };

            AutomationHub.AutomationManager.AddRule(rule);

            return rule;
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
        /// A delivery raises <c>integration.received</c> on the objects of the hook's workspace,
        /// once per occurrence; an object of another workspace and an unknown key are ignored.
        /// </summary>
        [Fact]
        public void Receive_RaisesOncePerOccurrenceWithinTheWorkspace()
        {
            var database = nameof(Receive_RaisesOncePerOccurrenceWithinTheWorkspace);
            Seed(database);

            var webhook = Incoming();
            var deployed = Rule(IntegrationReceivedTrigger.TriggerKey, "comment internal: Deployed.", "deployed");
            var failed = Rule(IntegrationReceivedTrigger.TriggerKey, "comment internal: Build failed.", "build.failed");

            var receipt = IntegrationHub.WebhookManager.Receive(Delivery(webhook, """{ "objects": ["DEV-1", "FIN-1", "NOPE-9"], "event": "deployed", "id": "pipeline-7" }"""));

            Assert.Equal(new WebhookReceipt(3, 1, 1), receipt);
            Assert.Single(ModelHub.GetAutomationJobs([deployed.Id], null, 10));
            Assert.Empty(ModelHub.GetAutomationJobs([failed.Id], null, 10));

            // the same pipeline reported again raises nothing new
            Assert.Equal(0, IntegrationHub.WebhookManager.Receive(Delivery(webhook, """{ "object": "DEV-1", "event": "deployed", "id": "pipeline-7" }""")).Queued);

            // a rule may narrow to the hook rather than the event
            var byHook = Rule(IntegrationReceivedTrigger.TriggerKey, "comment Seen.", webhook.Name);
            Assert.Equal(2, IntegrationHub.WebhookManager.Receive(Delivery(webhook, """{ "object": "DEV-1", "event": "deployed", "id": "pipeline-8" }""")).Queued);
            Assert.Single(ModelHub.GetAutomationJobs([byHook.Id], null, 10));

            var stored = IntegrationHub.WebhookManager.GetWebhook(webhook.Id);
            Assert.NotNull(stored.LastDelivery);
            Assert.Contains("2 jobs", stored.LastResult, StringComparison.Ordinal);
        }

        /// <summary>
        /// A payload without an id is its own occurrence: posted twice, it counts once.
        /// </summary>
        [Fact]
        public void Receive_WithoutIdDeduplicatesByPayload()
        {
            Seed(nameof(Receive_WithoutIdDeduplicatesByPayload));

            var webhook = Incoming();
            Rule(IntegrationReceivedTrigger.TriggerKey, "comment Pushed.");

            Assert.Equal(1, IntegrationHub.WebhookManager.Receive(Delivery(webhook, """{ "object": "DEV-1", "event": "pushed" }""")).Queued);
            Assert.Equal(0, IntegrationHub.WebhookManager.Receive(Delivery(webhook, """{ "object": "DEV-1", "event": "pushed" }""")).Queued);
            Assert.Equal(new WebhookReceipt(0, 0, 0), IntegrationHub.WebhookManager.Receive(Delivery(webhook, "not json")));
        }

        /// <summary>
        /// A rule's <c>webhook</c> line posts the object, signed, under a delivery id; a receiver
        /// answering with an error fails the line and the job retries it under the same id.
        /// </summary>
        [Fact]
        public void Deliver_SignsAndRetriesUnderTheSameId()
        {
            Seed(nameof(Deliver_SignsAndRetriesUnderTheSameId));

            var transport = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
            var manager = (WebhookManager)IntegrationHub.WebhookManager;
            var previous = manager.Transport;

            manager.Transport = transport;

            try
            {
                var webhook = new Webhook { WorkspaceId = WorkspaceId, Name = "notify-ci", Direction = WebhookDirection.Outgoing, Url = "https://ci.example.com/hook", Secret = "0123456789abcdef0123" };
                IntegrationHub.WebhookManager.AddWebhook(webhook);

                var rule = Rule(AutomationTriggerManager.ObjectCreated, "webhook notify-ci");
                var objectId = CoreHub.ObjectManager.GetObjectByKey("DEV-1").Id;

                AutomationHub.AutomationManager.Raise(new AutomationEvent { Trigger = AutomationTriggerManager.ObjectCreated, ObjectId = objectId, Occurrence = "commit:hook" });
                AutomationHub.AutomationManager.ProcessDue();

                var job = Assert.Single(ModelHub.GetAutomationJobs([rule.Id], null, 10));
                Assert.Equal(AutomationJobState.Pending, job.State);
                Assert.Contains("503", job.LastError, StringComparison.Ordinal);
                Assert.Contains("503", IntegrationHub.WebhookManager.GetWebhook(webhook.Id).LastResult, StringComparison.Ordinal);

                transport.Status = HttpStatusCode.OK;
                job.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
                ModelHub.UpdateAutomationJob(job);
                AutomationHub.AutomationManager.ProcessDue();

                Assert.Equal(AutomationJobState.Succeeded, Assert.Single(ModelHub.GetAutomationJobs([rule.Id], null, 10)).State);
                Assert.Equal(2, transport.Calls.Count);

                var (delivery, signature, content) = transport.Calls[1];
                Assert.Equal(transport.Calls[0].Delivery, delivery);
                Assert.Equal(WebhookManager.Sign("0123456789abcdef0123", content), signature);
                Assert.Contains("\"key\":\"DEV-1\"", Encoding.UTF8.GetString(content), StringComparison.Ordinal);
            }
            finally
            {
                manager.Transport = previous;
            }
        }

        /// <summary>
        /// A rule calling a webhook the workspace does not have is refused when it is saved.
        /// </summary>
        [Fact]
        public void Rule_WithUnknownWebhookIsRefused()
        {
            Seed(nameof(Rule_WithUnknownWebhookIsRefused));

            var problems = AutomationHub.AutomationManager.Validate(new AutomationRule
            {
                Name = "calls nothing",
                WorkspaceId = WorkspaceId,
                Trigger = AutomationTriggerManager.ObjectCreated,
                Actions = "webhook missing"
            });

            Assert.Contains(problems, x => x.StartsWith("Line 1:", StringComparison.Ordinal) && x.Contains("missing", StringComparison.Ordinal));
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
