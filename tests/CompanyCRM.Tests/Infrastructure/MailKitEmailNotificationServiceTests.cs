using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Email;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

public class MailKitEmailNotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;

    public MailKitEmailNotificationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CrmDbContext CreateContext() => new CrmDbContext(_options);

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrmDbContext>(opts => opts.UseSqlite(_connection));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void EmailTemplates_ReviewAssigned_ContainsCodeTitleAndReviewLink()
    {
        var html = EmailTemplates.GenerateReviewAssignedHtml(
            "Sarah Jenkins",
            "CRM-107",
            "Automated Daily Cutoff Check Background Worker",
            Guid.NewGuid());

        html.Should().Contain("Sarah Jenkins");
        html.Should().Contain("CRM-107");
        html.Should().Contain("Automated Daily Cutoff Check Background Worker");
        html.Should().Contain("/reviews");
        html.Should().Contain("Review Requested");
    }

    [Fact]
    public void EmailTemplates_ClarificationNeeded_RendersCommentAndWarningBadge()
    {
        var html = EmailTemplates.GenerateReviewDecisionHtml(
            "Alex Rivera",
            "CRM-108",
            "PostgreSQL DateOnly Conversion Offset",
            ReviewStatus.ClarificationNeeded,
            "Please attach the stack trace from the Linux container.");

        html.Should().Contain("Alex Rivera");
        html.Should().Contain("CRM-108");
        html.Should().Contain("Clarification Needed");
        html.Should().Contain("Please attach the stack trace from the Linux container.");
        html.Should().Contain("/reviews");
    }

    [Fact]
    public void EmailTemplates_Denied_RendersDenialReasonAndBlockedSyncNotice()
    {
        var html = EmailTemplates.GenerateReviewDecisionHtml(
            "Alex Rivera",
            "CRM-109",
            "Bypass Authorization Header",
            ReviewStatus.Denied,
            "Bypassing auth headers in dev violates security policy R5.");

        html.Should().Contain("Alex Rivera");
        html.Should().Contain("CRM-109");
        html.Should().Contain("Review Denied");
        html.Should().Contain("Bypassing auth headers in dev violates security policy R5.");
        html.Should().Contain("External synchronization is blocked");
    }

    [Fact]
    public void EmailTemplates_Approved_RendersApprovedNotice()
    {
        var html = EmailTemplates.GenerateReviewDecisionHtml(
            "Alex Rivera",
            "CRM-101",
            "Architect Dual-Mode Database",
            ReviewStatus.Approved,
            "Looks great, ready for deployment.");

        html.Should().Contain("Alex Rivera");
        html.Should().Contain("CRM-101");
        html.Should().Contain("Review Approved");
        html.Should().Contain("Looks great, ready for deployment.");
    }

    [Fact]
    public void EmailTemplates_CutoffReminder_RendersDateAndCutoffTime()
    {
        var date = new DateOnly(2026, 11, 18);
        var time = new TimeOnly(10, 0, 0);

        var html = EmailTemplates.GenerateCutoffReminderHtml("Maya Patel", date, time);

        html.Should().Contain("Maya Patel");
        html.Should().Contain("2026-11-18");
        html.Should().Contain("10:00");
        html.Should().Contain("/planning");
        html.Should().Contain("Action Required");
    }

    [Fact]
    public async Task SendEmail_WhenSmtpServerUnreachable_DoesNotThrow_AndLogsFailure()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Point to an unreachable port to simulate offline SMTP server
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = "59999", // Unused port that will fail connection
            ["Smtp:SenderEmail"] = "no-reply@companycrm.local",
            ["Smtp:SenderName"] = "CompanyCRM"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new MailKitEmailNotificationService(
            CreateScopeFactory(),
            configuration,
            NullLogger<MailKitEmailNotificationService>.Instance);

        // Act: should NOT throw exception! (Graceful fallback)
        var act = async () => await service.SendReviewAssignedEmailAsync(
            "reviewer@company.com",
            "Lead Reviewer",
            "CRM-999",
            "Unreachable SMTP Test",
            Guid.NewGuid());

        await act.Should().NotThrowAsync();

        // Verify that an EmailNotificationLog was persisted with Success = false
        using var verifyContext = CreateContext();
        var log = await verifyContext.EmailNotificationLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.Success.Should().BeFalse();
        log.RecipientEmail.Should().Be("reviewer@company.com");
        log.Subject.Should().Contain("CRM-999");
        log.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }
}
