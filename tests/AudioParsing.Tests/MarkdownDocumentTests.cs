using Xunit;

namespace AudioParsing.Tests;

public sealed class MarkdownDocumentTests
{
    [Fact]
    public void SplitSummaryAndKeywordsSeparatesTrailingMarkerLine()
    {
        (string summary, IReadOnlyList<string> keywords) =
            MarkdownDocument.SplitSummaryAndKeywords("Line one\n- point\nKeywords: alpha, beta , gamma");

        Assert.Equal("Line one\n- point", summary);
        Assert.Equal("alpha,beta,gamma", string.Join(",", keywords));
    }

    [Fact]
    public void SplitSummaryAndKeywordsToleratesTrailingBlankLines()
    {
        (string summary, IReadOnlyList<string> keywords) =
            MarkdownDocument.SplitSummaryAndKeywords("Summary text\nKeywords: one\n\n");

        Assert.Equal("Summary text", summary);
        Assert.Equal("one", string.Join(",", keywords));
    }

    [Fact]
    public void SplitSummaryAndKeywordsReturnsEmptyKeywordsWhenMarkerIsMissing()
    {
        (string summary, IReadOnlyList<string> keywords) =
            MarkdownDocument.SplitSummaryAndKeywords("Just a summary");

        Assert.Equal("Just a summary", summary);
        Assert.Empty(keywords);
    }

    [Fact]
    public void BuildCreatesFrontmatterAndBothSections()
    {
        string markdown = MarkdownDocument.Build(
            "Lecture 1",
            new DateOnly(2026, 9, 24),
            new List<string> { "alpha", "beta" },
            "Short summary",
            "Full verbatim transcript");

        Assert.Contains("title: \"Lecture 1\"", markdown, StringComparison.Ordinal);
        Assert.Contains("date: 2026-09-24", markdown, StringComparison.Ordinal);
        Assert.Contains("  - alpha", markdown, StringComparison.Ordinal);
        Assert.Contains("  - beta", markdown, StringComparison.Ordinal);
        Assert.Contains("## Краткое содержание", markdown, StringComparison.Ordinal);
        Assert.Contains("Short summary", markdown, StringComparison.Ordinal);
        Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
        Assert.Contains("Full verbatim transcript", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOmitsKeywordsSectionWhenEmpty()
    {
        string markdown = MarkdownDocument.Build(
            "Lecture 1",
            new DateOnly(2026, 9, 24),
            [],
            "Short summary",
            "Full verbatim transcript");

        Assert.DoesNotContain("keywords:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEscapesQuotesInTitle()
    {
        string markdown = MarkdownDocument.Build(
            "Say \"hi\"",
            new DateOnly(2026, 9, 24),
            [],
            "Short summary",
            "Full verbatim transcript");

        Assert.Contains("title: \"Say \\\"hi\\\"\"", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTranscriptOnlyOmitsSummarySection()
    {
        string markdown = MarkdownDocument.BuildTranscriptOnly(
            "Lecture 1",
            new DateOnly(2026, 9, 24),
            "Full verbatim transcript");

        Assert.Contains("title: \"Lecture 1\"", markdown, StringComparison.Ordinal);
        Assert.Contains("date: 2026-09-24", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Краткое содержание", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("keywords:", markdown, StringComparison.Ordinal);
        Assert.Contains("## Полный транскрипт", markdown, StringComparison.Ordinal);
        Assert.Contains("Full verbatim transcript", markdown, StringComparison.Ordinal);
    }
}
