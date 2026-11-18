using System.Text;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Documents.Csv;

namespace Qonaqet.IntegrationTests.Documents;

public sealed class CsvServiceTests
{
    private static readonly CsvService Csv = new();

    [Fact]
    public void Reads_header_and_rows_with_crlf()
    {
        var doc = CsvService.Parse("name,email\r\nLeyla,leyla@a.az\r\nOrxan,orxan@a.az\r\n", CsvReadOptions.Default);

        Assert.True(doc.IsValid);
        Assert.Equal(["name", "email"], doc.Header);
        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("orxan@a.az", doc.Get(doc.Rows[1], "EMAIL"));
        Assert.Equal(3, doc.Rows[1].LineNumber);
    }

    [Fact]
    public void Handles_quotes_escapes_and_embedded_delimiters()
    {
        var doc = CsvService.Parse("name,note\n\"Əliyev, Ruslan\",\"Dedi: \"\"Salam\"\"\"\n\"\",x\n", CsvReadOptions.Default);

        Assert.True(doc.IsValid);
        Assert.Equal(["Əliyev, Ruslan", "Dedi: \"Salam\""], doc.Rows[0].Fields);
        Assert.Equal(["", "x"], doc.Rows[1].Fields);
    }

    [Fact]
    public void Multiline_cell_keeps_newlines_and_line_numbers_stay_physical()
    {
        var doc = CsvService.Parse("name,message\r\nLeyla,\"Birinci sətir\r\nikinci sətir\"\r\nOrxan,ok\r\n", CsvReadOptions.Default);

        Assert.True(doc.IsValid);
        Assert.Equal("Birinci sətir\r\nikinci sətir", doc.Rows[0].Fields[1]);
        Assert.Equal(2, doc.Rows[0].LineNumber);
        Assert.Equal(4, doc.Rows[1].LineNumber);
    }

    [Fact]
    public async Task Utf8_bom_is_stripped_and_azerbaijani_letters_survive()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("ad,şəhər\nGülşən Çələbiyeva,Gəncə\nİlqar Ağayev,Şəki\n")).ToArray();
        await using var stream = new MemoryStream(bytes);

        var doc = await Csv.ReadAsync(stream, CsvReadOptions.Default, TestContext.Current.CancellationToken);

        Assert.True(doc.IsValid);
        Assert.Equal("ad", doc.Header[0]);
        Assert.Equal(["Gülşən Çələbiyeva", "Gəncə"], doc.Rows[0].Fields);
        Assert.Equal("Şəki", doc.Get(doc.Rows[1], "şəhər"));
    }

    [Fact]
    public async Task Non_utf8_file_is_reported_not_garbled()
    {
        await using var stream = new MemoryStream([0x61, 0x2C, 0x62, 0x0A, 0xFF, 0xFE, 0x2C, 0x63]);

        var doc = await Csv.ReadAsync(stream, CsvReadOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal("invalid_encoding", Assert.Single(doc.Errors).Code);
    }

    [Fact]
    public void Errors_carry_line_numbers_and_valid_rows_are_kept()
    {
        const string text = "name,email\n" +
                            "Leyla,leyla@a.az\n" +          // 2 ok
                            "Orxan\n" +                     // 3 sahə sayı
                            "Ni\"gar,n@a.az\n" +            // 4 dırnaqsız xanada dırnaq
                            "\"Aysel\"x,a@a.az\n" +         // 5 dırnaqdan sonra mətn
                            "Rəşad,r@a.az\n" +              // 6 ok
                            "\"Kamran,k@a.az\n";            // 7 bağlanmamış dırnaq

        var doc = CsvService.Parse(text, CsvReadOptions.Default);

        Assert.Equal(
            [(3, "field_count_mismatch"), (4, "unexpected_quote"), (5, "text_after_quote"), (7, "unterminated_quote")],
            doc.Errors.Select(e => (e.LineNumber, e.Code)));
        Assert.Equal([2, 6], doc.Rows.Select(r => r.LineNumber));
    }

    [Fact]
    public void Row_limit_and_field_limit_are_enforced()
    {
        var limited = CsvService.Parse("a\n1\n2\n3\n", CsvReadOptions.Default with { MaxRows = 2 });
        var longField = CsvService.Parse($"a\n{new string('x', 11)}\n", CsvReadOptions.Default with { MaxFieldLength = 10 });

        Assert.Equal(2, limited.Rows.Count);
        Assert.Equal((4, "too_many_rows"), (limited.Errors[0].LineNumber, limited.Errors[0].Code));
        Assert.Equal("field_too_long", Assert.Single(longField.Errors).Code);
    }

    [Fact]
    public void Empty_lines_are_skipped_and_missing_header_is_reported()
    {
        Assert.Single(CsvService.Parse("a,b\n\n1,2\n\n", CsvReadOptions.Default).Rows);
        Assert.Equal("missing_header", Assert.Single(CsvService.Parse("", CsvReadOptions.Default).Errors).Code);
        Assert.Equal(2, CsvService.Parse("1,2\r3,4", CsvReadOptions.Default with { HasHeader = false }).Rows.Count);
    }

    [Fact]
    public void Writer_quotes_when_needed_and_uses_crlf()
    {
        Assert.Equal("plain,\"a,b\",\"say \"\"hi\"\"\",\"two\nlines\",\" padded \"\r\n",
            CsvService.FormatLine(["plain", "a,b", "say \"hi\"", "two\nlines", " padded "]));
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+994501234567", "'+994501234567")]
    [InlineData("-5", "'-5")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("Leyla", "Leyla")]
    public void Writer_neutralises_formula_injection(string value, string expected)
    {
        Assert.Equal(expected + "\r\n", CsvService.FormatLine([value]));
    }

    [Fact]
    public async Task Written_file_round_trips_through_the_reader()
    {
        string[] header = ["ad", "qeyd"];
        IReadOnlyList<string>[] rows = [["Gülşən", "Çay, şəkər\nvə \"limon\""], ["Ilkin", ""]];
        await using var stream = new MemoryStream();

        await Csv.WriteAsync(stream, header, rows, TestContext.Current.CancellationToken);
        var bytes = stream.ToArray();
        stream.Position = 0;
        var doc = await Csv.ReadAsync(stream, CsvReadOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.True(doc.IsValid);
        Assert.Equal(header, doc.Header);
        Assert.Equal(rows[0], doc.Rows[0].Fields);
        Assert.Equal(rows[1], doc.Rows[1].Fields);
    }
}
