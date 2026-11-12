![KleeneStar](https://raw.githubusercontent.com/kleenestar-project/.github/main/docs/assets/img/banner.png)

# KleeneStar Integrations

A workspace talks to other systems - GitLab, Jenkins, Automic, a monitoring tool - through **webhooks**. The plugin `KleeneStar.Integration` carries them (moved out of the core on 2026-11-11; the core knows neither webhooks nor automation): what a payload means is read by an **integration**, a public sealed type of any plugin the `IntegrationManager` finds, and what follows from it is decided by the workspace's **automation rules** (`KleeneStar.Automation/docs/kleenestar.automation.md`, the automation plugin). Calls in both directions are therefore persistent, deduplicated and retried like every automation job.

## Webhooks

`Webhook` (`WorkspaceId`, unique `Name`, `Direction`, `Url`, `Integration`, `Secret`, `State`, `LastDelivery`, `LastResult`). The secret is generated (32 random bytes, hex) when none is given, at least 16 characters otherwise, redacted in the audit log and never part of a JSON projection of the entity; an update without a secret keeps the one in use.

### Incoming

Another system posts to `POST /kleenestar/kleenestar.integration/api/1/hooks?hook=<id>` (a plugin's routes sit under its id; the address before the move, `/kleenestar/api/1/hooks`, answers 404 - configured systems need the new one) with the hook's token as `Authorization: Bearer <token>` or `&token=<token>` (for systems that can only set an address, e.g. GitLab, Jenkins).

- **Authentication** is the token alone, compared in constant time. An unknown hook, a disabled one and a wrong token all answer `404`, so the address reveals nothing. Payloads above 1 MB answer `413`.
- **Translation.** The hook's integration (`IIntegration.Translate`) reads `IntegrationEvent(ObjectKey, Event, Occurrence)`s out of the payload.
- **Scope.** An event is raised only for an object **of the hook's workspace** - read unrestricted, because the caller is a system, not an account - anything else is ignored: a connection reaches no further than its workspace.
- **Events.** Each event is raised as automation trigger `integration.received` with the subjects *event* and *hook name*, so a rule's parameter narrows to either (`deployed`, `build.failed`, or `gitlab`). The occurrence `hook:<id>:<occurrence>` makes a repeated delivery queue nothing.
- **Answer.** `202 { events, objects, queued }`; the consequences run as jobs.

The **generic integration** (`generic`) reads `{ "object": "DEV-12", "event": "deployed", "id": "build-481" }` or `"objects": [ … ]`. Without `id`, the hash of the payload is the occurrence - the same document posted twice counts once.

### Outgoing

The automation action `webhook <name>` posts the object and the event:

```json
{ "delivery": "automation:<job>:<line>", "event": "object.updated",
  "rule": { "id": "…", "name": "…" },
  "object": { "id": "…", "key": "DEV-12", "summary": "…", "workspace": "DEV", "class": "Story", "assignee": "alice", "updated": "…" },
  "sent": "…" }
```

with `X-KleeneStar-Delivery` (the same across retries - the receiver discards repeats) and `X-KleeneStar-Signature: sha256=<hex HMAC-SHA256 of the body under the secret>` (`WebhookManager.Sign`). Only `2xx` counts as delivered; anything else, a timeout (15 s) or an unreachable host fails the line, and the job retries it with the automation's backoff and parks it after the last attempt. The outcome of every call is kept in `LastResult`. A rule naming a webhook the workspace does not have (or an incoming one) is refused when it is saved.

`WebhookManager.Transport` replaces the HTTP handler - in tests, or to route calls through a proxy. Outgoing addresses are set by workspace administrators and may point into the internal network on purpose (a Jenkins in the intranet).

## Integration plugins

```csharp
public sealed class GitLabIntegration : IIntegration
{
    public string Key => "gitlab";
    public string Label => "myplugin:gitlab.label";

    public IReadOnlyList<IntegrationEvent> Translate(IntegrationDelivery delivery)
    {
        // read commits, merge requests, pipelines; find object keys in titles and messages
    }
}

// nothing to register: a public sealed IIntegration with a parameterless constructor is found
// by the IntegrationManager, a public sealed IAutomationAction by the automation plugin's
```

An integration that acts on the other system ships automation actions for it; a new trigger, if it needs one, is a public sealed `AutomationTrigger` (this plugin's `IntegrationReceivedTrigger` is one).

## Surfaces

- **Page** `WWW/Integration/{workspacekey}` (workspace cog → *Integrations*): the webhooks with address, state and last delivery; `Webhook?webhook=<id>` writes one and, for an incoming one, shows address and token; `Delete?webhook=` confirms. Workspace administration: the pages ask `IntegrationAuthorization.Demand` in `Process` (a plugin page is not judged by the core's route convention).
- **REST** `/kleenestar/kleenestar.integration/api/1/webhooks/{workspacekey}`: `GET` (with secrets and the incoming address), `POST` add/change (with `id`), `POST …/delete {id}`; gated by `MayAdministerWorkspace`, refusals `{ "message" }`.

## GitLab, Jenkins, Automic

This plugin ships three integrations (`WebIntegration/DevIntegrations`, before in `KleeneStar.Templates`). Each names the objects by the keys it finds (`DEV-42`) and uses the other system's own identity of the event as occurrence, so a redelivery raises nothing:

|Integration|Reads                                                                           |Events
|-----------|--------------------------------------------------------------------------------|-----------------------------------------------------------
|`gitlab`   |project webhooks: push (commit messages, branch), merge request (title, description, source branch), pipeline (ref, commit message), deployment (commit title, ref) |`pushed`, `merge_request.<action>`, `pipeline.<status>`, `deployed`
|`jenkins`  |Notification plugin JSON: job name, build parameters, branch, change messages; COMPLETED and FINALIZED are one end |`pipeline.running`, `pipeline.success`, `pipeline.failed`, `pipeline.<status>`
|`automic`  |`{ run_id, object_name, status, reference }` posted by the Automation Engine     |`job.succeeded`, `job.failed`, `job.<status>`

GitLab is configured with the hook's address and `&token=`; the Software Development template's rules note failed pipelines and deployments.
