using KleeneStar.Integration.WebIntegration;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using WebExpress.WebCore;
using WebExpress.WebCore.WebComponent;
using WebExpress.WebCore.WebPlugin;

namespace KleeneStar.Integration.WebManager
{
    /// <summary>
    /// The open catalog of integrations: every <b>public sealed</b> <see cref="IIntegration"/>
    /// with a public parameterless constructor of every plugin - the generic one of this plugin,
    /// GitLab, Jenkins and Automic, and whatever another plugin ships. There is no enum of
    /// integrations; WebExpress's <see cref="PluginTypeCatalog{TItem}"/> collects them.
    /// </summary>
    public sealed class IntegrationManager : IIntegrationManager
    {
        private readonly PluginTypeCatalog<IIntegration> _catalog = new(x => x.Key);

        /// <summary>
        /// The key of the generic integration.
        /// </summary>
        public const string Generic = "generic";

        /// <summary>
        /// Initializes a new instance of the class. Invoked by WebExpress via reflection.
        /// </summary>
        /// <param name="componentHub">The component hub.</param>
        /// <param name="httpServerContext">The server context.</param>
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used via reflection.")]
        private IntegrationManager(IComponentHub componentHub, IHttpServerContext httpServerContext)
        {
            if (componentHub?.PluginManager is not null)
            {
                _catalog.Attach(componentHub, httpServerContext);
            }
            else
            {
                // without a plugin manager (a test fixture) the manager knows its own assembly's items
                _catalog.Collect(typeof(IntegrationManager).Assembly);
            }
        }

        /// <summary>
        /// Gets the integrations, ordered by key.
        /// </summary>
        public IReadOnlyList<IIntegration> All => [.. _catalog.Items.OrderBy(x => x.Key, StringComparer.Ordinal)];

        /// <summary>
        /// Returns an integration.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The integration, or <see langword="null"/>.</returns>
        public IIntegration Get(string key)
        {
            return _catalog.Find(key);
        }

        /// <summary>
        /// Gets a number that moves on every change of the items, so a cached picker can tell
        /// that an item arrived or left after it was built.
        /// </summary>
        public long Version => _catalog.Version;

        /// <inheritdoc/>
        public void Register(IIntegration item)
        {
            _catalog.Register(item);
        }

        /// <summary>
        /// Stops following the plugins.
        /// </summary>
        public void Dispose()
        {
            _catalog.Dispose();
        }
    }

    /// <summary>
    /// The catalog of integrations.
    /// </summary>
    public interface IIntegrationManager : IComponentManager
    {
        /// <summary>
        /// Gets the integrations, ordered by key.
        /// </summary>
        IReadOnlyList<IIntegration> All { get; }

        /// <summary>
        /// Registers an integration that is no type of its own; one under an existing key replaces it.
        /// </summary>
        /// <param name="integration">The integration.</param>
        void Register(IIntegration integration);

        /// <summary>
        /// Returns an integration.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The integration, or <see langword="null"/>.</returns>
        IIntegration Get(string key);
    }
}
