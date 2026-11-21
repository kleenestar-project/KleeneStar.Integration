using KleeneStar.Automation.WebAutomation;
using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.WebIntegration;
using KleeneStar.Integration.WebManager;
using KleeneStar.Model.Entities;
using System;
using System.Text.Json;

namespace KleeneStar.Integration.WebAutomation
{
    /// <summary>
    /// <c>webhook &lt;name&gt;</c> - posts the object and the event to an outgoing webhook of the
    /// rule's workspace.
    /// </summary>
    /// <remarks>
    /// The delivery id is the job's creation key for the line, so a retried or resumed job
    /// delivers under the same id and the receiver can discard the repeat; a receiver that
    /// does not answer with a success status fails the line, and the job retries it.
    /// </remarks>
    public sealed class WebhookAction : IAutomationAction
    {
        /// <inheritdoc/>
        public string Key => "webhook";

        /// <inheritdoc/>
        public string Syntax => "kleenestar.integration:automation.action.webhook";

        /// <inheritdoc/>
        public string Validate(string argument, Class @class)
        {
            return string.IsNullOrWhiteSpace(argument) ? "name the webhook to call." : null;
        }

        /// <inheritdoc/>
        public string Validate(string argument, Class @class, Guid workspaceId)
        {
            if (Validate(argument, @class) is { } problem)
            {
                return problem;
            }

            if (workspaceId == Guid.Empty)
            {
                return null;
            }

            return IntegrationHub.WebhookManager.GetWebhook(workspaceId, argument) is { Direction: WebhookDirection.Outgoing }
                ? null
                : $"the workspace has no outgoing webhook '{argument.Trim()}'.";
        }

        /// <inheritdoc/>
        public void Execute(AutomationActionContext context, string argument)
        {
            var webhook = IntegrationHub.WebhookManager.GetWebhook(context.Rule.WorkspaceId, argument)
                ?? throw new InvalidOperationException($"The workspace has no webhook '{argument?.Trim()}'.");
            var @object = context.Object;
            var assignee = @object.AssigneeId is Guid assigneeId ? CoreHub.IdentityManager.GetIdentity(assigneeId) : null;
            var deliveryId = context.IdempotencyKey;

            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                delivery = deliveryId,
                @event = context.Job?.Trigger,
                rule = new { id = context.Rule.Id, name = context.Rule.Name },
                @object = new
                {
                    id = @object.Id,
                    key = @object.Key,
                    summary = @object.Summary,
                    workspace = CoreHub.WorkspaceManager.GetWorkspace(@object.WorkspaceId)?.Key,
                    @class = CoreHub.ClassManager.GetClass(@object.ClassId)?.Name,
                    assignee = assignee?.UserName,
                    updated = @object.Updated
                },
                sent = DateTime.UtcNow
            });

            IntegrationHub.WebhookManager.Deliver(webhook, body, deliveryId);
        }
    }
}
