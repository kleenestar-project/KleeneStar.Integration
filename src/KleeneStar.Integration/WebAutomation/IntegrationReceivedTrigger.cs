using KleeneStar.Automation.WebAutomation.Triggers;

namespace KleeneStar.Integration.WebAutomation
{
    /// <summary>
    /// Another system reported an event through an incoming webhook; the parameter narrows to
    /// the event or the hook. The automation plugin's trigger manager finds it like its own,
    /// because it is a public sealed trigger of a plugin.
    /// </summary>
    public sealed class IntegrationReceivedTrigger : AutomationTrigger
    {
        /// <summary>
        /// The key of the trigger.
        /// </summary>
        public const string TriggerKey = "integration.received";

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public IntegrationReceivedTrigger()
            : base(TriggerKey, "kleenestar.integration:automation.trigger.integration.received", "kleenestar.integration:automation.trigger.integration.received.parameter")
        {
        }
    }
}
