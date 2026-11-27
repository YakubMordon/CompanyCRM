using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class ClientRetainerConfiguration : IEntityTypeConfiguration<ClientRetainer>
{
    public void Configure(EntityTypeBuilder<ClientRetainer> builder)
    {
        builder.ToTable("ClientRetainers");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ClientName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(r => r.MonthlyRetainerHours).HasPrecision(18, 2);
        builder.Property(r => r.MonthlyBudgetAmount).HasPrecision(18, 2);
        builder.Property(r => r.WarningThresholdPercent).HasPrecision(5, 2);

        builder.HasIndex(r => r.ProjectId)
            .IsUnique()
            .HasDatabaseName("IX_ClientRetainers_ProjectId");

        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
