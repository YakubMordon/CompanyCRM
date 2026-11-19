using Hangfire.Dashboard;

namespace CompanyCRM.Api.Security;

public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Allow all local requests in development / testing environments
        if (httpContext.Request.Host.Host == "localhost" || httpContext.Request.Host.Host == "127.0.0.1")
        {
            return true;
        }

        // Allow authenticated users in role HigherManagement or TeamLead
        return httpContext.User.Identity?.IsAuthenticated == true &&
               (httpContext.User.IsInRole("HigherManagement") || httpContext.User.IsInRole("TeamLead"));
    }
}
