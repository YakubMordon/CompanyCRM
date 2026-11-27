using System.Net;
using System.Net.Http.Json;
using System.Text;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class BillingAutomationIntegrationTests(CrmApiTestFixture fixture) : IClassFixture<CrmApiTestFixture>
{
    private HttpClient CreateAuthenticatedClient(AppRole role) =>
        fixture.CreateClientWithEmployee(Guid.NewGuid(), $"{role}@company.com", role.ToString(), role);

    [Theory]
    [InlineData(AppRole.Employee)]
    [InlineData(AppRole.TeamLead)]
    public async Task BillingAndRetainerEndpoints_ShouldReturnForbidden_ForNonHigherManagement(AppRole role)
    {
        using var client = CreateAuthenticatedClient(role);

        var retainersRes = await client.GetAsync("/api/retainers");
        retainersRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var exportRes = await client.PostAsJsonAsync("/api/invoices/export", new AccountingExportRequestDto(AccountingExportFormat.XeroCsv));
        exportRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StripeCheckout_Webhook_AndAccountingExports_ShouldWorkEndToEnd()
    {
        Guid projectId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = $"STR{Random.Shared.Next(100, 999)}",
                Name = "Stripe & Export Test Project"
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                FullName = "Sam Stripe",
                Email = $"sam.stripe.{Guid.NewGuid():N}@company.com",
                Department = "Engineering",
                JobTitle = "Staff Engineer",
                Role = AppRole.Employee,
                HourlyBillableRate = 150m
            };
            var workItem = new WorkItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                CreatedById = employee.Id,
                Title = "Stripe Integration",
                Status = WorkItemStatus.Done
            };
            var timeEntry = new TimeEntry
            {
                Id = Guid.NewGuid(),
                WorkItemId = workItem.Id,
                EmployeeId = employee.Id,
                LoggedDate = new DateOnly(2026, 6, 10),
                DurationMinutes = 120, // 2h * $150 = $300
                IsBillable = true,
                IsRunning = false
            };

            db.Projects.Add(project);
            db.Employees.Add(employee);
            db.WorkItems.Add(workItem);
            db.TimeEntries.Add(timeEntry);
            await db.SaveChangesAsync();
            projectId = project.Id;
        }

        using var client = CreateAuthenticatedClient(AppRole.HigherManagement);

        // 1. Generate invoice
        var genRes = await client.PostAsJsonAsync("/api/invoices/generate", new GenerateInvoicesRequestDto(2026, 6, projectId, 0m));
        genRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var invoices = await genRes.Content.ReadFromJsonAsync<List<InvoiceDto>>(CrmApiTestFixture.JsonOptions);
        invoices.Should().ContainSingle();
        var invoice = invoices![0];

        // 2. Create simulated Stripe checkout session
        var checkoutRes = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/stripe/checkout", new CreateStripeCheckoutRequestDto());
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await checkoutRes.Content.ReadFromJsonAsync<StripeCheckoutSessionDto>(CrmApiTestFixture.JsonOptions);
        session.Should().NotBeNull();
        session!.IsSimulated.Should().BeTrue();
        session.SessionId.Should().StartWith("cs_sim_");

        // 3. Process Stripe webhook (checkout.session.completed)
        using var anonClient = fixture.CreateClient();
        var webhookPayload = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "checkout.session.completed",
                data = new
                {
                    @object = new
                    {
                        id = session.SessionId,
                        payment_intent = "pi_test_123",
                        client_reference_id = invoice.Id.ToString()
                    }
                }
            }),
            Encoding.UTF8,
            "application/json");
        var webhookRes = await anonClient.PostAsync("/api/webhooks/stripe", webhookPayload);
        webhookRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify invoice is now Paid
        var getRes = await client.GetFromJsonAsync<List<InvoiceDto>>($"/api/invoices?projectId={projectId}&year=2026&month=6", CrmApiTestFixture.JsonOptions);
        getRes.Should().ContainSingle();
        getRes![0].Status.Should().Be(InvoiceStatus.Paid);
        getRes[0].PaidAt.Should().NotBeNull();

        // 4. Export Xero CSV & QuickBooks IIF
        var xeroRes = await client.PostAsJsonAsync("/api/invoices/export", new AccountingExportRequestDto(AccountingExportFormat.XeroCsv, projectId, 2026, 6));
        xeroRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var xeroCsv = await xeroRes.Content.ReadAsStringAsync();
        xeroCsv.Should().Contain("*ContactName,*InvoiceNumber");
        xeroCsv.Should().Contain(invoice.InvoiceNumber);

        var qbRes = await client.PostAsJsonAsync("/api/invoices/export", new AccountingExportRequestDto(AccountingExportFormat.QuickBooksIif, projectId, 2026, 6));
        qbRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var qbIif = await qbRes.Content.ReadAsStringAsync();
        qbIif.Should().Contain("!TRNS\tTRNSTYPE\tDATE");
        qbIif.Should().Contain(invoice.InvoiceNumber);
    }

    [Fact]
    public async Task RetainerTracking_AndBudgetOverageAlarms_ShouldEvaluateAndDeduplicate()
    {
        Guid projectId;
        var now = DateTime.UtcNow;
        var today = new DateOnly(now.Year, now.Month, 1);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = $"RET{Random.Shared.Next(100, 999)}",
                Name = "Retainer Alarm Test Project"
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                FullName = "Rita Retainer",
                Email = $"rita.retainer.{Guid.NewGuid():N}@company.com",
                Department = "Engineering",
                JobTitle = "Senior Engineer",
                Role = AppRole.Employee,
                HourlyBillableRate = 100m
            };
            var workItem = new WorkItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                CreatedById = employee.Id,
                Title = "Retainer Work",
                Status = WorkItemStatus.InProgress
            };
            var timeEntry = new TimeEntry
            {
                Id = Guid.NewGuid(),
                WorkItemId = workItem.Id,
                EmployeeId = employee.Id,
                LoggedDate = today,
                DurationMinutes = 600, // 10h * $100 = $1,000
                IsBillable = true,
                IsRunning = false
            };

            db.Projects.Add(project);
            db.Employees.Add(employee);
            db.WorkItems.Add(workItem);
            db.TimeEntries.Add(timeEntry);
            await db.SaveChangesAsync();
            projectId = project.Id;
        }

        using var client = CreateAuthenticatedClient(AppRole.HigherManagement);

        // Upsert retainer with $800 budget (so $1,000 logged = 125% -> Exceeded)
        var upsertRes = await client.PutAsJsonAsync("/api/retainers", new UpsertRetainerRequestDto(
            ProjectId: projectId,
            ClientName: "Overage Corp",
            MonthlyRetainerHours: 8m,
            MonthlyBudgetAmount: 800m,
            WarningThresholdPercent: 80m));
        upsertRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var retainer = await upsertRes.Content.ReadFromJsonAsync<RetainerStatusDto>(CrmApiTestFixture.JsonOptions);
        retainer!.HealthStatus.Should().Be(RetainerHealthStatus.Exceeded);
        retainer.BudgetUtilizationPercent.Should().Be(125m);

        // First evaluation triggers alert
        var eval1 = await client.PostAsync($"/api/retainers/evaluate-alerts?year={now.Year}&month={now.Month}", null);
        eval1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = await eval1.Content.ReadFromJsonAsync<BudgetOverageEvaluationResultDto>(CrmApiTestFixture.JsonOptions);
        res1!.AlertsTriggered.Should().BeGreaterThanOrEqualTo(1);
        res1.TriggeredMessages.Should().Contain(m => m.Contains("Overage Corp"));

        // Second evaluation is deduplicated (0 new alerts for Overage Corp)
        var eval2 = await client.PostAsync($"/api/retainers/evaluate-alerts?year={now.Year}&month={now.Month}", null);
        eval2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = await eval2.Content.ReadFromJsonAsync<BudgetOverageEvaluationResultDto>(CrmApiTestFixture.JsonOptions);
        res2!.TriggeredMessages.Should().NotContain(m => m.Contains("Overage Corp"));
    }
}
