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
    private readonly IAbsenceProcessingStore _processingStore;
    private readonly IUserSettingsStore _userSettingsStore;
    private readonly IStatusChangeDetector _statusChangeDetector;
    private readonly ILogger<MainViewModel> _logger;
    private readonly AppOptions _appOptions;

    private DispatcherTimer? _autoRefreshTimer;
    private IReadOnlyDictionary<long, Employee> _employeesById = new Dictionary<long, Employee>();
    private IReadOnlyDictionary<long, Department> _departmentsById = new Dictionary<long, Department>();
    private IReadOnlyList<AbsenceRequest> _lastFetchedRequests = [];
    private IReadOnlyList<PersistedActionItem> _lastOpenItems = [];
    private HashSet<long> _openActionRequestIds = [];
    private long? _pendingDepartmentFilterId;
    private long? _pendingEmployeeFilterId;
    private bool _isInitializing = true;

    /// <summary>Wird nach jeder Aktualisierung ausgelöst, sofern neue SAP-Aktionsposten gefunden wurden (Anzahl).</summary>
    public event EventHandler<int>? ActionItemsDetected;

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

    /// <summary>Noch offene SAP-Aktionsposten, gefiltert nach Abteilung/Mitarbeiter (aber nicht nach
    /// Zeitraum/Art - das ist eine Aufgabenliste, kein Zeitraum-Browser).</summary>
    public ObservableCollection<ActionItemRowViewModel> OpenActionItems { get; } = [];

    public MainViewModel(
        IPlandayAbsenceService absenceService,
        IPlandayHrService hrService,
        IAbsenceProcessingStore processingStore,
        IUserSettingsStore userSettingsStore,
        IStatusChangeDetector statusChangeDetector,
        IOptions<AppOptions> appOptions,
        ILogger<MainViewModel> logger)
    {
        _absenceService = absenceService;
        _hrService = hrService;
        _processingStore = processingStore;
        _userSettingsStore = userSettingsStore;
        _statusChangeDetector = statusChangeDetector;
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
            var query = new AbsenceRequestQuery(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(EndDate));

            var employeesTask = _hrService.GetEmployeesAsync();
            var departmentsTask = _hrService.GetDepartmentsAsync();
            var requestsTask = _absenceService.GetAbsenceRequestsAsync(query);
            await Task.WhenAll(employeesTask, departmentsTask, requestsTask);

            var employees = employeesTask.Result;
            _employeesById = employees.ToDictionary(e => e.Id);
            RebuildEmployeeFilterOptions(employees);

            var departments = departmentsTask.Result;
            _departmentsById = departments.ToDictionary(d => d.Id);
            RebuildDepartmentFilterOptions(departments);

            var requests = requestsTask.Result;
            _lastFetchedRequests = requests;

            // Reihenfolge entscheidend: erst die ALTEN Stati lesen, bevor ApplyRefreshAsync sie überschreibt.
            var previousStatuses = await _processingStore.GetLastKnownStatusesAsync();

            // Ein komplett leerer Store (z.B. beim allerersten Anbinden an eine neue zentrale SQL-
            // Datenbank) bedeutet nicht "alles ist neu" - sonst würde jeder aktuell genehmigte Antrag
            // fälschlich als offener Posten gemeldet. Stattdessen wird der aktuelle Stand als Basis
            // übernommen, ohne Aktionen auszulösen.
            var newActionItems = previousStatuses.Count > 0
                ? _statusChangeDetector.DetectActionsNeeded(requests, previousStatuses)
                : [];
            await _processingStore.ApplyRefreshAsync(requests, newActionItems);

            var openItems = await _processingStore.GetOpenItemsAsync();
            _openActionRequestIds = openItems.Select(i => i.AbsenceRequestId).ToHashSet();
            _lastOpenItems = openItems;

            ApplyFilter();

            LastRefreshedAt = DateTime.Now;
            StatusMessage = OpenActionItems.Count > 0
                ? $"{requests.Count} Abwesenheiten geladen, {OpenActionItems.Count} offene Posten für SAP."
                : $"{requests.Count} Abwesenheiten geladen.";

            if (newActionItems.Count > 0)
            {
                ActionItemsDetected?.Invoke(this, newActionItems.Count);
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

    [RelayCommand]
    private async Task MarkCompletedAsync(long absenceRequestId)
    {
        try
        {
            await _processingStore.SetCompletedAsync(absenceRequestId, true);

            _openActionRequestIds.Remove(absenceRequestId);
            _lastOpenItems = _lastOpenItems.Where(i => i.AbsenceRequestId != absenceRequestId).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Markieren als erledigt");
            StatusMessage = $"Fehler beim Markieren als erledigt: {ex.Message}";
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
            AbsenceRows.Add(ToRowViewModel(request, _openActionRequestIds.Contains(request.Id)));
        }

        var filteredOpenItems = _lastOpenItems
            .Where(i => filterDepartmentId is null || IsInDepartment(i.EmployeeId, filterDepartmentId.Value))
            .Where(i => filterEmployeeId is null || i.EmployeeId == filterEmployeeId.Value)
            .OrderBy(i => i.StartDate)
            .ToList();

        OpenActionItems.Clear();
        foreach (var item in filteredOpenItems)
        {
            OpenActionItems.Add(ToActionItemRow(item));
        }
    }

    private bool IsInDepartment(long employeeId, long departmentId) =>
        _employeesById.TryGetValue(employeeId, out var employee) && employee.DepartmentIds.Contains(departmentId);

    /// <summary>Baut die Detailansicht für einen Mitarbeiter (alle aktuell geladenen Abwesenheiten),
    /// z.B. für einen Doppelklick auf eine Zeile der Ferien-Übersicht. Liefert null, wenn der
    /// Mitarbeiter nicht (mehr) in den zuletzt geladenen Stammdaten enthalten ist.</summary>
    public EmployeeDetailViewModel? GetEmployeeDetail(long employeeId)
    {
        if (!_employeesById.TryGetValue(employeeId, out var employee))
        {
            return null;
        }

        var absences = _lastFetchedRequests
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate)
            .Select(r => ToRowViewModel(r, _openActionRequestIds.Contains(r.Id)))
            .ToList();

        return new EmployeeDetailViewModel
        {
            EmployeeName = employee.FullName,
            DepartmentName = GetDepartmentName(employee),
            Absences = absences,
        };
    }

    private string GetDepartmentName(Employee? employee) =>
        employee?.DepartmentIds
            .Select(id => _departmentsById.TryGetValue(id, out var department) ? department.Name : null)
            .FirstOrDefault(name => name is not null) ?? "–";

    private AbsenceRowViewModel ToRowViewModel(AbsenceRequest request, bool hasOpenAction)
    {
        _employeesById.TryGetValue(request.EmployeeId, out var employee);

        return new AbsenceRowViewModel
        {
            Id = request.Id,
            EmployeeId = request.EmployeeId,
            EmployeeName = employee?.FullName ?? $"Mitarbeiter #{request.EmployeeId}",
            DepartmentName = GetDepartmentName(employee),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            AbsenceTypeDisplay = request.Accounts.Count > 0 && !string.IsNullOrWhiteSpace(request.Accounts[0].Name)
                ? request.Accounts[0].Name
                : AbsenceDisplayText.ForType(request.PrimaryAbsenceType),
            StatusDisplay = AbsenceDisplayText.ForStatus(request.Status),
            Note = request.Note,
            HasOpenAction = hasOpenAction,
        };
    }

    private ActionItemRowViewModel ToActionItemRow(PersistedActionItem item)
    {
        _employeesById.TryGetValue(item.EmployeeId, out var employee);

        return new ActionItemRowViewModel
        {
            Id = item.AbsenceRequestId,
            EmployeeId = item.EmployeeId,
            EmployeeName = employee?.FullName ?? $"Mitarbeiter #{item.EmployeeId}",
            DepartmentName = GetDepartmentName(employee),
            StartDate = item.StartDate,
            EndDate = item.EndDate,
            AbsenceTypeDisplay = string.IsNullOrWhiteSpace(item.AccountName) ? "Unbekannt" : item.AccountName,
            ActionDisplay = AbsenceDisplayText.ForAction(item.Action),
            Note = item.Note,
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
