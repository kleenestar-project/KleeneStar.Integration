using KleeneStar.Core;
using KleeneStar.Core.WebManager;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Integration.WebIntegration;
using KleeneStar.Integration.WebManager;
using KleeneStar.Model.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KleeneStar.Integration.WebIntegration
{
    /// <summary>
    /// What another system posted to an incoming webhook.
    /// </summary>
    public sealed class IntegrationDelivery
    {
        /// <summary>
        /// Gets the webhook the delivery arrived at.
        /// </summary>
        public Webhook Webhook { get; init; }

        /// <summary>
        /// Gets the body as posted.
        /// </summary>
        public byte[] Body { get; init; } = [];

        /// <summary>
        /// Gets the query parameters of the call, the token excluded.
        /// </summary>
        public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An event an integration read out of a delivery: something that happened, in the other
    /// system, to an object of this one.
    /// </summary>
    /// <param name="ObjectKey">The key of the object the event concerns, e.g. <c>DEV-42</c>.</param>
    /// <param name="Event">What happened, in the other system's words - <c>pushed</c>,
    /// <c>build.failed</c>, <c>deployed</c>. Rules on <c>integration.received</c> narrow to it.</param>
    /// <param name="Occurrence">What identifies this occurrence in the other system - a commit
    /// hash, a build number, a delivery id; the same occurrence delivered twice raises nothing
    /// the second time.</param>
    public sealed record IntegrationEvent(string ObjectKey, string Event, string Occurrence);

    /// <summary>
    /// Translates the payloads of one kind of system into events on objects.
    /// </summary>
    /// <remarks>
    /// The core knows no system: what a GitLab push, a Jenkins build or an Automic job reports is
    /// read by an integration a plugin ships - a <b>public sealed</b> class the <see cref="IntegrationManager"/> finds, and
    /// what follows from it is decided by the workspace's rules. An integration that wants to act
    /// on the other system as well registers an automation action for it.
    /// </remarks>
    public interface IIntegration
    {
        /// <summary>
        /// Gets the key an incoming webhook names the integration by.
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Gets the i18n key of the integration's label.
        /// </summary>
        string Label { get; }

        /// <summary>
        /// Reads the events out of a delivery.
        /// </summary>
        /// <param name="delivery">The delivery.</param>
        /// <returns>The events; empty when the payload reports nothing about objects.</returns>
        IReadOnlyList<IntegrationEvent> Translate(IntegrationDelivery delivery);
    }

    /// <summary>
    /// The plugin's own integration, for a system that can post a small JSON document:
    /// <c>{ "object": "SD-12", "event": "deployed", "id": "build-481" }</c> - or
    /// <c>"objects": [ … ]</c> for several.
    /// </summary>
    public sealed class GenericIntegration : IIntegration
    {
        /// <summary>
        /// Gets the key an incoming webhook names the integration by.
        /// </summary>
        public string Key => IntegrationManager.Generic;

        /// <summary>
        /// Gets the i18n key of the integration's label.
        /// </summary>
        public string Label => "kleenestar.integration:integration.generic.label";

        /// <summary>
        /// Reads the events out of a generic JSON document: <c>object</c> or <c>objects</c>, <c>event</c>
        /// (default <c>received</c>) and <c>id</c>; without an id the payload's hash is the occurrence.
        /// </summary>
        /// <param name="delivery">The delivery.</param>
        /// <returns>The events; empty when the body is no JSON object.</returns>
        public IReadOnlyList<IntegrationEvent> Translate(IntegrationDelivery delivery)
        {
            JsonElement root;

            try
            {
                root = JsonDocument.Parse(delivery?.Body ?? []).RootElement;
            }
            catch (JsonException)
            {
                return [];
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var name = Read(root, "event") ?? "received";
            var occurrence = Read(root, "id") ?? Read(root, "occurrence");
            var keys = new List<string>();

            if (Read(root, "object") is { } single)
            {
                keys.Add(single);
            }

            if (root.TryGetProperty("objects", out var many) && many.ValueKind == JsonValueKind.Array)
            {
                keys.AddRange(many.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
            }

            // without an id from the other system, the payload itself is the occurrence: the
            // same document posted twice is the same delivery
            occurrence ??= Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(delivery.Body ?? []))[..32];

            return [.. keys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => new IntegrationEvent(x.Trim(), name, $"{occurrence}:{x.Trim()}"))];
        }

        /// <summary>
        /// Reads a string property.
        /// </summary>
        /// <param name="root">The document.</param>
        /// <param name="name">The property.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        private static string Read(JsonElement root, string name)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => null
                    };
                }
            }

            return null;
        }
    }
}
