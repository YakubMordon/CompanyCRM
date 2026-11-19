using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Api.Services;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) GenerateToken(Employee employee);
    (string Token, DateTimeOffset ExpiresAt) GenerateToken(Guid id, string email, string fullName, AppRole role, string department, string jobTitle);
}
