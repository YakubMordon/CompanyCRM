namespace CompanyCRM.Application.Services;

using System;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Enums;

/// <summary>
/// Production RBAC authorization service evaluating route and feature permissions.
/// Enforces strict role gating according to CRM security policies:
/// - Unauthenticated requests return HTTP 401 Unauthorized.
/// - Management portal &amp; endpoints (/api/management/*) strictly require HigherManagement; returns 403 Forbidden to Employee/TeamLead.
/// - Planning cutoff configuration (/api/planning/cutoff-config) requires TeamLead or HigherManagement.
/// - Review queue requires TeamLead or HigherManagement.
/// - Public directory requires authentication.
/// </summary>
public class RbacAuthorizationService : IRbacAuthorizationService
{
    /// <summary>
    /// Shared singleton instance for lightweight callers or static evaluation.
    /// </summary>
    public static IRbacAuthorizationService Instance { get; } = new RbacAuthorizationService();

    /// <inheritdoc />
    public bool CanAccessPublicDirectory(SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsAuthenticated;
    }

    /// <inheritdoc />
    public bool CanAccessReviewQueue(SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsAuthenticated && (context.Role == AppRole.TeamLead || context.Role == AppRole.HigherManagement);
    }

    /// <inheritdoc />
    public bool CanManageCutoffConfig(SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsAuthenticated && (context.Role == AppRole.TeamLead || context.Role == AppRole.HigherManagement);
    }

    /// <inheritdoc />
    public bool CanAccessManagementPortal(SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsAuthenticated && context.Role == AppRole.HigherManagement;
    }

    /// <inheritdoc />
    public int GetAuthorizationStatusCode(SecurityContext context, string endpoint, string httpMethod = "GET")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        if (!context.IsAuthenticated)
        {
            return 401; // HTTP 401 Unauthorized
        }

        // Normalize route by handling absolute URIs, query strings, and trailing slashes
        string path;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }
        else
        {
            path = endpoint.Trim().Split('?')[0];
        }

        var normalizedPath = path.TrimEnd('/');

        // /api/management and all sub-routes strictly require HigherManagement
        if (normalizedPath.Equals("/api/management", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith("/api/management/", StringComparison.OrdinalIgnoreCase))
        {
            return context.Role == AppRole.HigherManagement ? 200 : 403;
        }

        // Planning cutoff configuration requires TeamLead or HigherManagement
        if (normalizedPath.Equals("/api/planning/cutoff-config", StringComparison.OrdinalIgnoreCase))
        {
            return (context.Role == AppRole.TeamLead || context.Role == AppRole.HigherManagement) ? 200 : 403;
        }

        // Review queue requires TeamLead or HigherManagement
        if (normalizedPath.Equals("/api/reviews", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith("/api/reviews/", StringComparison.OrdinalIgnoreCase))
        {
            return (context.Role == AppRole.TeamLead || context.Role == AppRole.HigherManagement) ? 200 : 403;
        }

        // Standard authenticated endpoints default to 200 OK
        return 200; // HTTP 200 OK
    }

    /// <inheritdoc />
    public bool IsAuthorized(SecurityContext context, string endpoint, string httpMethod = "GET")
    {
        return GetAuthorizationStatusCode(context, endpoint, httpMethod) == 200;
    }
}
