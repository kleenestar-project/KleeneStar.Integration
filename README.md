![KleeneStar](https://raw.githubusercontent.com/kleenestar-project/.github/main/docs/assets/img/banner.png)

# KleeneStar.Integration - Talking to Other Systems

**KleeneStar.Integration** connects the workspaces of **KleeneStar** to the systems around them - GitLab, Jenkins, Automic, a monitoring tool, anything that can send or receive a webhook. What another system reports becomes an event the automation rules react to, and a rule can call another system in return.

Built on the **WebExpress-Framework**, **KleeneStar.Core** and **KleeneStar.Automation**, it is a plugin of its own: an installation that talks to no other system carries none of it.

## What It Powers

**KleeneStar.Integration** brings webhooks in both directions and the integrations that understand them.

- **Incoming webhooks** - another system posts to `/api/1/hooks?hook=<id>`, authenticated by the hook's token; the events it reports on objects of the hook's workspace raise the automation trigger *Event of another system* (`integration.received`), narrowed to the event or the hook.
- **Outgoing webhooks** - the automation action `webhook <name>` calls another system, signed with HMAC-SHA256, under a delivery id that stays the same across retries.
- **Integrations** - GitLab (push, merge request, pipeline, deployment), Jenkins (build notifications), Automic (job results) and a generic JSON document. Each finds the objects it concerns by their keys (`DEV-42`) in commit messages, branch names, titles and parameters.
- **Exactly once** - the other system's own identity of an event is its occurrence, so a redelivery raises nothing twice.
- **Administration per workspace** - the page *Integrations* in the workspace's settings, for the workspace's administrators; the configuration of every webhook is recorded in the audit log.

## How It Works

1. An administrator creates an incoming webhook for a workspace, picks the integration that reads its payloads and hands the address and token to the other system.
2. A delivery is translated by the integration into events on objects; only objects of the hook's workspace count, and each event raises `integration.received` through the automation plugin.
3. The workspace's rules decide what follows - a comment, a transition, an outgoing webhook back to the other system.

The integrations are an open catalog: every **public sealed** integration of any plugin is found by the plugin's integration manager, no registration needed. A plugin that connects another system ships an integration and, where it acts on that system, an automation action.

## For Developers

The repository contains the plugin and its tests:

| Path                                         | Content
|----------------------------------------------|--------------------------------------------------------------
| `src/KleeneStar.Integration/KleeneStarPlugin.cs` | Plugin entry; audits the webhooks' configuration
| `src/KleeneStar.Integration/IntegrationHub.cs`   | The plugin's managers
| `src/KleeneStar.Integration/WebManager`      | `WebhookManager` (receive, deliver), `IntegrationManager` (the catalog)
| `src/KleeneStar.Integration/WebIntegration`  | The integration types, the generic, GitLab, Jenkins and Automic integrations
| `src/KleeneStar.Integration/WebAutomation`   | The trigger `integration.received` and the action `webhook`
| `src/KleeneStar.Integration/WWW`             | The page `/integration/{workspacekey}`, the endpoints `/api/1/hooks` and `/api/1/webhooks/{workspacekey}`
| `src/KleeneStar.Integration.Test`            | Tests against an in-memory database; deliveries are swapped for a stub transport
| `docs/kleenestar.integration.md`             | Concept and behaviour in detail

The plugin references **KleeneStar.Core** and **KleeneStar.Automation** by relative path, so the repositories are cloned side by side; the host references this plugin. A plugin's routes sit under its id: the incoming address is `/kleenestar/kleenestar.integration/api/1/hooks?hook=<id>`.

**Building**

```
dotnet build KleeneStar.Integration/src/KleeneStar.Integration/KleeneStar.Integration.csproj
```

**Testing**

```
dotnet build KleeneStar.Integration/src/KleeneStar.Integration.Test
KleeneStar.Integration/src/KleeneStar.Integration.Test/bin/Debug/net10.0/KleeneStar.Integration.Test.exe
```

## Legal & Licensing

**KleeneStar.Integration** is released under the `MIT License`, a permissive open-source license that allows reuse, modification, and distribution with minimal restrictions. You're free to use **KleeneStar** in personal, academic, or commercial projects, just include the original copyright notice.

The system is designed to be GDPR-compliant:
- No tracking
- No monetization
- No hidden dependencies
- Full transparency and infrastructure control

**KleeneStar** respects your data and your autonomy. It's built for clarity, not surveillance.

## Contributing

We welcome contributions in many areas:
- Integrations for further systems (C#)
- Tests against real payloads of the systems supported
- Documentation and onboarding flows

Feel free to fork the repository, open issues, or submit pull requests. For larger contributions, please reach out via kleenestar.project@gmail.com.

# AI transparency notice

Parts of this software, its documentation, and its assets were created with the assistance of AI-based tools, including large language models. AI-assisted contributions are reviewed by the project maintainer before they are included.

---

Become part of the **KleeneStar** community and contribute to a modular, open-source future.
