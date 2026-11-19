namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Domain.Enums;

/// <summary>
/// Represents the security context of the caller for role-based access control (RBAC) evaluation.
/// </summary>
/// <param name="IsAuthenticated">Whether the caller has provided valid authentication credentials.</param>
/// <param name="Role">The application role of the caller, or null if unauthenticated.</param>
public record SecurityContext(bool IsAuthenticated, AppRole? Role);

/// <summary>
/// Application service contract governing role-based access control (RBAC) authorization evaluations.
/// Enforces HTTP 401 for unauthenticated calls and HTTP 403 for unauthorized role access on restricted endpoints.
/// </summary>
public interface IRbacAuthorizationService
{
    /// <summary>
    /// Evaluates whether the caller has permission to view the public employee directory.
    /// Requires authentication.
    /// </summary>
    bool CanAccessPublicDirectory(SecurityContext context);

    /// <summary>
    /// Evaluates whether the caller has permission to access the peer review queue.
    /// Restricted to TeamLead and HigherManagement.
    /// </summary>
    bool CanAccessReviewQueue(SecurityContext context);

    /// <summary>
    /// Evaluates whether the caller has permission to view and configure planning cutoff deadlines.
    /// Restricted to TeamLead and HigherManagement.
    /// </summary>
    bool CanManageCutoffConfig(SecurityContext context);

    /// <summary>
    /// Evaluates whether the caller has permission to access the restricted management portal and financial metrics.
    /// Strictly restricted to HigherManagement.
    /// </summary>
    bool CanAccessManagementPortal(SecurityContext context);

    /// <summary>
    /// Evaluates the HTTP authorization status code (200 OK, 401 Unauthorized, or 403 Forbidden)
    /// for a requested endpoint route and HTTP method.
    /// </summary>
    /// <param name="context">The caller's security context.</param>
    /// <param name="endpoint">The requested API endpoint route (e.g. /api/management/compensation).</param>
    /// <param name="httpMethod">The HTTP request method (defaults to "GET").</param>
    /// <returns>HTTP 200 if authorized, HTTP 401 if unauthenticated, HTTP 403 if authenticated but forbidden.</returns>
    int GetAuthorizationStatusCode(SecurityContext context, string endpoint, string httpMethod = "GET");

    /// <summary>
    /// Checks whether the caller is authorized to access the requested endpoint route.
    /// </summary>
    /// <param name="context">The caller's security context.</param>
    /// <param name="endpoint">The requested API endpoint route.</param>
    /// <param name="httpMethod">The HTTP request method (defaults to "GET").</param>
    /// <returns>True if the status code is 200 OK; otherwise, false.</returns>
    bool IsAuthorized(SecurityContext context, string endpoint, string httpMethod = "GET");
}
