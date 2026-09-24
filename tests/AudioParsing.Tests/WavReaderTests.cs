using AudioParsing.LocalStt;
using Xunit;

namespace AudioParsing.Tests;

public sealed class WavReaderTests
{
    [Fact]
    public void ReadMonoParses16kHzMonoPcm()
    {
        string path = Path.GetTempFileName();
        try
        {
            WriteWav(path, channels: 1, sampleRate: 16000, samples: new short[] { 0, 16384, -16384 });

            (int sampleRate, float[] samples) = WavReader.ReadMono(path);

            Assert.Equal(16000, sampleRate);
            Assert.Equal(3, samples.Length);
            Assert.Equal(0f, samples[0]);
            Assert.Equal(0.5f, samples[1], precision: 5);
            Assert.Equal(-0.5f, samples[2], precision: 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadMonoDownmixesStereoByAveraging()
    {
        string path = Path.GetTempFileName();
        try
        {
            // Interleaved stereo frames: (L,R) = (32767, 0), (0, 0).
            WriteWav(path, channels: 2, sampleRate: 16000, samples: new short[] { 32767, 0, 0, 0 });

            (int sampleRate, float[] samples) = WavReader.ReadMono(path);

            Assert.Equal(16000, sampleRate);
            Assert.Equal(2, samples.Length);
            Assert.Equal(32767f / (2 * 32768f), samples[0], precision: 5);
            Assert.Equal(0f, samples[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadMonoRejectsOtherSampleRates()
    {
        string path = Path.GetTempFileName();
        try
        {
            WriteWav(path, channels: 1, sampleRate: 44100, samples: new short[] { 0 });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WavReader.ReadMono(path));

            Assert.Contains("44100", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadMonoRejectsMissingFile()
    {
        Assert.Throws<FileNotFoundException>(
            () => WavReader.ReadMono(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.wav")));
    }

    private static void WriteWav(string path, int channels, int sampleRate, short[] samples)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }
    }
}
