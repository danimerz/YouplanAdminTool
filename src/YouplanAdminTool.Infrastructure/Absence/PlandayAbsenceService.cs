using System.Globalization;
using System.Net.Http.Json;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Http;

namespace YouplanAdminTool.Infrastructure.Absence;

/// <summary>Typisierter Client für die Planday Absence-API. Löst dabei die Konto-Ids der Anträge
/// gegen die Kontoinstanzen und deren Kontoarten auf, um die grobe Abwesenheitsart
/// (Urlaub, Gleitzeit, ...) zu bestimmen: requestedAccounts[].id -> accounts[].typeId -> accounttypes[].absenceType.</summary>
public sealed class PlandayAbsenceService : IPlandayAbsenceService
{
    private const int PageSize = 100;
    private static readonly TimeSpan AccountCacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _accountTypeCacheLock = new(1, 1);
    private readonly SemaphoreSlim _accountCacheLock = new(1, 1);
    private IReadOnlyList<AccountType>? _cachedAccountTypes;
    private DateTimeOffset _accountTypesCachedAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<long, long?>? _cachedAccountTypeIdByAccountId;
    private DateTimeOffset _accountsCachedAtUtc = DateTimeOffset.MinValue;

    public PlandayAbsenceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<AbsenceRequest>> GetAbsenceRequestsAsync(
        AbsenceRequestQuery query, CancellationToken cancellationToken = default)
    {
        var accountTypesById = (await GetAccountTypesAsync(cancellationToken)).ToDictionary(a => a.Id);
        var accountTypeIdByAccountId = await GetAccountTypeIdByAccountIdAsync(cancellationToken);

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

        return results.Select(dto => MapToAbsenceRequest(dto, accountTypesById, accountTypeIdByAccountId)).ToList();
    }

    public async Task<IReadOnlyList<AccountType>> GetAccountTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedAccountTypes is not null && DateTimeOffset.UtcNow - _accountTypesCachedAtUtc < AccountCacheDuration)
        {
            return _cachedAccountTypes;
        }

        await _accountTypeCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccountTypes is not null && DateTimeOffset.UtcNow - _accountTypesCachedAtUtc < AccountCacheDuration)
            {
                return _cachedAccountTypes;
            }

            var results = await FetchAllPagesAsync<AccountTypeDto>("absence/v1.0/accounttypes", cancellationToken);

            _cachedAccountTypes = results.Select(MapToAccountType).ToList();
            _accountTypesCachedAtUtc = DateTimeOffset.UtcNow;
            return _cachedAccountTypes;
        }
        finally
        {
            _accountTypeCacheLock.Release();
        }
    }

    /// <summary>Lädt alle Konto-Instanzen (nicht die Kontoarten) und liefert eine Zuordnung
    /// Konto-Id -> Kontoart-Id, um requestedAccounts[].id auf eine Abwesenheitsart aufzulösen.</summary>
    private async Task<IReadOnlyDictionary<long, long?>> GetAccountTypeIdByAccountIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedAccountTypeIdByAccountId is not null && DateTimeOffset.UtcNow - _accountsCachedAtUtc < AccountCacheDuration)
        {
            return _cachedAccountTypeIdByAccountId;
        }

        await _accountCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccountTypeIdByAccountId is not null && DateTimeOffset.UtcNow - _accountsCachedAtUtc < AccountCacheDuration)
            {
                return _cachedAccountTypeIdByAccountId;
            }

            var results = await FetchAllPagesAsync<AccountDto>("absence/v1.0/accounts", cancellationToken);

            _cachedAccountTypeIdByAccountId = results.ToDictionary(a => a.Id, a => a.TypeId);
            _accountsCachedAtUtc = DateTimeOffset.UtcNow;
            return _cachedAccountTypeIdByAccountId;
        }
        finally
        {
            _accountCacheLock.Release();
        }
    }

    private async Task<List<T>> FetchAllPagesAsync<T>(string path, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var offset = 0;
        long total;

        do
        {
            var url = new QueryStringBuilder()
                .Add("Limit", PageSize)
                .Add("Offset", offset)
                .Build(path);

            var page = await _httpClient.GetFromJsonAsync<PagedApiDataResponse<T>>(url, cancellationToken)
                ?? throw new InvalidOperationException($"Planday hat keine gültige Antwort für '{path}' geliefert.");

            results.AddRange(page.Data ?? []);
            total = page.Paging?.Total ?? results.Count;
            offset += PageSize;
        } while (offset < total);

        return results;
    }

    private static AccountType MapToAccountType(AccountTypeDto dto) => new(
        dto.Id,
        dto.Name ?? string.Empty,
        ParseAbsenceType(dto.AbsenceType));

    private static AbsenceRequest MapToAbsenceRequest(
        AbsenceRequestDto dto,
        IReadOnlyDictionary<long, AccountType> accountTypesById,
        IReadOnlyDictionary<long, long?> accountTypeIdByAccountId)
    {
        var accounts = (dto.RequestedAccounts ?? [])
            .Select(a => new AbsenceAccountReference(a.Id, a.Name ?? string.Empty, ResolveAbsenceType(a.Id)))
            .ToList();

        return new AbsenceRequest(
            dto.Id,
            dto.EmployeeId ?? 0,
            ParseStatus(dto.Status),
            ParseDate(dto.AbsencePeriod?.Start),
            ParseDate(dto.AbsencePeriod?.End),
            dto.Note,
            accounts);

        AbsenceType ResolveAbsenceType(long accountId) =>
            accountTypeIdByAccountId.TryGetValue(accountId, out var typeId)
                && typeId is { } id
                && accountTypesById.TryGetValue(id, out var accountType)
                ? accountType.AbsenceType
                : AbsenceType.Unknown;
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
