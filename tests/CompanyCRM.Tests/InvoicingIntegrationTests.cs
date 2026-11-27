using System.Net;
using System.Net.Http.Headers;
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

public class InvoicingIntegrationTests(CrmApiTestFixture fixture) : IClassFixture<CrmApiTestFixture>
{
    private HttpClient CreateAuthenticatedClient(AppRole role) =>
        fixture.CreateClientWithEmployee(Guid.NewGuid(), $"{role}@company.com", role.ToString(), role);

    [Theory]
    [InlineData(AppRole.Employee)]
    [InlineData(AppRole.TeamLead)]
    public async Task InvoicesEndpoints_ShouldReturnForbidden_ForNonHigherManagement(AppRole role)
    {
        using var client = CreateAuthenticatedClient(role);

        var getRes = await client.GetAsync("/api/invoices");
        getRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var genRes = await client.PostAsJsonAsync("/api/invoices/generate", new GenerateInvoicesRequestDto(2026, 5));
        genRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GenerateUpdateAndRenderPdf_ShouldWorkEndToEnd_ForHigherManagement()
    {
        Guid projectId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = $"INV{Random.Shared.Next(100, 999)}",
                Name = "Invoicing Test Project"
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                FullName = "Jane Billing",
                Email = $"jane.billing.{Guid.NewGuid():N}@company.com",
                Department = "Engineering",
                JobTitle = "Senior Engineer",
                Role = AppRole.Employee,
                HourlyBillableRate = 120m
            };
            var workItem = new WorkItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                CreatedById = employee.Id,
                Title = "Implement Billing",
                Status = WorkItemStatus.Done
            };
            var timeEntry = new TimeEntry
            {
                Id = Guid.NewGuid(),
                WorkItemId = workItem.Id,
                EmployeeId = employee.Id,
                LoggedDate = new DateOnly(2026, 4, 15),
                DurationMinutes = 150, // 2.50 hours * $120 = $300.00
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

        // 1. Generate monthly invoice with 10% tax
        var genRes = await client.PostAsJsonAsync("/api/invoices/generate", new GenerateInvoicesRequestDto(2026, 4, projectId, 10m));
        genRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var generated = await genRes.Content.ReadFromJsonAsync<List<InvoiceDto>>(CrmApiTestFixture.JsonOptions);
        generated.Should().NotBeNull();
        generated!.Should().ContainSingle();

        var invoice = generated![0];
        invoice.TotalBillableHours.Should().Be(2.50m);
        invoice.SubtotalAmount.Should().Be(300.00m);
        invoice.TaxAmount.Should().Be(30.00m);
        invoice.TotalAmount.Should().Be(330.00m);
        invoice.Status.Should().Be(InvoiceStatus.Draft);

        // 2. Update status to Sent
        var patchRes = await client.PatchAsJsonAsync($"/api/invoices/{invoice.Id}/status", new UpdateInvoiceStatusDto(InvoiceStatus.Sent));
        patchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patchRes.Content.ReadFromJsonAsync<InvoiceDto>(CrmApiTestFixture.JsonOptions);
        updated!.Status.Should().Be(InvoiceStatus.Sent);

        // 3. Download PDF
        var pdfRes = await client.GetAsync($"/api/invoices/{invoice.Id}/pdf");
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfRes.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var pdfBytes = await pdfRes.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString([.. pdfBytes.Take(8)]).Should().StartWith("%PDF-1.4");
    }
}
