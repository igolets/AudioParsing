using Xunit;

namespace AudioParsing.Tests;

public sealed class ChunkingSummaryGeneratorTests
{
    [Fact]
    public async Task SinglePassWhenTranscriptFits()
    {
        FakeSummaryGenerator inner = new(t => $"Summary of {t.Length} chars\nKeywords: kw");
        ChunkingSummaryGenerator generator = new(inner, contextSize: 16384, reservedOutputTokens: 2048);

        string result = await generator.GenerateSummaryAsync("Short lecture transcript.");

        Assert.Equal("Summary of 25 chars\nKeywords: kw", result);
        Assert.Single(inner.Received);
        Assert.Equal("Short lecture transcript.", inner.Received[0]);
    }

    [Fact]
    public void NameComesFromInnerGenerator()
    {
        FakeSummaryGenerator inner = new(_ => "x\nKeywords: y");
        ChunkingSummaryGenerator generator = new(inner, contextSize: 16384, reservedOutputTokens: 2048);

        Assert.Equal(inner.Name, generator.Name);
    }

    [Fact]
    public async Task MapReduceWhenTranscriptExceedsBudget()
    {
        FakeSummaryGenerator inner = new(t => $"Partial ({t.Length})\nKeywords: kw");
        int contextSize = 500;
        int reserved = 50;
        int budget = contextSize - reserved - TranscriptChunker.EstimateTokens(RouterAiClient.SummarySystemPrompt);
        ChunkingSummaryGenerator generator = new(inner, contextSize, reserved);
        string transcript = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => $"Paragraph {i} about physics. Waves and particles. " + new string('z', 200)));

        string result = await generator.GenerateSummaryAsync(transcript);

        Assert.True(inner.Received.Count > 2, $"Expected chunk calls plus a reduce call, got {inner.Received.Count}.");
        IReadOnlyList<string> chunks = inner.Received.Take(inner.Received.Count - 1).ToList();
        foreach (string chunk in chunks)
        {
            Assert.True(TranscriptChunker.EstimateTokens(chunk) <= budget, $"Chunk exceeds budget: {chunk.Length} chars.");
        }

        string expectedJoin = string.Join("\n\n", chunks.Select(c => $"Partial ({c.Length})\nKeywords: kw"));
        Assert.Equal(expectedJoin, inner.Received[^1]);
        Assert.EndsWith("Keywords: kw", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorThrowsOnNullInner() =>
        Assert.Throws<ArgumentNullException>(() => new ChunkingSummaryGenerator(null!, 1000, 100));

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ConstructorThrowsOnNonPositiveContextSize(int contextSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingSummaryGenerator(new FakeSummaryGenerator(_ => "x"), contextSize, 10));

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(1001)]
    public void ConstructorThrowsOnInvalidReservedBudget(int reserved) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingSummaryGenerator(new FakeSummaryGenerator(_ => "x"), 1000, reserved));

    private sealed class FakeSummaryGenerator : ISummaryGenerator
    {
        private readonly Func<string, string> _responder;

        public FakeSummaryGenerator(Func<string, string> responder)
        {
            _responder = responder;
        }

        public string Name => "fake-summary";

        public List<string> Received { get; } = new();

        public Task<string> GenerateSummaryAsync(string transcript, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transcript);

            Received.Add(transcript);
            return Task.FromResult(_responder(transcript));
        }
    }
}
