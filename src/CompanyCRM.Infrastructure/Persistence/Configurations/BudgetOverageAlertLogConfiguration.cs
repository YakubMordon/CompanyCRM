using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class BudgetOverageAlertLogConfiguration : IEntityTypeConfiguration<BudgetOverageAlertLog>
{
    public void Configure(EntityTypeBuilder<BudgetOverageAlertLog> builder)
    {
        builder.ToTable("BudgetOverageAlertLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.UtilizationPercent).HasPrecision(8, 2);
        builder.Property(a => a.LoggedAmount).HasPrecision(18, 2);
        builder.Property(a => a.BudgetAmount).HasPrecision(18, 2);

        builder.HasIndex(a => new { a.ProjectId, a.Year, a.Month, a.AlertLevel })
            .IsUnique()
            .HasDatabaseName("IX_BudgetOverageAlertLogs_Project_Period_Level");

        builder.HasOne(a => a.Project)
            .WithMany()
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
