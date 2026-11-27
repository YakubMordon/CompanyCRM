using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.ToTable("InvoiceLineItems");

        builder.HasKey(li => li.Id);

        builder.Property(li => li.EmployeeName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(li => li.Description)
            .HasMaxLength(500);

        builder.Property(li => li.Hours)
            .HasPrecision(18, 2);

        builder.Property(li => li.HourlyRate)
            .HasPrecision(18, 2);

        builder.Property(li => li.Amount)
            .HasPrecision(18, 2);
    }
}
