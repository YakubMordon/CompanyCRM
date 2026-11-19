using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class EmailNotificationLogConfiguration : IEntityTypeConfiguration<EmailNotificationLog>
{
    public void Configure(EntityTypeBuilder<EmailNotificationLog> builder)
    {
        builder.ToTable("EmailNotificationLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.RecipientEmail)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(l => l.RecipientName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(l => l.Subject)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(l => l.TriggerEvent)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasOne(l => l.WorkItem)
            .WithMany()
            .HasForeignKey(l => l.WorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(l => l.SentAt)
            .HasDatabaseName("IX_EmailNotificationLogs_SentAt");

        builder.HasIndex(l => l.RecipientEmail)
            .HasDatabaseName("IX_EmailNotificationLogs_RecipientEmail");

        builder.HasIndex(l => l.WorkItemId)
            .HasDatabaseName("IX_EmailNotificationLogs_WorkItemId");
    }
}
