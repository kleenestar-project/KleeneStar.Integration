using KleeneStar.Automation;
using KleeneStar.Integration.WebAutomation;
using KleeneStar.Model;
using KleeneStar.Model.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Concurrent;
using System.Reflection;
using WebExpress.WebCore;
using WebExpress.WebCore.WebComponent;

namespace KleeneStar.Integration.Test.Fixture
{
    /// <summary>
    /// Wires the core, the automation plugin and this plugin to an isolated in-memory database:
    /// every manager backing field of <see cref="CoreHub"/>, <see cref="AutomationHub"/> and
    /// <see cref="IntegrationHub"/> gets a fresh instance, found by reflection, so the fixture never
    /// has to be kept in step with the hubs by hand.
    /// </summary>
    /// <remarks>
    /// Without a plugin manager the automation catalogs know only the items of their own assembly,
    /// so this plugin's trigger and action are registered with them by hand - in a running host the
    /// managers find them in this plugin's assembly.
    /// </remarks>
    internal static class IntegrationHubFixture
    {
        /// <summary>
        /// Points the model at an in-memory database and builds every manager.
        /// </summary>
        /// <param name="database">A unique name of the in-memory database.</param>
        public static void Initialize(string database)
        {
            KleeneStar.Core.WebManager.ObjectPathIndex.Disable();

            ModelHub.DatabaseSettings = new DatabaseSettings
            {
                Assembly = typeof(IntegrationHubFixture).Assembly.GetName().Name,
                ConnectionString = database
            };

            foreach (var hub in new[] { typeof(CoreHub), typeof(AutomationHub), typeof(IntegrationHub) })
            {
                foreach (var field in hub.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var ctor = field.FieldType.IsClass && !field.FieldType.IsAbstract && typeof(IComponentManager).IsAssignableFrom(field.FieldType)
                        ? field.FieldType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IComponentHub), typeof(IHttpServerContext)], null)
                        : null;

                    if (ctor is not null)
                    {
                        field.SetValue(null, ctor.Invoke([null, null]));
                    }
                }
            }

            AutomationHub.AutomationTriggerManager.Register(new IntegrationReceivedTrigger());
            AutomationHub.AutomationActionManager.Register(new WebhookAction());
        }

        /// <summary>
        /// Creates a context over the same in-memory database, for the arrange phase.
        /// </summary>
        /// <param name="database">The database name.</param>
        /// <returns>The context.</returns>
        public static KleeneStarDbContext CreateDbContext(string database)
        {
            return InMemoryDbContextFactory.Create(database);
        }
    }

    /// <summary>
    /// The provider the model loads by the fixture's assembly name: the first public static
    /// <c>Create(string)</c> of this assembly.
    /// </summary>
    public static class InMemoryDbContextFactory
    {
        private static readonly ConcurrentDictionary<string, DbContextOptions<KleeneStarDbContext>> _options = new();

        /// <summary>
        /// Creates a context over the named in-memory database.
        /// </summary>
        /// <param name="connectionString">The database name.</param>
        /// <returns>The context.</returns>
        public static KleeneStarDbContext Create(string connectionString)
        {
            var key = string.IsNullOrWhiteSpace(connectionString) ? "IntegrationTestDb" : connectionString;
            var options = _options.GetOrAdd(key, name => new DbContextOptionsBuilder<KleeneStarDbContext>()
                .UseInMemoryDatabase(name)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            return new KleeneStarDbContext(options);
        }
    }
}
