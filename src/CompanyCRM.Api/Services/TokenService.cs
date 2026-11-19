using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CompanyCRM.Api.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(Employee employee)
    {
        return GenerateToken(
            employee.Id,
            employee.Email,
            employee.FullName,
            employee.Role,
            employee.Department,
            employee.JobTitle);
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(
        Guid id,
        string email,
        string fullName,
        AppRole role,
        string department,
        string jobTitle)
    {
        var secret = _configuration["Jwt:Secret"] ?? "CompanyCRM_SuperSecret_Jwt_Key_For_Development_Only_Min256Bits!";
        var issuer = _configuration["Jwt:Issuer"] ?? "CompanyCRM";
        var audience = _configuration["Jwt:Audience"] ?? "CompanyCRM.Web";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new("sub", id.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Role, role.ToString()),
            new("role", role.ToString()),
            new("department", department ?? string.Empty),
            new("jobTitle", jobTitle ?? string.Empty)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return (tokenHandler.WriteToken(token), expiresAt);
    }
}
