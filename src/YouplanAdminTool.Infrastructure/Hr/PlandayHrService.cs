using System.Net.Http.Json;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Http;

namespace YouplanAdminTool.Infrastructure.Hr;

/// <summary>Typisierter Client für die Planday HR-API (Mitarbeiter und Abteilungen). Stammdaten
/// ändern sich selten, daher werden sie zwischengespeichert statt bei jeder Aktualisierung
/// (inkl. automatischem Polling) komplett neu geladen zu werden.</summary>
public sealed class PlandayHrService : IPlandayHrService
{
    private const int PageSize = 50; // Maximum, das die Planday HR-API pro Anfrage erlaubt.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _employeesCacheLock = new(1, 1);
    private readonly SemaphoreSlim _departmentsCacheLock = new(1, 1);
    private IReadOnlyList<Employee>? _cachedEmployees;
    private DateTimeOffset _employeesCachedAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<Department>? _cachedDepartments;
    private DateTimeOffset _departmentsCachedAtUtc = DateTimeOffset.MinValue;

    public PlandayHrService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedEmployees is not null && DateTimeOffset.UtcNow - _employeesCachedAtUtc < CacheDuration)
        {
            return _cachedEmployees;
        }

        await _employeesCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedEmployees is not null && DateTimeOffset.UtcNow - _employeesCachedAtUtc < CacheDuration)
            {
                return _cachedEmployees;
            }

            var results = await FetchAllPagesAsync<EmployeeDto>(
                offset => new QueryStringBuilder().Add("limit", PageSize).Add("offset", offset).Build("hr/v1.0/employees"),
                "Mitarbeiter",
                cancellationToken);

            _cachedEmployees = results.Select(dto => new Employee(
                    dto.Id,
                    dto.FirstName ?? string.Empty,
                    dto.LastName ?? string.Empty,
                    dto.Departments ?? []))
                .ToList();
            _employeesCachedAtUtc = DateTimeOffset.UtcNow;
            return _cachedEmployees;
        }
        finally
        {
            _employeesCacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedDepartments is not null && DateTimeOffset.UtcNow - _departmentsCachedAtUtc < CacheDuration)
        {
            return _cachedDepartments;
        }

        await _departmentsCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedDepartments is not null && DateTimeOffset.UtcNow - _departmentsCachedAtUtc < CacheDuration)
            {
                return _cachedDepartments;
            }

            var results = await FetchAllPagesAsync<DepartmentDto>(
                offset => new QueryStringBuilder().Add("limit", PageSize).Add("offset", offset).Build("hr/v1.0/departments"),
                "Abteilungen",
                cancellationToken);

            _cachedDepartments = results.Select(dto => new Department(dto.Id, dto.Name ?? string.Empty)).ToList();
            _departmentsCachedAtUtc = DateTimeOffset.UtcNow;
            return _cachedDepartments;
        }
        finally
        {
            _departmentsCacheLock.Release();
        }
    }

    private async Task<List<T>> FetchAllPagesAsync<T>(Func<int, string> buildUrl, string description, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var offset = 0;
        long total;

        do
        {
            var page = await _httpClient.GetFromJsonAsync<PagedApiDataResponse<T>>(buildUrl(offset), cancellationToken)
                ?? throw new InvalidOperationException($"Planday hat keine gültige Antwort für {description} geliefert.");

            results.AddRange(page.Data ?? []);
            total = page.Paging?.Total ?? results.Count;
            offset += PageSize;
        } while (offset < total);

        return results;
    }
}
