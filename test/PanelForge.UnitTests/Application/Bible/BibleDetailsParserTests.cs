using FluentAssertions;
using PanelForge.Application.Features.Bible.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class BibleDetailsParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Parse_EmptyOrWhitespace_ReturnsEmptyDetails(string? input)
    {
        var details = BibleDetailsParser.Parse(input);
        details.Age.Should().BeNull();
        details.ArtistNotes.Should().BeNull();
        details.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_NewFormat_ExtractsDirectFields()
    {
        var json = """
        {
            "age": "17 tuổi",
            "artistNotes": "Nhân vật thiếu niên, tóc đen ngắn, ánh mắt cương nghị."
        }
        """;

        var details = BibleDetailsParser.Parse(json);
        details.Age.Should().Be("17 tuổi");
        details.ArtistNotes.Should().Be("Nhân vật thiếu niên, tóc đen ngắn, ánh mắt cương nghị.");
    }

    [Fact]
    public void Parse_Legacy4Groups_ExtractsAgeAndCombinesIntoArtistNotes()
    {
        var json = """
        {
            "body": {
                "age": "18 tuổi",
                "height": "1m78",
                "build": "Gầy gò"
            },
            "face": {
                "hair": "Tóc bạch kim",
                "eyes": "Mắt đỏ",
                "marks": "Vết sẹo chéo qua mắt phải"
            },
            "costume": {
                "outfit": "Áo choàng rách",
                "accessories": "Dây chuyền khắc gia huy"
            },
            "art": {
                "inkTone": "Đổ bóng tương phản mạnh",
                "expressions": "Lạnh lùng, hiếm khi cười",
                "forbidden": "Tuyệt đối không vẽ mắt màu xanh"
            }
        }
        """;

        var details = BibleDetailsParser.Parse(json);
        details.Age.Should().Be("18 tuổi");
        details.ArtistNotes.Should().NotBeNullOrWhiteSpace();
        details.ArtistNotes.Should().Contain("- Chiều cao: 1m78");
        details.ArtistNotes.Should().Contain("- Vóc dáng / Thể chất: Gầy gò");
        details.ArtistNotes.Should().Contain("- Kiểu tóc / Màu tóc: Tóc bạch kim");
        details.ArtistNotes.Should().Contain("- Lưu ý cấm kỵ / Tránh vẽ sai: Tuyệt đối không vẽ mắt màu xanh");
    }

    [Fact]
    public void Parse_LegacyAttributesFormat_ExtractsAgeAndBullets()
    {
        var json = """
        {
            "turnaroundGallery": ["https://cdn.example.com/sheet.png"],
            "attributes": {
                "age": "25-28",
                "height": "1m85",
                "forbidden": "Không vẽ trang sức vàng"
            }
        }
        """;

        var details = BibleDetailsParser.Parse(json);
        details.Age.Should().Be("25-28");
        details.ArtistNotes.Should().Contain("- Chiều cao: 1m85");
        details.ArtistNotes.Should().Contain("- Lưu ý cấm kỵ / Tránh vẽ sai: Không vẽ trang sức vàng");
        details.ArtistNotes.Should().NotContain("turnaroundGallery");
    }

    [Fact]
    public void Parse_UnifiedBriefInAttributes_DoesNotExposeTechnicalFieldOrDuplicateText()
    {
        var json = """
        {
            "artistNotes": "Tự tin, tóc xanh buộc gọn.",
            "attributes": {
                "artistNotes": "Tự tin, tóc xanh buộc gọn.",
                "height": "1m68"
            }
        }
        """;

        var details = BibleDetailsParser.Parse(json);

        details.ArtistNotes.Should().Be(
            "Tự tin, tóc xanh buộc gọn.\n\n- Chiều cao: 1m68");
        details.ArtistNotes.Should().NotContain("artistNotes:");
        details.ArtistNotes!.Split(
            "Tự tin, tóc xanh buộc gọn.",
            StringSplitOptions.None).Should().HaveCount(2);
    }

    [Fact]
    public void Parse_IgnoresDeliverablesFields()
    {
        var json = """
        {
            "attributes": {
                "age": "16",
                "charTurnaround": "turnaround_image",
                "charExpressions": "expression_sheet",
                "appearance": "Áo sơ mi trắng"
            }
        }
        """;

        var details = BibleDetailsParser.Parse(json);
        details.Age.Should().Be("16");
        details.ArtistNotes.Should().Contain("- Đặc điểm ngoại hình: Áo sơ mi trắng");
        details.ArtistNotes.Should().NotContain("charTurnaround");
        details.ArtistNotes.Should().NotContain("charExpressions");
    }

    [Fact]
    public void Serialize_ProducesCleanJson()
    {
        var json = BibleDetailsParser.Serialize("20 tuổi", "Ghi chú mẫu");
        json.Should().Contain("\"age\":\"20 tuổi\"");
        json.Should().Contain("\"artistNotes\":\"Ghi chú mẫu\"");
    }

    [Fact]
    public void NormalizeJson_ConvertsLegacyToCleanFormat()
    {
        var legacy = """
        {
            "body": { "age": "15" },
            "face": { "hair": "Xanh lam" }
        }
        """;

        var normalized = BibleDetailsParser.NormalizeJson(legacy);
        normalized.Should().Contain("\"age\":\"15\"");
        normalized.Should().Contain("Kiểu tóc / Màu tóc: Xanh lam");
    }
}
