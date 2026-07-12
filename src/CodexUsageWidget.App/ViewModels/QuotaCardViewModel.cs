using System.Globalization;
using CodexUsageWidget.App.Helpers;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.ViewModels;

/// <summary>Presentation color state for a quota card.</summary>
public enum QuotaCardColorState
{
    Normal,
    Warning,
    Critical,
}

/// <summary>
/// View model for a single five-hour or weekly quota card.
/// </summary>
public sealed class QuotaCardViewModel : ViewModelBase
{
    private QuotaBucketSnapshot? _snapshot;
    private bool _isFresh;
    private DateTimeOffset? _syncedAt;
    private TimeSpan? _countdown;

    public QuotaCardViewModel(QuotaBucket bucket, IDispatcher dispatcher)
        : base(dispatcher)
    {
        Bucket = bucket;
        BucketLabel = bucket == QuotaBucket.FiveHour ? "5小时" : "本周";
    }

    /// <summary>The bucket this card represents.</summary>
    public QuotaBucket Bucket { get; }

    /// <summary>Localized bucket label.</summary>
    public string BucketLabel { get; }

    /// <summary>Raw used percentage from the server.</summary>
    public int UsedPercent => _snapshot?.UsedPercent ?? 0;

    /// <summary>Clamped remaining percentage.</summary>
    public int RemainingPercent => _snapshot?.RemainingPercent ?? 0;

    /// <summary>Remaining percentage formatted for display.</summary>
    public string RemainingPercentText => _snapshot?.IsAvailable == true ? $"{RemainingPercent}%" : "--";

    /// <summary>Progress bar value 0–100.</summary>
    public int ProgressValue => Math.Clamp(RemainingPercent, 0, 100);

    /// <summary>Formatted reset countdown, or empty when unavailable.</summary>
    public string CountdownText => FormatCountdown(_countdown);

    /// <summary>Status text such as 已同步, 已过期, 不可用, or the active rounded-100 label.</summary>
    public string StatusText => BuildStatusText();

    /// <summary>Presentation color state based on remaining thresholds.</summary>
    public QuotaCardColorState ColorState => ResolveColorState();

    /// <summary>Last synchronization time text.</summary>
    public string LastSyncTimeText => FormatLastSync();

    /// <summary>Updates the card from a new monitor snapshot.
    /// </summary>
    public void Update(QuotaBucketSnapshot? snapshot, bool isFresh, DateTimeOffset? syncedAt, TimeSpan? countdown)
    {
        _snapshot = snapshot;
        _isFresh = isFresh;
        _syncedAt = syncedAt;
        _countdown = countdown;

        OnPropertyChanged(nameof(UsedPercent));
        OnPropertyChanged(nameof(RemainingPercent));
        OnPropertyChanged(nameof(RemainingPercentText));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ColorState));
        OnPropertyChanged(nameof(LastSyncTimeText));
    }

    private string BuildStatusText()
    {
        if (_snapshot is null || !_snapshot.IsAvailable)
        {
            return "不可用";
        }

        if (!_isFresh)
        {
            return "已过期";
        }

        if (Bucket == QuotaBucket.FiveHour
            && RemainingPercent == 100
            && _countdown.HasValue
            && _countdown.Value > TimeSpan.Zero)
        {
            return "100%·计时已启动";
        }

        return "已同步";
    }

    private QuotaCardColorState ResolveColorState()
    {
        if (_snapshot is null || !_snapshot.IsAvailable)
        {
            return QuotaCardColorState.Normal;
        }

        int remaining = RemainingPercent;
        if (remaining > 30)
        {
            return QuotaCardColorState.Normal;
        }

        if (remaining >= 11)
        {
            return QuotaCardColorState.Warning;
        }

        return QuotaCardColorState.Critical;
    }

    private static string FormatCountdown(TimeSpan? countdown)
    {
        if (!countdown.HasValue)
        {
            return string.Empty;
        }

        TimeSpan value = countdown.Value;
        if (value <= TimeSpan.Zero)
        {
            return "00:00:00";
        }

        int hours = (int)value.TotalHours;
        int minutes = value.Minutes;
        int seconds = value.Seconds;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    private string FormatLastSync()
    {
        if (_syncedAt is null)
        {
            return "--";
        }

        return _syncedAt.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }
}
