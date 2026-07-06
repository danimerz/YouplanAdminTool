using System.Globalization;
using System.Net.Http.Json;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Http;

namespace YouplanAdminTool.Infrastructure.Absence;

/// <summary>Typisierter Client für die Planday Absence-API. Löst dabei Konto-Ids der Anträge
/// gegen die Kontoarten auf, um die grobe Abwesenheitsart (Urlaub, Krankheit, ...) zu bestimmen.</summary>
public sealed class PlandayAbsenceService : IPlandayAbsenceService
{
    private const int PageSize = 100;
    private static readonly TimeSpan AccountTypeCacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _accountTypeCacheLock = new(1, 1);
    private IReadOnlyList<AccountType>? _cachedAccountTypes;
    private DateTimeOffset _accountTypesCachedAtUtc = DateTimeOffset.MinValue;

    public PlandayAbsenceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<AbsenceRequest>> GetAbsenceRequestsAsync(
        AbsenceRequestQuery query, CancellationToken cancellationToken = default)
    {
        var accountTypesById = (await GetAccountTypesAsync(cancellationToken)).ToDictionary(a => a.Id);

        var results = new List<AbsenceRequestDto>();
        var offset = 0;
        long total;

        do
        {
            var qs = new QueryStringBuilder()
                .Add("startDate", query.StartDate.ToString("yyyy-MM-dd"))
                .Add("endDate", query.EndDate.ToString("yyyy-MM-dd"))
                .Add("Limit", PageSize)
                .Add("Offset", offset);

            if (query.EmployeeId is { } employeeId)
            {
                qs.Add("employeeId", employeeId.ToString(CultureInfo.InvariantCulture));
            }

            if (query.Statuses is { Count: > 0 })
            {
                qs.AddEach("status", query.Statuses.Select(s => s.ToString()));
            }

            var url = qs.Build("absence/v1.0/absencerequests");
            var page = await _httpClient.GetFromJsonAsync<PagedApiDataResponse<AbsenceRequestDto>>(url, cancellationToken)
                ?? throw new InvalidOperationException("Planday hat keine gültige Antwort für Abwesenheitsanträge geliefert.");

            results.AddRange(page.Data ?? []);
            total = page.Paging?.Total ?? results.Count;
            offset += PageSize;
        } while (offset < total);

        return results.Select(dto => MapToAbsenceRequest(dto, accountTypesById)).ToList();
    }

    public async Task<IReadOnlyList<AccountType>> GetAccountTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedAccountTypes is not null && DateTimeOffset.UtcNow - _accountTypesCachedAtUtc < AccountTypeCacheDuration)
        {
            return _cachedAccountTypes;
        }

        await _accountTypeCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccountTypes is not null && DateTimeOffset.UtcNow - _accountTypesCachedAtUtc < AccountTypeCacheDuration)
            {
                return _cachedAccountTypes;
            }

            var results = new List<AccountTypeDto>();
            var offset = 0;
            long total;

            do
            {
                var url = new QueryStringBuilder()
                    .Add("Limit", PageSize)
                    .Add("Offset", offset)
                    .Build("absence/v1.0/accounttypes");

                var page = await _httpClient.GetFromJsonAsync<PagedApiDataResponse<AccountTypeDto>>(url, cancellationToken)
                    ?? throw new InvalidOperationException("Planday hat keine gültige Antwort für Kontoarten geliefert.");

                results.AddRange(page.Data ?? []);
                total = page.Paging?.Total ?? results.Count;
                offset += PageSize;
            } while (offset < total);

            _cachedAccountTypes = results.Select(MapToAccountType).ToList();
            _accountTypesCachedAtUtc = DateTimeOffset.UtcNow;
            return _cachedAccountTypes;
        }
        finally
        {
            _accountTypeCacheLock.Release();
        }
    }

    private static AccountType MapToAccountType(AccountTypeDto dto) => new(
        dto.Id,
        dto.Name ?? string.Empty,
        ParseAbsenceType(dto.AbsenceType));

    private static AbsenceRequest MapToAbsenceRequest(AbsenceRequestDto dto, IReadOnlyDictionary<long, AccountType> accountTypesById)
    {
        var accounts = (dto.RequestedAccounts ?? [])
            .Select(a => new AbsenceAccountReference(
                a.Id,
                a.Name ?? string.Empty,
                accountTypesById.TryGetValue(a.Id, out var accountType) ? accountType.AbsenceType : AbsenceType.Unknown))
            .ToList();

        return new AbsenceRequest(
            dto.Id,
            dto.EmployeeId ?? 0,
            ParseStatus(dto.Status),
            ParseDate(dto.AbsencePeriod?.Start),
            ParseDate(dto.AbsencePeriod?.End),
            dto.Note,
            accounts);
    }

    private static AbsenceRequestStatus ParseStatus(string? status) =>
        Enum.TryParse<AbsenceRequestStatus>(status, out var parsed) ? parsed : AbsenceRequestStatus.Submitted;

    private static AbsenceType ParseAbsenceType(string? absenceType) =>
        Enum.TryParse<AbsenceType>(absenceType, out var parsed) ? parsed : AbsenceType.Unknown;

    private static DateOnly ParseDate(string? date) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : default;
}
