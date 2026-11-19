using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class WeeklyPlanConfiguration : IEntityTypeConfiguration<WeeklyPlan>
{
    public void Configure(EntityTypeBuilder<WeeklyPlan> builder)
    {
        builder.ToTable("WeeklyPlans");

        builder.HasKey(wp => wp.Id);

        builder.Property(wp => wp.GoalsMarkdown)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(wp => wp.FocusAreasJson)
            .HasMaxLength(2000);

        builder.Property(wp => wp.KeyDeliverables)
            .HasMaxLength(2000);

        builder.HasOne(wp => wp.Employee)
            .WithMany(e => e.WeeklyPlans)
            .HasForeignKey(wp => wp.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(wp => wp.Project)
            .WithMany(p => p.WeeklyPlans)
            .HasForeignKey(wp => wp.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(wp => new { wp.EmployeeId, wp.WeekStartDate })
            .IsUnique()
            .HasDatabaseName("IX_WeeklyPlans_EmployeeId_WeekStartDate");

        builder.HasIndex(wp => wp.ProjectId)
            .HasDatabaseName("IX_WeeklyPlans_ProjectId");
    }
}
