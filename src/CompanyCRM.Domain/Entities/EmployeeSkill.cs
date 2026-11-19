namespace CompanyCRM.Domain.Entities;

using System;

public class EmployeeSkill
{
    public const int MinProficiency = 1;
    public const int MaxProficiency = 5;

    private int _proficiencyLevel = MinProficiency;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public string SkillName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Skill proficiency rating strictly bounded between 1 (Novice) and 5 (Expert).
    /// </summary>
    public int ProficiencyLevel
    {
        get => _proficiencyLevel;
        set => SetProficiency(value);
    }

    public decimal YearsOfExperience { get; set; } = 1.0m;
    public DateTimeOffset? LastEvaluatedAt { get; set; } = DateTimeOffset.UtcNow;

    public EmployeeSkill()
    {
    }

    public EmployeeSkill(string skillName, string category, int proficiencyLevel, decimal yearsOfExperience = 1.0m)
    {
        SkillName = skillName;
        Category = category;
        SetProficiency(proficiencyLevel);
        YearsOfExperience = yearsOfExperience;
    }

    /// <summary>
    /// Updates the proficiency level, enforcing domain invariant [1, 5].
    /// </summary>
    /// <param name="level">Target proficiency rating (1 to 5).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if level is outside [1, 5].</exception>
    public void SetProficiency(int level)
    {
        if (level < MinProficiency || level > MaxProficiency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                $"Proficiency level must be between {MinProficiency} and {MaxProficiency}.");
        }

        _proficiencyLevel = level;
    }
}
