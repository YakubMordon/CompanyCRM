using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(i => i.TotalBillableHours)
            .HasPrecision(18, 2);

        builder.Property(i => i.SubtotalAmount)
            .HasPrecision(18, 2);

        builder.Property(i => i.TaxRatePercent)
            .HasPrecision(5, 2);

        builder.Property(i => i.TaxAmount)
            .HasPrecision(18, 2);

        builder.Property(i => i.TotalAmount)
            .HasPrecision(18, 2);

        builder.HasIndex(i => new { i.ProjectId, i.Year, i.Month })
            .IsUnique()
            .HasDatabaseName("IX_Invoices_ProjectId_Year_Month");

        builder.HasOne(i => i.Project)
            .WithMany()
            .HasForeignKey(i => i.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.LineItems)
            .WithOne(li => li.Invoice)
            .HasForeignKey(li => li.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
