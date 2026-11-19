namespace CompanyCRM.Application.Services;

using System.Security.Cryptography;
using System.Text;
using CompanyCRM.Domain.Enums;

public static class SyncContentHasher
{
    /// <summary>
    /// Computes a deterministic SHA-256 canonical hash across mutable work item synchronization fields.
    /// Used for infinite echo loop prevention between CompanyCRM and external issue trackers (Azure DevOps).
    /// </summary>
    public static string ComputeHash(
        string title, 
        string? description, 
        WorkItemStatus status, 
        WorkItemType type, 
        string? assigneeEmail)
    {
        // 1. Whitespace & null normalization
        var normalizedTitle = (title ?? string.Empty).Trim();
        
        // 2. Line ending normalization (CRLF -> LF) to ensure cross-platform / webhook consistency
        var normalizedDesc = (description ?? string.Empty).Replace("\r\n", "\n").Trim();
        
        // 3. Case-insensitive email normalization
        var normalizedEmail = (assigneeEmail ?? string.Empty).Trim().ToLowerInvariant();
        
        // 4. Canonical pipe-delimited string with field escaping
        var canonical = $"{EscapeField(normalizedTitle)}|{EscapeField(normalizedDesc)}|{status}|{type}|{EscapeField(normalizedEmail)}";
        
        // 5. UTF-8 SHA-256 hexadecimal encoding
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes);
    }

    private static string EscapeField(string value)
    {
        return value.Replace(@"\", @"\\").Replace("|", @"\|");
    }
}
