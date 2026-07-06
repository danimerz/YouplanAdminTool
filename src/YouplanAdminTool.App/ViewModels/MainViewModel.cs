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
    private readonly IUserSettingsStore _userSettingsStore;
    private readonly INewApprovalDetector _newApprovalDetector;
    private readonly ILogger<MainViewModel> _logger;
    private readonly AppOptions _appOptions;

    private DispatcherTimer? _autoRefreshTimer;
    private IReadOnlyDictionary<long, Employee> _employeesById = new Dictionary<long, Employee>();
    private IReadOnlyDictionary<long, Department> _departmentsById = new Dictionary<long, Department>();
    private IReadOnlyList<AbsenceRequest> _lastFetchedRequests = [];
    private IReadOnlySet<long> _lastNewlyApprovedIds = new HashSet<long>();
    private long? _pendingDepartmentFilterId;
    private long? _pendingEmployeeFilterId;
    private bool _isInitializing = true;

    /// <summary>Wird nach jeder Aktualisierung ausgelöst, sofern neu genehmigte Anträge gefunden wurden (Anzahl).</summary>
    public event EventHandler<int>? NewApprovalsDetected;

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
    private AbsenceTypeFilterItem? selectedAbsenceTypeFilter;

    [ObservableProperty]
    private DepartmentFilterItem? selectedDepartmentFilter;

    [ObservableProperty]
    private EmployeeFilterItem? selectedEmployeeFilter;

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

    public ObservableCollection<DepartmentFilterItem> DepartmentFilterOptions { get; } =
    [
        new(null, "Alle Abteilungen"),
    ];

    public ObservableCollection<EmployeeFilterItem> EmployeeFilterOptions { get; } =
    [
        new(null, "Alle Mitarbeiter"),
    ];

    public ObservableCollection<AbsenceRowViewModel> AbsenceRows { get; } = [];

    public ObservableCollection<AbsenceRowViewModel> NewlyApproved { get; } = [];

    public MainViewModel(
        IPlandayAbsenceService absenceService,
        IPlandayHrService hrService,
        IApprovalStateStore approvalStateStore,
        IUserSettingsStore userSettingsStore,
        INewApprovalDetector newApprovalDetector,
        IOptions<AppOptions> appOptions,
        ILogger<MainViewModel> logger)
    {
        _absenceService = absenceService;
        _hrService = hrService;
        _approvalStateStore = approvalStateStore;
        _userSettingsStore = userSettingsStore;
        _newApprovalDetector = newApprovalDetector;
        _appOptions = appOptions.Value;
        _logger = logger;

        startDate = DateTime.Today;
        endDate = DateTime.Today.AddDays(_appOptions.DefaultDateRangeDays);
        selectedAbsenceTypeFilter = AbsenceTypeFilterOptions[0];
        selectedDepartmentFilter = DepartmentFilterOptions[0];
        selectedEmployeeFilter = EmployeeFilterOptions[0];
        pollingIntervalMinutes = _appOptions.PollingIntervalMinutes;

        StartAutoRefreshTimer();
    }

    /// <summary>Lädt zuvor gespeicherte Benutzereinstellungen und führt anschließend die erste Aktualisierung aus.
    /// Muss einmalig beim Start der Anwendung aufgerufen werden.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _userSettingsStore.LoadAsync();

            if (settings.PollingIntervalMinutes is { } interval)
            {
                PollingIntervalMinutes = interval;
            }

            if (settings.AbsenceTypeFilter is { } absenceType)
            {
                var match = AbsenceTypeFilterOptions.FirstOrDefault(o => o.Value == absenceType);
                if (match is not null)
                {
                    SelectedAbsenceTypeFilter = match;
                }
            }

            _pendingDepartmentFilterId = settings.DepartmentFilterId;
            _pendingEmployeeFilterId = settings.EmployeeFilterId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gespeicherte Benutzereinstellungen konnten nicht geladen werden.");
        }
        finally
        {
            _isInitializing = false;
        }

        await RefreshAsync();
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
            RebuildEmployeeFilterOptions(employees);

            var departments = await _hrService.GetDepartmentsAsync();
            _departmentsById = departments.ToDictionary(d => d.Id);
            RebuildDepartmentFilterOptions(departments);

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

            if (newlyApproved.Count > 0)
            {
                NewApprovalsDetected?.Invoke(this, newlyApproved.Count);
            }
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

    partial void OnSelectedAbsenceTypeFilterChanged(AbsenceTypeFilterItem? value)
    {
        // WPF setzt SelectedItem beim Zurücksetzen der gebundenen Collection kurzzeitig auf null,
        // bevor die Auswahl explizit neu zugewiesen wird (siehe RebuildDepartmentFilterOptions).
        if (value is null)
        {
            return;
        }

        ApplyFilter();
        TriggerSaveSettings();
    }

    partial void OnSelectedDepartmentFilterChanged(DepartmentFilterItem? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyFilter();
        TriggerSaveSettings();
    }

    partial void OnSelectedEmployeeFilterChanged(EmployeeFilterItem? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyFilter();
        TriggerSaveSettings();
    }

    partial void OnPollingIntervalMinutesChanged(int value)
    {
        if (_autoRefreshTimer is not null)
        {
            _autoRefreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, value));
        }

        TriggerSaveSettings();
    }

    private void TriggerSaveSettings()
    {
        if (_isInitializing)
        {
            return;
        }

        var settings = new UserSettings(
            PollingIntervalMinutes,
            SelectedAbsenceTypeFilter?.Value,
            SelectedDepartmentFilter?.Value,
            SelectedEmployeeFilter?.Value);

        _ = SaveSettingsAsync(settings);
    }

    private async Task SaveSettingsAsync(UserSettings settings)
    {
        try
        {
            await _userSettingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Benutzereinstellungen konnten nicht gespeichert werden.");
        }
    }

    private void RebuildDepartmentFilterOptions(IReadOnlyList<Department> departments)
    {
        var currentSelection = SelectedDepartmentFilter?.Value ?? _pendingDepartmentFilterId;

        DepartmentFilterOptions.Clear();
        DepartmentFilterOptions.Add(new DepartmentFilterItem(null, "Alle Abteilungen"));
        foreach (var department in departments.OrderBy(d => d.Name))
        {
            DepartmentFilterOptions.Add(new DepartmentFilterItem(department.Id, department.Name));
        }

        SelectedDepartmentFilter = DepartmentFilterOptions.FirstOrDefault(d => d.Value == currentSelection)
            ?? DepartmentFilterOptions[0];
        _pendingDepartmentFilterId = null;
    }

    private void RebuildEmployeeFilterOptions(IReadOnlyList<Employee> employees)
    {
        var currentSelection = SelectedEmployeeFilter?.Value ?? _pendingEmployeeFilterId;

        EmployeeFilterOptions.Clear();
        EmployeeFilterOptions.Add(new EmployeeFilterItem(null, "Alle Mitarbeiter"));
        foreach (var employee in employees.OrderBy(e => e.FullName))
        {
            EmployeeFilterOptions.Add(new EmployeeFilterItem(employee.Id, employee.FullName));
        }

        SelectedEmployeeFilter = EmployeeFilterOptions.FirstOrDefault(e => e.Value == currentSelection)
            ?? EmployeeFilterOptions[0];
        _pendingEmployeeFilterId = null;
    }

    private void ApplyFilter()
    {
        var filterType = SelectedAbsenceTypeFilter?.Value;
        var filterDepartmentId = SelectedDepartmentFilter?.Value;
        var filterEmployeeId = SelectedEmployeeFilter?.Value;

        var filtered = _lastFetchedRequests
            .Where(r => filterType is null || r.PrimaryAbsenceType == filterType)
            .Where(r => filterDepartmentId is null || IsInDepartment(r.EmployeeId, filterDepartmentId.Value))
            .Where(r => filterEmployeeId is null || r.EmployeeId == filterEmployeeId.Value)
            .ToList();

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

    private bool IsInDepartment(long employeeId, long departmentId) =>
        _employeesById.TryGetValue(employeeId, out var employee) && employee.DepartmentIds.Contains(departmentId);

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
            AbsenceTypeDisplay = request.Accounts.Count > 0 && !string.IsNullOrWhiteSpace(request.Accounts[0].Name)
                ? request.Accounts[0].Name
                : AbsenceDisplayText.ForType(request.PrimaryAbsenceType),
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
