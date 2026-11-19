using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers");

        builder.HasKey(pm => pm.Id);

        builder.HasOne(pm => pm.Project)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pm => pm.Employee)
            .WithMany(e => e.ProjectMemberships)
            .HasForeignKey(pm => pm.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pm => new { pm.ProjectId, pm.EmployeeId })
            .IsUnique()
            .HasDatabaseName("IX_ProjectMembers_ProjectId_EmployeeId");
    }
}
