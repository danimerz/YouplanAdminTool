using System.Net.Http.Json;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Http;

namespace YouplanAdminTool.Infrastructure.Hr;

/// <summary>Typisierter Client für die Planday HR-API (Mitarbeiter und Abteilungen).</summary>
public sealed class PlandayHrService : IPlandayHrService
{
    private const int PageSize = 50; // Maximum, das die Planday HR-API pro Anfrage erlaubt.

    private readonly HttpClient _httpClient;

    public PlandayHrService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<EmployeeDto>();
        var offset = 0;
        long total;

        do
        {
            var url = new QueryStringBuilder()
                .Add("limit", PageSize)
                .Add("offset", offset)
                .Build("hr/v1.0/employees");

            var page = await _httpClient.GetFromJsonAsync<PagedApiDataResponse<EmployeeDto>>(url, cancellationToken)
                ?? throw new InvalidOperationException("Planday hat keine gültige Antwort für Mitarbeiter geliefert.");

            results.AddRange(page.Data ?? []);
            total = page.Paging?.Total ?? results.Count;
            offset += PageSize;
        } while (offset < total);

        return results.Select(dto => new Employee(
                dto.Id,
                dto.FirstName ?? string.Empty,
                dto.LastName ?? string.Empty,
                dto.Departments ?? []))
            .ToList();
    }

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DepartmentDto>();
        var offset = 0;
        long total;

        do
        {
            var url = new QueryStringBuilder()
                .Add("limit", PageSize)
                .Add("offset", offset)
                .Build("hr/v1.0/departments");

            var page = await _httpClient.GetFromJsonAsync<PagedApiDataResponse<DepartmentDto>>(url, cancellationToken)
                ?? throw new InvalidOperationException("Planday hat keine gültige Antwort für Abteilungen geliefert.");

            results.AddRange(page.Data ?? []);
            total = page.Paging?.Total ?? results.Count;
            offset += PageSize;
        } while (offset < total);

        return results.Select(dto => new Department(dto.Id, dto.Name ?? string.Empty)).ToList();
    }
}
