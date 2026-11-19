using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class DailyPlanItemConfiguration : IEntityTypeConfiguration<DailyPlanItem>
{
    public void Configure(EntityTypeBuilder<DailyPlanItem> builder)
    {
        builder.ToTable("DailyPlanItems");

        builder.HasKey(dpi => dpi.Id);
        builder.Property(dpi => dpi.Id).ValueGeneratedNever();

        builder.Property(dpi => dpi.CustomTaskTitle)
            .HasMaxLength(200);

        builder.Property(dpi => dpi.PlannedHours)
            .HasPrecision(5, 2);

        builder.Property(dpi => dpi.GoalNote)
            .HasMaxLength(1000);

        builder.HasOne(dpi => dpi.DailyPlan)
            .WithMany(dp => dp.Items)
            .HasForeignKey(dpi => dpi.DailyPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dpi => dpi.WorkItem)
            .WithMany(w => w.DailyPlanItems)
            .HasForeignKey(dpi => dpi.WorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(dpi => dpi.DailyPlanId)
            .HasDatabaseName("IX_DailyPlanItems_DailyPlanId");

        builder.HasIndex(dpi => dpi.WorkItemId)
            .HasDatabaseName("IX_DailyPlanItems_WorkItemId");
    }
}
