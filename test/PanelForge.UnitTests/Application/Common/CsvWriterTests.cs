using System.Text;
using FluentAssertions;
using PanelForge.Application.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Common;

public class CsvWriterTests
{
    private static string Render(params object?[] row)
    {
        var bytes = CsvWriter.Write(["A", "B", "C"], [row]);
        return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }

    [Fact]
    public void Write_ShouldQuoteCommasQuotesAndNewlines()
    {
        var csv = Render("a,b", "say \"hi\"", "line1\nline2");

        csv.Should().Contain("\"a,b\",\"say \"\"hi\"\"\",\"line1\nline2\"");
    }

    [Fact]
    public void Write_ShouldNeutralizeFormulaInjection()
    {
        var csv = Render("=HYPERLINK(\"http://evil\")", "+1", "@cmd");

        csv.Split('\n')[1].Should().StartWith("\"'=HYPERLINK");
        csv.Should().Contain(",'+1,'@cmd");
    }

    [Fact]
    public void Write_ShouldFormatDatesAsUtcIso_AndKeepVietnamese()
    {
        var csv = Render(new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc), "Tô màu", null);

        csv.Should().Contain("2026-09-26T08:30:00Z,Tô màu,");
    }
}
