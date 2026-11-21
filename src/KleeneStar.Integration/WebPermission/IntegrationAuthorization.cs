using KleeneStar.Core;
using KleeneStar.Core.WebParameter;
using KleeneStar.Core.WebPermission;
using KleeneStar.Core.WebRestApi;
using KleeneStar.Model.Entities;
using WebExpress.WebCore.WebMessage;

namespace KleeneStar.Integration.WebPermission
{
    /// <summary>
    /// Who may manage the webhooks of a workspace: its administrators. A plugin page is not
    /// judged by the core's route convention, so the pages ask in <c>Process</c> and refuse with
    /// WebExpress's <c>ForbiddenException</c>; the endpoints ask the same question through
    /// <see cref="JsonApi.Authorize"/>.
    /// </summary>
    public static class IntegrationAuthorization
    {
        /// <summary>
        /// Returns the workspace of the route.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The workspace, or <see langword="null"/>.</returns>
        public static Workspace WorkspaceOf(IRequest request)
        {
            var key = request?.GetParameter<WorkspaceKeyParameter>()?.Value;

            return string.IsNullOrWhiteSpace(key) ? null : CoreHub.WorkspaceManager.GetWorkspaceByKey(key);
        }

        /// <summary>
        /// Refuses a page to a caller who does not administer the route's workspace.
        /// </summary>
        /// <param name="request">The request.</param>
        public static void Demand(IRequest request)
        {
            var workspace = WorkspaceOf(request);

            if (workspace is not null && !ContentAuthorization.MayAdministerWorkspace(workspace.Id, request))
            {
                throw PageAuthorization.Refuse();
            }
        }
    }
}
