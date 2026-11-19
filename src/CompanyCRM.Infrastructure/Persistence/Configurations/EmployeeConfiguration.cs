using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(e => e.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(e => e.Department)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.JobTitle)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.WeeklyCapacityHours)
            .HasPrecision(5, 2);

        builder.Property(e => e.Bio)
            .HasMaxLength(2000);

        builder.Property(e => e.LayoutPreferencesJson)
            .HasMaxLength(4000);

        builder.Property(e => e.AnnualBaseSalary)
            .HasPrecision(18, 2);

        builder.Property(e => e.HourlyCostRate)
            .HasPrecision(18, 2);

        builder.Property(e => e.HourlyBillableRate)
            .HasPrecision(18, 2);

        builder.Property(e => e.TargetUtilizationPercent)
            .HasPrecision(5, 2);

        builder.HasIndex(e => e.Email)
            .IsUnique()
            .HasDatabaseName("IX_Employees_Email");

        builder.HasIndex(e => e.Role)
            .HasDatabaseName("IX_Employees_Role");

        builder.HasMany(e => e.ProjectMemberships)
            .WithOne(pm => pm.Employee)
            .HasForeignKey(pm => pm.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Skills)
            .WithOne(s => s.Employee)
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.WeeklyPlans)
            .WithOne(wp => wp.Employee)
            .HasForeignKey(wp => wp.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.DailyPlans)
            .WithOne(dp => dp.Employee)
            .HasForeignKey(dp => dp.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ReminderLogs)
            .WithOne(rl => rl.Employee)
            .HasForeignKey(rl => rl.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
