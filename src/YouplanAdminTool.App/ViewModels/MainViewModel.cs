using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouplanAdminTool.App.Display;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Core.Services;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPlandayAbsenceService _absenceService;
    private readonly IPlandayHrService _hrService;
    private readonly IApprovalStateStore _approvalStateStore;
    private readonly INewApprovalDetector _newApprovalDetector;
    private readonly ILogger<MainViewModel> _logger;
    private readonly AppOptions _appOptions;

    private DispatcherTimer? _autoRefreshTimer;
    private IReadOnlyDictionary<long, Employee> _employeesById = new Dictionary<long, Employee>();
    private IReadOnlyDictionary<long, Department> _departmentsById = new Dictionary<long, Department>();
    private IReadOnlyList<AbsenceRequest> _lastFetchedRequests = [];
    private IReadOnlySet<long> _lastNewlyApprovedIds = new HashSet<long>();

    [ObservableProperty]
    private DateTime startDate;

    [ObservableProperty]
    private DateTime endDate;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Bereit.";

    [ObservableProperty]
    private DateTime? lastRefreshedAt;

    [ObservableProperty]
    private AbsenceTypeFilterItem selectedAbsenceTypeFilter;

    [ObservableProperty]
    private int pollingIntervalMinutes;

    public ObservableCollection<AbsenceTypeFilterItem> AbsenceTypeFilterOptions { get; } =
    [
        new(null, "Alle Arten"),
        new(AbsenceType.Vacation, AbsenceDisplayText.ForType(AbsenceType.Vacation)),
        new(AbsenceType.Absence, AbsenceDisplayText.ForType(AbsenceType.Absence)),
        new(AbsenceType.Flextime, AbsenceDisplayText.ForType(AbsenceType.Flextime)),
        new(AbsenceType.Accrued, AbsenceDisplayText.ForType(AbsenceType.Accrued)),
    ];

    public ObservableCollection<AbsenceRowViewModel> AbsenceRows { get; } = [];

    public ObservableCollection<AbsenceRowViewModel> NewlyApproved { get; } = [];

    public MainViewModel(
        IPlandayAbsenceService absenceService,
        IPlandayHrService hrService,
        IApprovalStateStore approvalStateStore,
        INewApprovalDetector newApprovalDetector,
        IOptions<AppOptions> appOptions,
        ILogger<MainViewModel> logger)
    {
        _absenceService = absenceService;
        _hrService = hrService;
        _approvalStateStore = approvalStateStore;
        _newApprovalDetector = newApprovalDetector;
        _appOptions = appOptions.Value;
        _logger = logger;

        startDate = DateTime.Today;
        endDate = DateTime.Today.AddDays(_appOptions.DefaultDateRangeDays);
        selectedAbsenceTypeFilter = AbsenceTypeFilterOptions[0];
        pollingIntervalMinutes = _appOptions.PollingIntervalMinutes;

        StartAutoRefreshTimer();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Aktualisiere...";

        try
        {
            var employees = await _hrService.GetEmployeesAsync();
            _employeesById = employees.ToDictionary(e => e.Id);

            var departments = await _hrService.GetDepartmentsAsync();
            _departmentsById = departments.ToDictionary(d => d.Id);

            var query = new AbsenceRequestQuery(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(EndDate));
            var requests = await _absenceService.GetAbsenceRequestsAsync(query);

            var seenApprovedIds = await _approvalStateStore.GetSeenApprovedIdsAsync();
            var newlyApproved = _newApprovalDetector.DetectNewApprovals(requests, seenApprovedIds);

            var approvedIds = requests
                .Where(r => r.Status == AbsenceRequestStatus.Approved)
                .Select(r => r.Id);
            await _approvalStateStore.MarkAsSeenAsync(approvedIds);

            _lastFetchedRequests = requests;
            _lastNewlyApprovedIds = newlyApproved.Select(r => r.Id).ToHashSet();

            ApplyFilter();

            LastRefreshedAt = DateTime.Now;
            StatusMessage = newlyApproved.Count > 0
                ? $"{requests.Count} Abwesenheiten geladen, davon {newlyApproved.Count} neu genehmigt."
                : $"{requests.Count} Abwesenheiten geladen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren der Ferien-Übersicht");
            StatusMessage = $"Fehler beim Aktualisieren: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedAbsenceTypeFilterChanged(AbsenceTypeFilterItem value) => ApplyFilter();

    partial void OnPollingIntervalMinutesChanged(int value)
    {
        if (_autoRefreshTimer is not null)
        {
            _autoRefreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, value));
        }
    }

    private void ApplyFilter()
    {
        var filterType = SelectedAbsenceTypeFilter.Value;
        var filtered = filterType is null
            ? _lastFetchedRequests
            : _lastFetchedRequests.Where(r => r.PrimaryAbsenceType == filterType).ToList();

        AbsenceRows.Clear();
        foreach (var request in filtered.OrderBy(r => r.StartDate))
        {
            AbsenceRows.Add(ToRowViewModel(request, _lastNewlyApprovedIds.Contains(request.Id)));
        }

        NewlyApproved.Clear();
        foreach (var request in filtered.Where(r => _lastNewlyApprovedIds.Contains(r.Id)).OrderBy(r => r.StartDate))
        {
            NewlyApproved.Add(ToRowViewModel(request, isNew: true));
        }
    }

    private AbsenceRowViewModel ToRowViewModel(AbsenceRequest request, bool isNew)
    {
        _employeesById.TryGetValue(request.EmployeeId, out var employee);
        var departmentName = employee?.DepartmentIds
            .Select(id => _departmentsById.TryGetValue(id, out var department) ? department.Name : null)
            .FirstOrDefault(name => name is not null) ?? "–";

        return new AbsenceRowViewModel
        {
            Id = request.Id,
            EmployeeName = employee?.FullName ?? $"Mitarbeiter #{request.EmployeeId}",
            DepartmentName = departmentName,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            AbsenceTypeDisplay = AbsenceDisplayText.ForType(request.PrimaryAbsenceType),
            StatusDisplay = AbsenceDisplayText.ForStatus(request.Status),
            Note = request.Note,
            IsNew = isNew,
        };
    }

    private void StartAutoRefreshTimer()
    {
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, PollingIntervalMinutes)),
        };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshAsync();
        _autoRefreshTimer.Start();
    }

    public void Dispose()
    {
        _autoRefreshTimer?.Stop();
    }
}
