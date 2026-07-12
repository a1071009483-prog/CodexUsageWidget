using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CodexUsageWidget.App.Helpers;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.ViewModels;

/// <summary>
/// A single redacted audit row presented in the audit view.
/// </summary>
public sealed class AuditRowViewModel : ViewModelBase
{
    public AuditRowViewModel(IDispatcher dispatcher)
        : base(dispatcher)
    {
    }

    public string AttemptId { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public int PreUsedPercent { get; init; }
    public int PostUsedPercent { get; init; }
    public string? Outcome { get; init; }
    public string? ErrorCategory { get; init; }
    public string RecordedAt { get; init; } = string.Empty;
}

/// <summary>
/// View model for the local redacted audit log window.
/// </summary>
public sealed class AuditViewModel : ViewModelBase
{
    private readonly IAuditStore _auditStore;
    private bool _isLoading;

    public AuditViewModel(IAuditStore auditStore, IDispatcher dispatcher)
        : base(dispatcher)
    {
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
    }

    /// <summary>Redacted audit rows for display.</summary>
    public ObservableCollection<AuditRowViewModel> Rows { get; } = new();

    /// <summary>Whether audit rows are currently loading.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Reloads the audit rows from the store.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Loads audit rows asynchronously.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            List<AuditRowViewModel> rows = new();
            await foreach (AuditEntry entry in _auditStore.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(Map(entry));
            }

            Dispatcher.Invoke(() =>
            {
                Rows.Clear();
                foreach (AuditRowViewModel row in rows)
                {
                    Rows.Add(row);
                }
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private AuditRowViewModel Map(AuditEntry entry)
    {
        string recordedAt = string.IsNullOrEmpty(entry.RecordedAt)
            ? string.Empty
            : FormatTimestamp(entry.RecordedAt);

        return new AuditRowViewModel(Dispatcher)
        {
            AttemptId = entry.AttemptId ?? entry.AuditId,
            ModelId = entry.ModelId,
            PreUsedPercent = entry.PreQuota?.UsedPercent ?? 0,
            PostUsedPercent = entry.PostQuota?.UsedPercent ?? 0,
            Outcome = entry.Outcome,
            ErrorCategory = entry.ErrorCategory,
            RecordedAt = recordedAt,
        };
    }

    private static string FormatTimestamp(string isoTimestamp)
    {
        if (DateTimeOffset.TryParse(
                isoTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset value))
        {
            return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }

        return isoTimestamp;
    }
}
