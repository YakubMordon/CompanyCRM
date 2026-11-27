namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;

public interface IRetainerService
{
    Task<IReadOnlyList<RetainerStatusDto>> GetRetainersAsync(
        Guid? projectId = null,
        int? year = null,
        int? month = null,
        CancellationToken ct = default);

    Task<RetainerStatusDto> UpsertRetainerAsync(
        UpsertRetainerRequestDto request,
        CancellationToken ct = default);

    Task DeleteRetainerAsync(
        Guid id,
        CancellationToken ct = default);

    Task<BudgetOverageEvaluationResultDto> EvaluateBudgetOveragesAsync(
        int? year = null,
        int? month = null,
        CancellationToken ct = default);
}
