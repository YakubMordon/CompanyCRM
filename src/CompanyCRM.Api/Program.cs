using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using CompanyCRM.Api.Hubs;
using CompanyCRM.Api.Security;
using CompanyCRM.Api.Services;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Sanitize Visual Studio hosting startup assemblies that fail to execute under .NET 9
var rawHostingStartups = Environment.GetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES");
if (!string.IsNullOrWhiteSpace(rawHostingStartups))
{
    var loadableAssemblies = rawHostingStartups
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(name => !name.Equals("Microsoft.WebTools.ApiEndpointDiscovery", StringComparison.OrdinalIgnoreCase))
        .Where(name =>
        {
            try
            {
                System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(name));
                return true;
            }
            catch
            {
                return false;
            }
        })
        .ToArray();

    Environment.SetEnvironmentVariable(
        "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES",
        loadableAssemblies.Length > 0 ? string.Join(';', loadableAssemblies) : null);
}

var builder = WebApplication.CreateBuilder(args);

// 1. Aspire Service Defaults (OpenTelemetry, HealthChecks, HTTP Resilience)
builder.AddServiceDefaults();

// 2. Controllers & JSON Options
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problemDetails = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(context.ModelState);
            var firstError = problemDetails.Errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Validation failed.";
            problemDetails.Extensions["message"] = firstError;
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problemDetails);
        };
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 3. Infrastructure & Dual-Mode Database Registration
builder.Services.AddCrmInfrastructure(builder.Configuration);

// 4. Token & Security Services
builder.Services.AddScoped<ITokenService, TokenService>();

// 5. SignalR Real-Time Hub
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 6. CORS Policy ("Frontend" for React Vite client)
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 7. JWT Bearer Authentication & WebSocket Query Token Extraction
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "CompanyCRM_SuperSecret_Jwt_Key_For_Development_Only_Min256Bits!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "CompanyCRM";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "CompanyCRM.Web";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        RoleClaimType = ClaimTypes.Role,
        ClockSkew = TimeSpan.Zero
    };

    // Extract token from query string on SignalR WebSocket handshake
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/crm"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// 8. Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("HigherManagement", policy =>
        policy.RequireRole(nameof(AppRole.HigherManagement)));

    options.AddPolicy("TeamLeadOrAbove", policy =>
        policy.RequireRole(nameof(AppRole.HigherManagement), nameof(AppRole.TeamLead)));

    options.AddPolicy("ReviewerOrAbove", policy =>
        policy.RequireRole(nameof(AppRole.HigherManagement), nameof(AppRole.TeamLead)));
});

// 9. Swagger / OpenAPI Configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CompanyCRM API",
        Version = "v1",
        Description = "Enterprise CRM, Project Management, and Time-Tracking Web API."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using Bearer scheme. Format: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 10. Database Creation & Demo Data Seeding on Startup
await DatabaseSeeder.SeedDemoDataAsync(app.Services);

// 11. Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CompanyCRM API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseRouting();

// CORS precedes Authentication & Authorization
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

// 12. Hangfire Dashboard & Recurring Jobs
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthorizationFilter() },
    DashboardTitle = "CompanyCRM Job Dashboard"
});

if (!app.Environment.IsEnvironment("Testing"))
{
    try
    {
        RecurringJob.AddOrUpdate<ExternalSyncReconciliationJob>(
            "external-sync-reconciliation",
            job => job.RunReconciliationAsync(CancellationToken.None),
            "*/15 * * * *");

        RecurringJob.AddOrUpdate<DailyPlanCutoffReminderJob>(
            "daily-plan-cutoff-reminder",
            job => job.ExecuteCutoffCheckAsync(CancellationToken.None),
            "*/15 * * * *");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to register recurring jobs: {Message}", ex.Message);
    }
}

// 13. Map Endpoints
app.MapDefaultEndpoints();
app.MapControllers();
app.MapHub<CrmHub>("/hubs/crm");

app.Run();

// Needed for WebApplicationFactory<Program> in integration test projects
public partial class Program { }
