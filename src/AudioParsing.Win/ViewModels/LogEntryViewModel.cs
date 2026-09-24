using System.IO;

namespace AudioParsing.Win.ViewModels;

/// <summary>
/// A single user-facing log line rendered in the main window.
/// </summary>
public sealed class LogEntryViewModel
{
    public LogEntryViewModel(DateTime timestamp, string level, string message)
    {
        Timestamp = timestamp;
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public DateTime Timestamp { get; }

    public string Level { get; }

    public string Message { get; }

    public string Display => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";

    /// <summary>
    /// Maps a pipeline progress event to a log line.
    /// </summary>
    public static LogEntryViewModel From(PipelineProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        string file = Path.GetFileName(progress.AudioPath);
        string prefix = progress.Stage switch
        {
            PipelineStage.Reading => "read",
            PipelineStage.Compressing => "compress",
            PipelineStage.Transcribing => "transcribe",
            PipelineStage.Summarizing => "summarize",
            PipelineStage.Writing => "write",
            PipelineStage.Completed => "ok",
            PipelineStage.Skipped => "skip",
            PipelineStage.Failed => "fail",
            _ => "info",
        };
        string message = string.IsNullOrWhiteSpace(progress.Message) ? file : $"{file}: {progress.Message}";
        return new LogEntryViewModel(DateTime.Now, prefix, message);
    }

    public static LogEntryViewModel Error(string message) => new(DateTime.Now, "fail", message);

    public static LogEntryViewModel Info(string message) => new(DateTime.Now, "info", message);
}
