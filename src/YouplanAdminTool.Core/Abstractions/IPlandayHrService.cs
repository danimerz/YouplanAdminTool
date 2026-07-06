using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Abstractions;

/// <summary>Zugriff auf die HR-API von Planday (Mitarbeiter und Abteilungen).</summary>
public interface IPlandayHrService
{
    Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
}
