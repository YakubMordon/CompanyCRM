using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Application.Interfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Employee?> GetByIdWithSkillsAsync(Guid id, bool trackChanges = false, CancellationToken ct = default);
    Task<Employee?> GetByEmailWithSkillsAsync(string normalizedEmail, CancellationToken ct = default);
    Task<Employee?> GetFirstByRoleWithSkillsAsync(AppRole role, CancellationToken ct = default);
    Task<List<Employee>> GetDirectoryAsync(string? search, string? department, Guid? projectId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeEmployeeId = null, CancellationToken ct = default);
    Task AddAsync(Employee employee, CancellationToken ct = default);
    Task DeleteAsync(Employee employee, Guid? reassignCreatedItemsToId = null, CancellationToken ct = default);
    Task<List<EmployeeSkill>> GetSkillsAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmployeeSkill?> GetSkillByNameAsync(Guid employeeId, string skillName, CancellationToken ct = default);
    void AddSkill(EmployeeSkill skill);
    void RemoveSkill(EmployeeSkill skill);
    Task SaveChangesAsync(CancellationToken ct = default);
}
