namespace AudioParsing.LocalStt;

/// <summary>
/// Minimal reader for 16-bit PCM WAV files (mono output).
/// The local pipeline always produces this shape via ffmpeg
/// (<c>-ac 1 -ar 16000 -c:a pcm_s16le</c>), so anything else is rejected
/// with a clear error instead of being silently misrecognized.
/// </summary>
public static class WavReader
{
    /// <summary>Sample rate the GigaAM-v3 sherpa-onnx model expects.</summary>
    public const int ExpectedSampleRate = 16000;

    /// <summary>
    /// Reads <paramref name="path"/> as 16-bit PCM WAV and returns
    /// <c>(sampleRate, monoSamples)</c> with samples normalized to [-1, 1].
    /// Multi-channel input is downmixed to mono by averaging channels.
    /// </summary>
    public static (int SampleRate, float[] Samples) ReadMono(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("WAV file path must not be empty.", nameof(path));
        }

        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);

        ReadFourCc(reader, "RIFF");
        _ = reader.ReadInt32();
        ReadFourCc(reader, "WAVE");

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position < stream.Length)
        {
            string chunkId = ReadChunkId(reader);
            int chunkSize = reader.ReadInt32();
            long chunkEnd = stream.Position + chunkSize;
            if (chunkEnd > stream.Length || chunkSize < 0)
            {
                throw new InvalidOperationException($"WAV file is truncated: {path}");
            }

            if (chunkId == "fmt ")
            {
                int audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
                if (audioFormat != 1)
                {
                    throw new InvalidOperationException(
                        $"Unsupported WAV encoding (format tag {audioFormat}) in: {path}. Expected 16-bit PCM.");
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
                if (data.Length != chunkSize)
                {
                    throw new InvalidOperationException($"WAV file is truncated: {path}");
                }
            }

            stream.Position = chunkEnd + (chunkSize % 2);
        }

        if (data is null)
        {
            throw new InvalidOperationException($"WAV file has no data chunk: {path}");
        }

        if (sampleRate != ExpectedSampleRate)
        {
            throw new InvalidOperationException(
                $"Unsupported WAV sample rate {sampleRate} Hz in: {path}. Expected {ExpectedSampleRate} Hz mono PCM.");
        }

        if (channels <= 0)
        {
            throw new InvalidOperationException($"WAV file has no audio channels: {path}");
        }

        if (bitsPerSample != 16)
        {
            throw new InvalidOperationException(
                $"Unsupported WAV bit depth {bitsPerSample} in: {path}. Expected 16-bit PCM.");
        }

        int frames = data.Length / (channels * 2);
        float[] samples = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            int sum = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                int offset = (frame * channels + channel) * 2;
                sum += BitConverter.ToInt16(data, offset);
            }

            samples[frame] = sum / (float)(channels * 32768);
        }

        return (sampleRate, samples);
    }

    private static void ReadFourCc(BinaryReader reader, string expected)
    {
        string actual = ReadChunkId(reader);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Not a WAV file: expected '{expected}' marker, found '{actual}'.");
        }
    }

    private static string ReadChunkId(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
        {
            throw new InvalidOperationException("WAV file is truncated.");
        }

        return System.Text.Encoding.ASCII.GetString(bytes);
    }
}
