using AudioParsing;
using AudioParsing.Win.ViewModels;
using Xunit;

namespace AudioParsing.Win.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void LoadPopulatesPropertiesFromStore()
    {
        FakeSettingsStore store = new(new AppSettingsModel
        {
            FfmpegPath = @"D:\tools\ffmpeg.exe",
            TranscriptionModel = "model-a",
            SummaryModel = "model-b",
        });
        SettingsViewModel viewModel = new(store, new FakeDialogService());

        Assert.Equal(@"D:\tools\ffmpeg.exe", viewModel.FfmpegPath);
        Assert.Equal("model-a", viewModel.TranscriptionModel);
        Assert.Equal("model-b", viewModel.SummaryModel);
        Assert.Null(viewModel.ValidationMessage);
    }

    [Fact]
    public void TrySaveRejectsEmptyTranscriptionModel()
    {
        FakeSettingsStore store = new(new AppSettingsModel());
        SettingsViewModel viewModel = new(store, new FakeDialogService());
        viewModel.TranscriptionModel = "  ";

        bool saved = viewModel.TrySave();

        Assert.False(saved);
        Assert.Equal(0, store.SaveCallCount);
        Assert.NotNull(viewModel.ValidationMessage);
    }

    [Fact]
    public void TrySaveRejectsMissingFfmpeg()
    {
        FakeSettingsStore store = new(new AppSettingsModel());
        FakeDialogService dialog = new();
        SettingsViewModel viewModel = new(store, dialog);
        viewModel.TranscriptionModel = "model-a";
        viewModel.SummaryModel = "model-b";
        viewModel.FfmpegPath = Path.Combine(Path.GetTempPath(), $"no-such-ffmpeg-{Guid.NewGuid():N}.exe");
        bool? closed = null;
        viewModel.CloseRequested += (sender, e) => closed = e.DialogResult;

        bool saved = viewModel.TrySave();

        Assert.False(saved);
        Assert.Equal(0, store.SaveCallCount);
        Assert.NotNull(viewModel.ValidationMessage);
        Assert.Null(closed);
    }

    [Fact]
    public void TrySavePersistsAndRequestsClose()
    {
        string ffmpeg = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.TranscriptionModel = "model-a";
            viewModel.SummaryModel = "model-b";
            bool? closed = null;
            viewModel.CloseRequested += (sender, e) => closed = e.DialogResult;

            bool saved = viewModel.TrySave();

            Assert.True(saved);
            Assert.Equal(1, store.SaveCallCount);
            Assert.Equal(ffmpeg, store.Model.FfmpegPath);
            Assert.Equal("model-a", store.Model.TranscriptionModel);
            Assert.Equal("model-b", store.Model.SummaryModel);
            Assert.Null(viewModel.ValidationMessage);
            Assert.Equal(true, closed);
        }
        finally
        {
            File.Delete(ffmpeg);
        }
    }

    [Fact]
    public void BrowseFfmpegUpdatesPathWhenFilePicked()
    {
        string picked = Path.GetTempFileName();
        try
        {
            FakeDialogService dialog = new() { OpenFileDialogResult = picked };
            SettingsViewModel viewModel = new(new FakeSettingsStore(new AppSettingsModel()), dialog);

            viewModel.BrowseFfmpegCommand.Execute(null);

            Assert.Equal(picked, viewModel.FfmpegPath);
        }
        finally
        {
            File.Delete(picked);
        }
    }

    [Fact]
    public void BrowseFfmpegKeepsPathWhenDialogCancelled()
    {
        FakeDialogService dialog = new() { OpenFileDialogResult = null };
        SettingsViewModel viewModel = new(new FakeSettingsStore(new AppSettingsModel()), dialog);
        string before = viewModel.FfmpegPath;

        viewModel.BrowseFfmpegCommand.Execute(null);

        Assert.Equal(before, viewModel.FfmpegPath);
    }
}
