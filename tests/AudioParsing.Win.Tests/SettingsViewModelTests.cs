using System.Net;
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
        Assert.Equal("External", viewModel.TranscriptionBackend);
        Assert.Equal("ru", viewModel.Language);
        Assert.Equal("models/gigaam-v3", viewModel.GigaAmModelPath);
        Assert.Equal("gigaam_v3_e2e_rnnt_encoder.onnx", viewModel.GigaAmEncoderFileName);
        Assert.Null(viewModel.ValidationMessage);
    }

    [Fact]
    public void LoadNormalizesUnknownBackendToExternal()
    {
        FakeSettingsStore store = new(new AppSettingsModel { TranscriptionBackend = "something-else" });
        SettingsViewModel viewModel = new(store, new FakeDialogService());

        Assert.Equal("External", viewModel.TranscriptionBackend);
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
    public void TrySaveRejectsLocalWithMissingModelDirectory()
    {
        string ffmpeg = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryModel = "model-b";
            viewModel.TranscriptionBackend = "Local";
            viewModel.GigaAmModelPath = Path.Combine(Path.GetTempPath(), $"no-such-model-{Guid.NewGuid():N}");

            bool saved = viewModel.TrySave();

            Assert.False(saved);
            Assert.Equal(0, store.SaveCallCount);
            Assert.Contains("GigaAM", viewModel.ValidationMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(ffmpeg);
        }
    }

    [Fact]
    public void TrySaveRejectsLocalWithMissingModelFiles()
    {
        string ffmpeg = Path.GetTempFileName();
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryModel = "model-b";
            viewModel.TranscriptionBackend = "Local";
            viewModel.GigaAmModelPath = modelDir;

            bool saved = viewModel.TrySave();

            Assert.False(saved);
            Assert.Equal(0, store.SaveCallCount);
            Assert.Contains("GigaAM", viewModel.ValidationMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(ffmpeg);
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void TrySavePersistsLocalSettings()
    {
        string ffmpeg = Path.GetTempFileName();
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        try
        {
            foreach (string file in new[] { "gigaam_v3_e2e_rnnt_encoder.onnx", "gigaam_v3_e2e_rnnt_decoder.onnx", "gigaam_v3_e2e_rnnt_joint.onnx", "gigaam_v3_e2e_rnnt_tokens.txt" })
            {
                File.WriteAllText(Path.Combine(modelDir, file), "dummy");
            }

            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryModel = "model-b";
            viewModel.TranscriptionBackend = "Local";
            viewModel.Language = "ru";
            viewModel.GigaAmModelPath = modelDir;
            bool? closed = null;
            viewModel.CloseRequested += (sender, e) => closed = e.DialogResult;

            bool saved = viewModel.TrySave();

            Assert.True(saved);
            Assert.Equal(1, store.SaveCallCount);
            Assert.Equal("Local", store.Model.TranscriptionBackend);
            Assert.Equal("ru", store.Model.Language);
            Assert.Equal(modelDir, store.Model.GigaAm.ModelPath);
            Assert.Equal("gigaam_v3_e2e_rnnt_encoder.onnx", store.Model.GigaAm.EncoderFileName);
            Assert.Null(viewModel.ValidationMessage);
            Assert.Equal(true, closed);
        }
        finally
        {
            File.Delete(ffmpeg);
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void BrowseGigaAmModelUpdatesPathWhenFolderPicked()
    {
        FakeDialogService dialog = new() { OpenFolderDialogResult = @"D:\models\gigaam" };
        SettingsViewModel viewModel = new(new FakeSettingsStore(new AppSettingsModel()), dialog);

        viewModel.BrowseGigaAmModelCommand.Execute(null);

        Assert.Equal(@"D:\models\gigaam", viewModel.GigaAmModelPath);
    }

    [Fact]
    public void BrowseGigaAmModelKeepsPathWhenDialogCancelled()
    {
        FakeDialogService dialog = new() { OpenFolderDialogResult = null };
        SettingsViewModel viewModel = new(new FakeSettingsStore(new AppSettingsModel()), dialog);
        string before = viewModel.GigaAmModelPath;

        viewModel.BrowseGigaAmModelCommand.Execute(null);

        Assert.Equal(before, viewModel.GigaAmModelPath);
    }

    [Fact]
    public void DownloadDoesNothingWhenNotConfirmed()
    {
        FakeDialogService dialog = new() { ConfirmResult = false };
        SettingsViewModel viewModel = new(
            new FakeSettingsStore(new AppSettingsModel()),
            dialog,
            () => throw new InvalidOperationException("HTTP must not be used."));
        string before = viewModel.GigaAmModelPath;

        viewModel.DownloadGigaAmModelCommand.Execute(null);

        Assert.Equal(before, viewModel.GigaAmModelPath);
        Assert.Equal("gigaam_v3_e2e_rnnt_encoder.onnx", viewModel.GigaAmEncoderFileName);
        Assert.Null(viewModel.DownloadStatus);
        Assert.Null(viewModel.ValidationMessage);
    }

    [Fact]
    public async Task DownloadFillsModelFieldsWhenConfirmed()
    {
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-dl-{Guid.NewGuid():N}");
        try
        {
            FakeDialogService dialog = new() { ConfirmResult = true };
            using ModelStubHandler stub = new();
            using HttpClient httpClient = new(stub);
            SettingsViewModel viewModel = new(
                new FakeSettingsStore(new AppSettingsModel()),
                dialog,
                () => httpClient);
            viewModel.GigaAmModelPath = modelDir;

            viewModel.DownloadGigaAmModelCommand.Execute(null);
            await WaitForDownloadAsync(viewModel);

            Assert.Equal(modelDir, viewModel.GigaAmModelPath);
            Assert.Equal(GigaAmDownloader.EncoderFileName, viewModel.GigaAmEncoderFileName);
            Assert.Equal(GigaAmDownloader.DecoderFileName, viewModel.GigaAmDecoderFileName);
            Assert.Equal(GigaAmDownloader.JoinerFileName, viewModel.GigaAmJoinerFileName);
            Assert.Equal(GigaAmDownloader.TokensFileName, viewModel.GigaAmTokensFileName);
            foreach (GigaAmModelFile file in GigaAmDownloader.RequiredFiles)
            {
                Assert.True(File.Exists(Path.Combine(modelDir, file.FileName)));
            }

            Assert.Contains("Сохранить", viewModel.DownloadStatus, StringComparison.Ordinal);
            Assert.Null(viewModel.ValidationMessage);
        }
        finally
        {
            if (Directory.Exists(modelDir))
            {
                Directory.Delete(modelDir, true);
            }
        }
    }

    [Fact]
    public async Task DownloadReportsErrorWhenServerFails()
    {
        string modelDir = Path.Combine(Path.GetTempPath(), $"gigaam-dl-{Guid.NewGuid():N}");
        try
        {
            FakeDialogService dialog = new() { ConfirmResult = true };
            using FailingHandler stub = new();
            using HttpClient httpClient = new(stub);
            SettingsViewModel viewModel = new(
                new FakeSettingsStore(new AppSettingsModel()),
                dialog,
                () => httpClient);
            viewModel.GigaAmModelPath = modelDir;

            viewModel.DownloadGigaAmModelCommand.Execute(null);
            await WaitForDownloadAsync(viewModel);

            Assert.Null(viewModel.DownloadStatus);
            Assert.Contains("Не удалось скачать модель", viewModel.ValidationMessage, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(modelDir))
            {
                Directory.Delete(modelDir, true);
            }
        }
    }

    private static async Task WaitForDownloadAsync(SettingsViewModel viewModel)
    {
        for (int i = 0; i < 300; i++)
        {
            string? status = viewModel.DownloadStatus;
            string? validation = viewModel.ValidationMessage;
            if ((status is not null && status.Contains("Сохранить", StringComparison.Ordinal))
                || (validation is not null && validation.Contains("Не удалось", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail("Model download did not complete in time.");
    }

    private sealed class ModelStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string fileName = request.RequestUri!.Segments[^1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"content-of-{fileName}"),
            });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":\"not found\"}"),
            });
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

    [Fact]
    public void LoadPopulatesSummaryPropertiesFromStore()
    {
        FakeSettingsStore store = new(new AppSettingsModel
        {
            SummaryBackend = "Local",
            SummaryModel = "model-b",
            GigaChat = new GigaChatSettings
            {
                GgufPath = "m.gguf",
                ContextSize = 8192,
                GpuLayerCount = 0,
            },
        });
        SettingsViewModel viewModel = new(store, new FakeDialogService());

        Assert.Equal("Local", viewModel.SummaryBackend);
        Assert.Equal("m.gguf", viewModel.GigaChatGgufPath);
        Assert.Equal(8192, viewModel.GigaChatContextSize);
        Assert.Equal(0, viewModel.GigaChatGpuLayerCount);
    }

    [Fact]
    public void LoadNormalizesUnknownSummaryBackendToExternal()
    {
        FakeSettingsStore store = new(new AppSettingsModel { SummaryBackend = "something-else" });
        SettingsViewModel viewModel = new(store, new FakeDialogService());

        Assert.Equal("External", viewModel.SummaryBackend);
    }

    [Fact]
    public void LoadPreservesSkipSummaryBackend()
    {
        FakeSettingsStore store = new(new AppSettingsModel { SummaryBackend = "Skip" });
        SettingsViewModel viewModel = new(store, new FakeDialogService());

        Assert.Equal("Skip", viewModel.SummaryBackend);
    }

    [Fact]
    public void TrySavePersistsSkipSummaryWithoutSummaryModel()
    {
        string ffmpeg = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryBackend = "Skip";
            viewModel.SummaryModel = "  ";
            bool? closed = null;
            viewModel.CloseRequested += (sender, e) => closed = e.DialogResult;

            bool saved = viewModel.TrySave();

            Assert.True(saved);
            Assert.Equal(1, store.SaveCallCount);
            Assert.Equal("Skip", store.Model.SummaryBackend);
            Assert.Null(viewModel.ValidationMessage);
            Assert.Equal(true, closed);
        }
        finally
        {
            File.Delete(ffmpeg);
        }
    }

    [Fact]
    public void TrySaveRejectsLocalSummaryWithMissingGguf()
    {
        string ffmpeg = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryBackend = "Local";
            viewModel.GigaChatGgufPath = Path.Combine(Path.GetTempPath(), $"no-such-model-{Guid.NewGuid():N}.gguf");

            bool saved = viewModel.TrySave();

            Assert.False(saved);
            Assert.Equal(0, store.SaveCallCount);
            Assert.Contains("GigaChat", viewModel.ValidationMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(ffmpeg);
        }
    }

    [Fact]
    public void TrySaveRejectsNonPositiveContextSize()
    {
        string ffmpeg = Path.GetTempFileName();
        string gguf = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryBackend = "Local";
            viewModel.GigaChatGgufPath = gguf;
            viewModel.GigaChatContextSize = 0;

            bool saved = viewModel.TrySave();

            Assert.False(saved);
            Assert.Equal(0, store.SaveCallCount);
            Assert.NotNull(viewModel.ValidationMessage);
        }
        finally
        {
            File.Delete(ffmpeg);
            File.Delete(gguf);
        }
    }

    [Fact]
    public void TrySaveRejectsNegativeGpuLayerCount()
    {
        string ffmpeg = Path.GetTempFileName();
        string gguf = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryBackend = "Local";
            viewModel.GigaChatGgufPath = gguf;
            viewModel.GigaChatGpuLayerCount = -1;

            bool saved = viewModel.TrySave();

            Assert.False(saved);
            Assert.Equal(0, store.SaveCallCount);
            Assert.NotNull(viewModel.ValidationMessage);
        }
        finally
        {
            File.Delete(ffmpeg);
            File.Delete(gguf);
        }
    }

    [Fact]
    public void TrySavePersistsLocalSummarySettings()
    {
        string ffmpeg = Path.GetTempFileName();
        string gguf = Path.GetTempFileName();
        try
        {
            FakeSettingsStore store = new(new AppSettingsModel());
            SettingsViewModel viewModel = new(store, new FakeDialogService());
            viewModel.FfmpegPath = ffmpeg;
            viewModel.SummaryBackend = "Local";
            viewModel.GigaChatGgufPath = gguf;
            viewModel.GigaChatContextSize = 8192;
            viewModel.GigaChatGpuLayerCount = 0;
            bool? closed = null;
            viewModel.CloseRequested += (sender, e) => closed = e.DialogResult;

            bool saved = viewModel.TrySave();

            Assert.True(saved);
            Assert.Equal(1, store.SaveCallCount);
            Assert.Equal("Local", store.Model.SummaryBackend);
            Assert.Equal(gguf, store.Model.GigaChat.GgufPath);
            Assert.Equal(8192, store.Model.GigaChat.ContextSize);
            Assert.Equal(0, store.Model.GigaChat.GpuLayerCount);
            Assert.Null(viewModel.ValidationMessage);
            Assert.Equal(true, closed);
        }
        finally
        {
            File.Delete(ffmpeg);
            File.Delete(gguf);
        }
    }

    [Fact]
    public void BrowseGigaChatModelUpdatesPathWhenFilePicked()
    {
        string picked = Path.GetTempFileName();
        try
        {
            FakeDialogService dialog = new() { OpenFileDialogResult = picked };
            SettingsViewModel viewModel = new(new FakeSettingsStore(new AppSettingsModel()), dialog);

            viewModel.BrowseGigaChatModelCommand.Execute(null);

            Assert.Equal(picked, viewModel.GigaChatGgufPath);
        }
        finally
        {
            File.Delete(picked);
        }
    }

    [Fact]
    public void BrowseGigaChatModelKeepsPathWhenDialogCancelled()
    {
        FakeDialogService dialog = new() { OpenFileDialogResult = null };
        SettingsViewModel viewModel = new(new FakeSettingsStore(new AppSettingsModel()), dialog);
        string before = viewModel.GigaChatGgufPath;

        viewModel.BrowseGigaChatModelCommand.Execute(null);

        Assert.Equal(before, viewModel.GigaChatGgufPath);
    }

    [Fact]
    public void DownloadGigaChatDoesNothingWhenNotConfirmed()
    {
        FakeDialogService dialog = new() { ConfirmResult = false };
        SettingsViewModel viewModel = new(
            new FakeSettingsStore(new AppSettingsModel()),
            dialog,
            () => throw new InvalidOperationException("HTTP must not be used."));
        string before = viewModel.GigaChatGgufPath;

        viewModel.DownloadGigaChatModelCommand.Execute(null);

        Assert.Equal(before, viewModel.GigaChatGgufPath);
        Assert.Null(viewModel.SummaryDownloadStatus);
        Assert.Null(viewModel.ValidationMessage);
    }

    [Fact]
    public async Task DownloadGigaChatFillsGgufPathWhenConfirmed()
    {
        string targetDir = Path.Combine(Path.GetTempPath(), $"gigachat-dl-{Guid.NewGuid():N}");
        string targetFile = Path.Combine(targetDir, GigaChatDownloader.GgufFileName);
        try
        {
            FakeDialogService dialog = new() { ConfirmResult = true };
            using GigaChatStubHandler stub = new();
            using HttpClient httpClient = new(stub);
            SettingsViewModel viewModel = new(
                new FakeSettingsStore(new AppSettingsModel()),
                dialog,
                () => httpClient);
            viewModel.GigaChatGgufPath = targetFile;

            viewModel.DownloadGigaChatModelCommand.Execute(null);
            await WaitForGigaChatDownloadAsync(viewModel);

            Assert.Equal(targetFile, viewModel.GigaChatGgufPath);
            Assert.True(File.Exists(targetFile));
            Assert.Contains("Сохранить", viewModel.SummaryDownloadStatus, StringComparison.Ordinal);
            Assert.Null(viewModel.ValidationMessage);
        }
        finally
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [Fact]
    public async Task DownloadGigaChatReportsErrorWhenServerFails()
    {
        string targetFile = Path.Combine(Path.GetTempPath(), $"gigachat-dl-{Guid.NewGuid():N}", GigaChatDownloader.GgufFileName);
        try
        {
            FakeDialogService dialog = new() { ConfirmResult = true };
            using FailingHandler stub = new();
            using HttpClient httpClient = new(stub);
            SettingsViewModel viewModel = new(
                new FakeSettingsStore(new AppSettingsModel()),
                dialog,
                () => httpClient);
            viewModel.GigaChatGgufPath = targetFile;

            viewModel.DownloadGigaChatModelCommand.Execute(null);
            await WaitForGigaChatDownloadAsync(viewModel);

            Assert.Null(viewModel.SummaryDownloadStatus);
            Assert.Contains("Не удалось скачать модель", viewModel.ValidationMessage, StringComparison.Ordinal);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(targetFile);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static async Task WaitForGigaChatDownloadAsync(SettingsViewModel viewModel)
    {
        for (int i = 0; i < 300; i++)
        {
            string? status = viewModel.SummaryDownloadStatus;
            string? validation = viewModel.ValidationMessage;
            if ((status is not null && status.Contains("Сохранить", StringComparison.Ordinal))
                || (validation is not null && validation.Contains("Не удалось", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail("Model download did not complete in time.");
    }

    private sealed class GigaChatStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string fileName = request.RequestUri!.Segments[^1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"content-of-{fileName}"),
            });
        }
    }
}
