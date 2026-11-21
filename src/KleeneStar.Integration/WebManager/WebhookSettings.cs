using Microsoft.Extensions.Configuration;

namespace KleeneStar.Integration.WebManager
{
    /// <summary>
    /// The settings of the outgoing webhooks, read from
    /// <c>Plugins:kleenestar.integration:Webhooks</c>.
    /// </summary>
    public sealed class WebhookSettings
    {
        /// <summary>
        /// The name of the section beneath the plugin settings.
        /// </summary>
        public const string Section = "Webhooks";

        /// <summary>
        /// Gets or sets a value indicating whether an outgoing webhook may reach this host and
        /// the private networks it can reach (loopback, link local, private and carrier grade
        /// ranges, unique local IPv6).
        /// </summary>
        /// <remarks>
        /// Off by default: a workspace administrator defines the address and reads back the
        /// answer, so an open transport would let them probe services inside the network. An
        /// installation whose build servers sit in the private network turns it on.
        /// </remarks>
        public bool AllowPrivateNetwork { get; set; }

        /// <summary>
        /// Reads the settings from the plugin settings.
        /// </summary>
        /// <param name="pluginSettings">The plugin settings, or null.</param>
        /// <returns>The settings; the defaults where a value is missing.</returns>
        public static WebhookSettings From(IConfiguration pluginSettings)
        {
            var settings = new WebhookSettings();

            pluginSettings?.GetSection(Section).Bind(settings);

            return settings;
        }
    }
}
