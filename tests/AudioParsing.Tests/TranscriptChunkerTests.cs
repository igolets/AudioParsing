using Xunit;

namespace AudioParsing.Tests;

public sealed class TranscriptChunkerTests
{
    [Fact]
    public void EstimateTokensReturnsZeroForEmpty() =>
        Assert.Equal(0, TranscriptChunker.EstimateTokens(string.Empty));

    [Theory]
    [InlineData("a", 1)]
    [InlineData("abc", 1)]
    [InlineData("abcd", 2)]
    [InlineData("abcdef", 2)]
    public void EstimateTokensUsesCharsOverThreeHeuristic(string text, int expected) =>
        Assert.Equal(expected, TranscriptChunker.EstimateTokens(text));

    [Fact]
    public void SplitReturnsSingleSegmentWhenItFits()
    {
        const string transcript = "Short lecture.\n\nSecond paragraph.";

        IReadOnlyList<string> segments = TranscriptChunker.Split(transcript, maxTokensPerSegment: 1000);

        Assert.Equal([transcript], segments);
    }

    [Fact]
    public void SplitKeepsEverySegmentWithinBudget()
    {
        string transcript = string.Join("\n\n", Enumerable.Range(0, 20).Select(i => $"Paragraph {i} with some lecture content. It has two sentences. Yes, two."));

        IReadOnlyList<string> segments = TranscriptChunker.Split(transcript, maxTokensPerSegment: 20);

        Assert.True(segments.Count > 1);
        foreach (string segment in segments)
        {
            Assert.True(TranscriptChunker.EstimateTokens(segment) <= 20, $"Segment exceeds budget: '{segment}'");
        }
    }

    [Fact]
    public void SplitIsDeterministic()
    {
        string transcript = string.Concat(Enumerable.Repeat("Лекция о физике. Волны и частицы.\n\n", 30));

        IReadOnlyList<string> first = TranscriptChunker.Split(transcript, maxTokensPerSegment: 25);
        IReadOnlyList<string> second = TranscriptChunker.Split(transcript, maxTokensPerSegment: 25);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SplitSeparatesParagraphsThatDoNotFitTogether()
    {
        string first = new('a', 60);
        string second = new('b', 60);

        IReadOnlyList<string> segments = TranscriptChunker.Split(first + "\n\n" + second, maxTokensPerSegment: 21);

        Assert.Equal(2, segments.Count);
        Assert.Equal(first, segments[0]);
        Assert.Equal(second, segments[1]);
    }

    [Fact]
    public void SplitHardCutsUnbreakableText()
    {
        string transcript = new('x', 100);

        IReadOnlyList<string> segments = TranscriptChunker.Split(transcript, maxTokensPerSegment: 10);

        Assert.True(segments.Count > 1);
        Assert.Equal(transcript, string.Concat(segments));
        foreach (string segment in segments)
        {
            Assert.True(TranscriptChunker.EstimateTokens(segment) <= 10);
        }
    }

    [Fact]
    public void SplitReturnsInputForEmptyTranscript() =>
        Assert.Equal([string.Empty], TranscriptChunker.Split(string.Empty, maxTokensPerSegment: 10));

    [Fact]
    public void SplitThrowsOnNullTranscript() =>
        Assert.Throws<ArgumentNullException>(() => TranscriptChunker.Split(null!, 10));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SplitThrowsOnNonPositiveBudget(int budget) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TranscriptChunker.Split("text", budget));
}
