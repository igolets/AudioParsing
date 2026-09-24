using AudioParsing.LocalStt;
using Xunit;

namespace AudioParsing.Tests;

public sealed class GigaAmTranscriberTests
{
    [Fact]
    public void ConstructorThrowsWhenModelDirectoryIsMissing()
    {
        GigaAmSettings settings = new()
        {
            ModelPath = Path.Combine(Path.GetTempPath(), $"no-such-model-{Guid.NewGuid():N}"),
        };

        Assert.Throws<InvalidOperationException>(() => new GigaAmTranscriber(settings));
    }

    [Fact]
    public void ConstructorThrowsWhenModelFilesAreMissing()
    {
        string modelDir = CreateTempDirectory();
        try
        {
            GigaAmSettings settings = new() { ModelPath = modelDir };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new GigaAmTranscriber(settings));

            Assert.Contains("gigaam_v3_e2e_rnnt_encoder.onnx", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void BuildRecognizerConfigMapsTransducerFileSet()
    {
        string modelDir = CreateTempDirectory();
        try
        {
            GigaAmSettings settings = new()
            {
                ModelPath = modelDir,
                EncoderFileName = "encoder.onnx",
                DecoderFileName = "decoder.onnx",
                JoinerFileName = "joiner.onnx",
                TokensFileName = "tokens.txt",
            };
            foreach (string file in new[]
            {
                settings.EncoderFileName,
                settings.DecoderFileName,
                settings.JoinerFileName,
                settings.TokensFileName,
            })
            {
                File.WriteAllText(Path.Combine(modelDir, file), "dummy");
            }

            SherpaOnnx.OfflineRecognizerConfig config = GigaAmTranscriber.BuildRecognizerConfig(settings);

            Assert.Equal(Path.Combine(modelDir, "encoder.onnx"), config.ModelConfig.Transducer.Encoder);
            Assert.Equal(Path.Combine(modelDir, "decoder.onnx"), config.ModelConfig.Transducer.Decoder);
            Assert.Equal(Path.Combine(modelDir, "joiner.onnx"), config.ModelConfig.Transducer.Joiner);
            Assert.Equal(Path.Combine(modelDir, "tokens.txt"), config.ModelConfig.Tokens);
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void TranscriberExposesLocalNameWithoutLoadingNativeModel()
    {
        string modelDir = CreateTempDirectory();
        try
        {
            GigaAmSettings settings = new() { ModelPath = modelDir };
            foreach (string file in new[] { "gigaam_v3_e2e_rnnt_encoder.onnx", "gigaam_v3_e2e_rnnt_decoder.onnx", "gigaam_v3_e2e_rnnt_joint.onnx", "gigaam_v3_e2e_rnnt_tokens.txt" })
            {
                File.WriteAllText(Path.Combine(modelDir, file), "dummy");
            }

            using GigaAmTranscriber transcriber = new(settings);

            Assert.Equal("GigaAM-v3 (local)", transcriber.Name);
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public async Task TranscribeAsyncRejectsMissingAudioFile()
    {
        string modelDir = CreateTempDirectory();
        try
        {
            GigaAmSettings settings = new() { ModelPath = modelDir };
            foreach (string file in new[] { "gigaam_v3_e2e_rnnt_encoder.onnx", "gigaam_v3_e2e_rnnt_decoder.onnx", "gigaam_v3_e2e_rnnt_joint.onnx", "gigaam_v3_e2e_rnnt_tokens.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(modelDir, file), "dummy");
            }

            using GigaAmTranscriber transcriber = new(settings);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => transcriber.TranscribeAsync(
                    Path.Combine(modelDir, "no-such.wav"), "ru"));
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audioparsing-gigaam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
