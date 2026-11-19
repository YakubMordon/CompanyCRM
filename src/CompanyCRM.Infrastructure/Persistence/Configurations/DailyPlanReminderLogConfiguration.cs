using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class DailyPlanReminderLogConfiguration : IEntityTypeConfiguration<DailyPlanReminderLog>
{
    public void Configure(EntityTypeBuilder<DailyPlanReminderLog> builder)
    {
        builder.ToTable("DailyPlanReminderLogs");

        builder.HasKey(rl => rl.Id);

        builder.Property(rl => rl.RecipientEmail)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(rl => rl.EmailDeliveryStatus)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(rl => rl.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasOne(rl => rl.Employee)
            .WithMany(e => e.ReminderLogs)
            .HasForeignKey(rl => rl.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(rl => rl.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rl => new { rl.EmployeeId, rl.PlanDate })
            .IsUnique()
            .HasFilter("\"ProjectId\" IS NULL")
            .HasDatabaseName("IX_DailyPlanReminderLogs_EmployeeId_PlanDate");

        builder.HasIndex(rl => new { rl.EmployeeId, rl.PlanDate, rl.ProjectId })
            .IsUnique()
            .HasFilter("\"ProjectId\" IS NOT NULL")
            .HasDatabaseName("IX_DailyPlanReminderLogs_EmployeeId_PlanDate_ProjectId");
    }
}
