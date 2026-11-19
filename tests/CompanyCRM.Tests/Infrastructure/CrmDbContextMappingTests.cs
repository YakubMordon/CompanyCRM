using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

public class CrmDbContextMappingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;

    public CrmDbContextMappingTests()
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

    [Fact]
    public async Task Database_EnsureCreated_OnSqliteInMemory_SucceedsWithAll14Tables()
    {
        using var context = CreateContext();
        var created = await context.Database.EnsureCreatedAsync();
        created.Should().BeTrue();

        // Verify all 14 DbSets are accessible and can be queried without throwing
        (await context.Projects.CountAsync()).Should().Be(0);
        (await context.ProjectMembers.CountAsync()).Should().Be(0);
        (await context.WorkItems.CountAsync()).Should().Be(0);
        (await context.WorkItemHistories.CountAsync()).Should().Be(0);
        (await context.TimeEntries.CountAsync()).Should().Be(0);
        (await context.Employees.CountAsync()).Should().Be(0);
        (await context.EmployeeSkills.CountAsync()).Should().Be(0);
        (await context.WeeklyPlans.CountAsync()).Should().Be(0);
        (await context.DailyPlans.CountAsync()).Should().Be(0);
        (await context.DailyPlanItems.CountAsync()).Should().Be(0);
        (await context.PlanningCutoffConfigs.CountAsync()).Should().Be(0);
        (await context.DailyPlanReminderLogs.CountAsync()).Should().Be(0);
        (await context.SyncConflicts.CountAsync()).Should().Be(0);
        (await context.EmailNotificationLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Project_UniqueCodeIndex_DuplicateCode_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.Projects.Add(new Project { Code = "CRM", Name = "First Project" });
        await context.SaveChangesAsync();

        context.Projects.Add(new Project { Code = "CRM", Name = "Duplicate Project" });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ProjectMember_CompositeUniqueIndex_DuplicateEmployeeInProject_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "PRJ", Name = "Project Alpha" };
        var employee = new Employee { FullName = "John Doe", Email = "john@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, EmployeeId = employee.Id, Role = ProjectRole.Member });
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, EmployeeId = employee.Id, Role = ProjectRole.Lead });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DailyPlan_CompositeUniqueIndex_DuplicateEmployeeAndDate_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee { FullName = "Alice Dev", Email = "alice@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var date = new DateOnly(2026, 11, 14);
        context.DailyPlans.Add(new DailyPlan { EmployeeId = employee.Id, PlanDate = date });
        await context.SaveChangesAsync();

        context.DailyPlans.Add(new DailyPlan { EmployeeId = employee.Id, PlanDate = date });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DailyPlanReminderLog_CompositeUniqueIndex_DuplicateEmployeeAndDate_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee { FullName = "Bob Dev", Email = "bob@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var date = new DateOnly(2026, 11, 14);
        context.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
        {
            EmployeeId = employee.Id,
            PlanDate = date,
            RecipientEmail = employee.Email,
            EmailDeliveryStatus = "Sent"
        });
        await context.SaveChangesAsync();

        context.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
        {
            EmployeeId = employee.Id,
            PlanDate = date,
            RecipientEmail = employee.Email,
            EmailDeliveryStatus = "Sent"
        });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task EmployeeSkill_CompositeUniqueIndex_DuplicateEmployeeAndSkill_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee { FullName = "Charlie Dev", Email = "charlie@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        context.EmployeeSkills.Add(new EmployeeSkill { EmployeeId = employee.Id, SkillName = "C#", Category = "Backend", ProficiencyLevel = 4 });
        await context.SaveChangesAsync();

        context.EmployeeSkills.Add(new EmployeeSkill { EmployeeId = employee.Id, SkillName = "C#", Category = "Backend", ProficiencyLevel = 5 });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task WeeklyPlan_CompositeUniqueIndex_DuplicateEmployeeAndWeekStart_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee { FullName = "Dan Dev", Email = "dan@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var monday = new DateOnly(2026, 11, 9);
        context.WeeklyPlans.Add(new WeeklyPlan { EmployeeId = employee.Id, WeekStartDate = monday, GoalsMarkdown = "Goal 1" });
        await context.SaveChangesAsync();

        context.WeeklyPlans.Add(new WeeklyPlan { EmployeeId = employee.Id, WeekStartDate = monday, GoalsMarkdown = "Goal 2" });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task WorkItem_CascadeDelete_DeletesWorkItemHistoriesAndTimeEntries()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CAS", Name = "Cascade Project" };
        var employee = new Employee { FullName = "Eva Dev", Email = "eva@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CAS-101",
            Title = "Cascade Test Item"
        };
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        context.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = workItem.Id,
            ChangedById = employee.Id,
            Action = "Created"
        });
        context.TimeEntries.Add(new TimeEntry
        {
            WorkItemId = workItem.Id,
            EmployeeId = employee.Id,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            EndedAt = DateTimeOffset.UtcNow,
            DurationMinutes = 60,
            LoggedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        await context.SaveChangesAsync();

        // Delete the WorkItem
        context.WorkItems.Remove(workItem);
        await context.SaveChangesAsync();

        (await context.WorkItems.CountAsync()).Should().Be(0);
        (await context.WorkItemHistories.CountAsync()).Should().Be(0);
        (await context.TimeEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PrecisionAndDecimalHandling_FinancialFields_PreservesExactValues()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee
        {
            FullName = "Financial Test",
            Email = "finance@company.com",
            Department = "Finance",
            JobTitle = "Analyst",
            AnnualBaseSalary = 125450.75m,
            HourlyCostRate = 60.31m,
            HourlyBillableRate = 185.50m,
            WeeklyCapacityHours = 37.50m,
            TargetUtilizationPercent = 82.50m
        };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        using var readContext = CreateContext();
        var saved = await readContext.Employees.FirstAsync(e => e.Id == employee.Id);
        saved.AnnualBaseSalary.Should().Be(125450.75m);
        saved.HourlyCostRate.Should().Be(60.31m);
        saved.HourlyBillableRate.Should().Be(185.50m);
        saved.WeeklyCapacityHours.Should().Be(37.50m);
        saved.TargetUtilizationPercent.Should().Be(82.50m);
    }

    [Fact]
    public async Task DatabaseSeeder_SeedsAllEntitiesAndRelationships_WithoutErrors()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var seeder = new DatabaseSeeder(context, NullLogger<DatabaseSeeder>.Instance);
        await seeder.SeedAsync();

        (await context.Employees.CountAsync()).Should().Be(6);
        (await context.Projects.CountAsync()).Should().Be(3);
        (await context.WorkItems.CountAsync()).Should().Be(14);
        (await context.TimeEntries.CountAsync()).Should().BeGreaterThan(0);
        (await context.DailyPlans.CountAsync()).Should().BeGreaterThan(0);
        (await context.WeeklyPlans.CountAsync()).Should().BeGreaterThan(0);
        (await context.PlanningCutoffConfigs.CountAsync()).Should().Be(2);
        (await context.SyncConflicts.CountAsync()).Should().Be(1);
        (await context.EmailNotificationLogs.CountAsync()).Should().Be(3);

        // Subsequent seed should be idempotent
        await seeder.SeedAsync();
        (await context.Employees.CountAsync()).Should().Be(6);
    }
}
