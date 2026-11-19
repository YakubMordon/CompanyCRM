using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class EmployeeSkillConfiguration : IEntityTypeConfiguration<EmployeeSkill>
{
    public void Configure(EntityTypeBuilder<EmployeeSkill> builder)
    {
        builder.ToTable("EmployeeSkills");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SkillName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.YearsOfExperience)
            .HasPrecision(4, 1);

        builder.HasOne(s => s.Employee)
            .WithMany(e => e.Skills)
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.EmployeeId, s.SkillName })
            .IsUnique()
            .HasDatabaseName("IX_EmployeeSkills_EmployeeId_SkillName");
    }
}
