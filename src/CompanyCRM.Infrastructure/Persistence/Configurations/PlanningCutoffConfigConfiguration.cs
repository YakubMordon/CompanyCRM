using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class PlanningCutoffConfigConfiguration : IEntityTypeConfiguration<PlanningCutoffConfig>
{
    public void Configure(EntityTypeBuilder<PlanningCutoffConfig> builder)
    {
        builder.ToTable("PlanningCutoffConfigs");

        builder.HasKey(cc => cc.Id);

        builder.Property(cc => cc.TimeZoneId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cc => cc.ExcludedDaysOfWeekJson)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasOne(cc => cc.Project)
            .WithMany(p => p.CutoffConfigs)
            .HasForeignKey(cc => cc.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(cc => cc.ProjectId)
            .HasDatabaseName("IX_PlanningCutoffConfigs_ProjectId");
    }
}
