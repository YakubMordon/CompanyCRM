namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;

public interface IPlanningCutoffJob
{
    /// <summary>Executes the daily plan cutoff check algorithm across active employees and dispatches reminders.</summary>
    Task<CutoffExecutionSummaryDto> ExecuteCutoffCheckAsync(CancellationToken ct = default);

    /// <summary>Executes the daily plan cutoff check using the specified project's cutoff configuration, falling back to global default.</summary>
    Task<CutoffExecutionSummaryDto> ExecuteCutoffCheckAsync(Guid? projectId, CancellationToken ct = default);
}
