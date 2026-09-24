using AudioParsing;
using AudioParsing.Win.ViewModels;
using Xunit;

namespace AudioParsing.Win.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ProcessDroppedFilesSwapsDropZoneToLogAndAppendsSummary()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            List<AudioFileResult> results = new()
            {
                new AudioFileResult(audio, Path.ChangeExtension(audio, ".md"), Skipped: false, Success: true, Error: null),
            };
            FakePipelineRunner runner = new((files, progress, ct) =>
            {
                progress?.Report(new PipelineProgress(audio, PipelineStage.Transcribing, "started"));
                progress?.Report(new PipelineProgress(audio, PipelineStage.Completed, "done"));
                return Task.FromResult<IReadOnlyList<AudioFileResult>>(results);
            });
            FakeDialogService dialog = new();
            MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);

            Assert.True(viewModel.IsDropZoneVisible);
            await viewModel.ProcessDroppedFilesAsync(new[] { audio });
            await WaitForProgressAsync(() => viewModel.Log.Count >= 3);

            Assert.False(viewModel.IsDropZoneVisible);
            Assert.True(viewModel.IsLogVisible);
            Assert.False(viewModel.IsProcessing);
            Assert.Empty(dialog.Warnings);
            Assert.Equal(new[] { audio }, runner.ReceivedFiles);
            Assert.Contains(viewModel.Log, static e => e.Level == "transcribe");
            Assert.Contains(viewModel.Log, static e => e.Level == "ok");
            Assert.Contains(viewModel.Log, static e => e.Message.Contains("Готово", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessDroppedFilesWarnsWhenNoAudioFound()
    {
        string root = CreateTempDirectory();
        try
        {
            string notes = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(notes, "not audio");
            FakePipelineRunner runner = new((files, progress, ct) =>
                Task.FromResult<IReadOnlyList<AudioFileResult>>(Array.Empty<AudioFileResult>()));
            FakeDialogService dialog = new();
            MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);

            await viewModel.ProcessDroppedFilesAsync(new[] { notes, Path.Combine(root, "missing.mp3") });

            Assert.Single(dialog.Warnings);
            Assert.Equal(0, runner.CallCount);
            Assert.True(viewModel.IsDropZoneVisible);
            Assert.Empty(viewModel.Log);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessDroppedFilesLogsFailureAndContinuesToSummary()
    {
        string root = CreateTempDirectory();
        try
        {
            string good = Path.Combine(root, "good.mp3");
            string bad = Path.Combine(root, "bad.mp3");
            await File.WriteAllBytesAsync(good, new byte[] { 1 });
            await File.WriteAllBytesAsync(bad, new byte[] { 2 });
            List<AudioFileResult> results = new()
            {
                new AudioFileResult(good, Path.ChangeExtension(good, ".md"), Skipped: false, Success: true, Error: null),
                new AudioFileResult(bad, Path.ChangeExtension(bad, ".md"), Skipped: false, Success: false, Error: "boom"),
            };
            FakePipelineRunner runner = new((files, progress, ct) =>
            {
                progress?.Report(new PipelineProgress(good, PipelineStage.Completed, "done"));
                progress?.Report(new PipelineProgress(bad, PipelineStage.Failed, "boom"));
                return Task.FromResult<IReadOnlyList<AudioFileResult>>(results);
            });
            FakeDialogService dialog = new();
            MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);

            await viewModel.ProcessDroppedFilesAsync(new[] { good, bad });
            await WaitForProgressAsync(() => viewModel.Log.Count >= 3);

            Assert.Contains(viewModel.Log, static e => e.Level == "fail");
            Assert.Contains(viewModel.Log, static e => e.Message.Contains("ошибок: 1", StringComparison.Ordinal));
            Assert.False(viewModel.IsProcessing);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessDroppedFilesDeclinesSecondDropWhileProcessing()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1, 2, 3 });
            TaskCompletionSource<IReadOnlyList<AudioFileResult>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            FakePipelineRunner runner = new((files, progress, ct) => gate.Task);
            FakeDialogService dialog = new();
            MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);
            string[] paths = new[] { audio };

            Task first = viewModel.ProcessDroppedFilesAsync(paths);
            await viewModel.ProcessDroppedFilesAsync(paths);

            Assert.Single(dialog.Warnings);
            Assert.Equal(1, runner.CallCount);
            gate.SetResult(Array.Empty<AudioFileResult>());
            await first.ConfigureAwait(true);
            Assert.False(viewModel.IsProcessing);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SelectFilesProcessesPickedFiles()
    {
        string root = CreateTempDirectory();
        try
        {
            string first = Path.Combine(root, "a.mp3");
            string second = Path.Combine(root, "b.mp4");
            await File.WriteAllBytesAsync(first, new byte[] { 1 });
            await File.WriteAllBytesAsync(second, new byte[] { 2 });
            FakePipelineRunner runner = new((files, progress, ct) =>
                Task.FromResult<IReadOnlyList<AudioFileResult>>(Array.Empty<AudioFileResult>()));
            FakeDialogService dialog = new() { OpenAudioFilesDialogResult = new[] { first, second } };
            MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);

            await viewModel.SelectFilesAsync();

            Assert.Equal(1, dialog.OpenAudioFilesDialogCallCount);
            Assert.Equal(new[] { first, second }, runner.ReceivedFiles);
            Assert.False(viewModel.IsDropZoneVisible);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SelectFilesIgnoresCancelledDialog()
    {
        FakePipelineRunner runner = new((files, progress, ct) =>
            Task.FromResult<IReadOnlyList<AudioFileResult>>(Array.Empty<AudioFileResult>()));
        FakeDialogService dialog = new() { OpenAudioFilesDialogResult = null };
        MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);

        await viewModel.SelectFilesAsync();

        Assert.Equal(1, dialog.OpenAudioFilesDialogCallCount);
        Assert.Equal(0, runner.CallCount);
        Assert.True(viewModel.IsDropZoneVisible);
    }

    [Fact]
    public async Task SelectFilesDeclinesWhileProcessing()
    {
        string root = CreateTempDirectory();
        try
        {
            string audio = Path.Combine(root, "a.mp3");
            await File.WriteAllBytesAsync(audio, new byte[] { 1 });
            TaskCompletionSource<IReadOnlyList<AudioFileResult>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            FakePipelineRunner runner = new((files, progress, ct) => gate.Task);
            FakeDialogService dialog = new() { OpenAudioFilesDialogResult = new[] { audio } };
            MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);
            string[] paths = new[] { audio };

            Task first = viewModel.ProcessDroppedFilesAsync(paths);
            await viewModel.SelectFilesAsync();

            Assert.Single(dialog.Warnings);
            Assert.Equal(0, dialog.OpenAudioFilesDialogCallCount);
            Assert.Equal(1, runner.CallCount);
            gate.SetResult(Array.Empty<AudioFileResult>());
            await first.ConfigureAwait(true);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OpenSettingsCommandShowsSettingsDialog()
    {
        FakePipelineRunner runner = new((files, progress, ct) =>
            Task.FromResult<IReadOnlyList<AudioFileResult>>(Array.Empty<AudioFileResult>()));
        FakeDialogService dialog = new();
        MainWindowViewModel viewModel = new(runner, dialog, TestLogger<MainWindowViewModel>.Instance);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal(1, dialog.SettingsShownCount);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audioparsing-winvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WaitForProgressAsync(Func<bool> ready)
    {
        for (int i = 0; i < 200 && !ready(); i++)
        {
            await Task.Delay(10).ConfigureAwait(true);
        }
    }
}
