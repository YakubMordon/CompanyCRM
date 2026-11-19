using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class DailyPlanConfiguration : IEntityTypeConfiguration<DailyPlan>
{
    public void Configure(EntityTypeBuilder<DailyPlan> builder)
    {
        builder.ToTable("DailyPlans");

        builder.HasKey(dp => dp.Id);

        builder.Property(dp => dp.Blockers)
            .HasMaxLength(2000);

        builder.Property(dp => dp.SummaryNotes)
            .HasMaxLength(2000);

        builder.HasOne(dp => dp.Employee)
            .WithMany(e => e.DailyPlans)
            .HasForeignKey(dp => dp.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(dp => dp.Items)
            .WithOne(dpi => dpi.DailyPlan)
            .HasForeignKey(dpi => dpi.DailyPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(dp => new { dp.EmployeeId, dp.PlanDate })
            .IsUnique()
            .HasDatabaseName("IX_DailyPlans_EmployeeId_PlanDate");
    }
}
