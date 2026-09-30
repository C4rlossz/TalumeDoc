using System.Text;
using TalumeDoc.Core;
using Xunit;
namespace TalumeDoc.Tests;
public class ValidationTests
{
    private static ValidationReport Validate(string rows) => new CsvValidator().Validate(Encoding.UTF8.GetBytes("sku,nome,quantidade,preco\n" + rows));
    [Fact] public void SupportsQuotedCommaAndMultiline()
    {
        var r = Validate("A,\"Pasta, A4\",0,10.50\nB,\"Nome\ncom quebra\",2,3.00\n");
        Assert.Equal(2, r.ValidRows); Assert.False(r.Rejected); Assert.Empty(r.Issues);
    }
    [Theory]
    [InlineData("A,Nome,-1,1.00", "QUANTITY")]
    [InlineData("A,Nome,1,0", "PRICE")]
    [InlineData("A,Nome,1,1.001", "PRICE")]
    [InlineData("A,,1,1.00", "NAME")]
    [InlineData(",Nome,1,1.00", "SKU")]
    [InlineData("A,Nome,1", "COLUMNS")]
    public void InvalidValuesAreReported(string row, string code)
    { var r = Validate(row); Assert.Equal(1, r.InvalidRows); Assert.Contains(r.Issues, i => i.Code == code); }
    [Fact] public void DuplicateSkuIsCaseInsensitive()
    { var r = Validate("A,Nome,1,1\na,Nome,1,1"); Assert.Equal(1, r.ValidRows); Assert.Contains(r.Issues, i => i.Code == "DUPLICATE" && i.Row == 3); }
    [Theory]
    [InlineData("", "EMPTY")]
    [InlineData("A,\"broken,1,1", "CSV")]
    public void RejectsInvalidDocument(string text, string code)
    { var r = Validate(text); Assert.True(r.Rejected); Assert.Equal(code, r.Issues[0].Code); }
    [Fact] public void RejectsWrongHeaders() => Assert.True(new CsvValidator().Validate("wrong,header"u8.ToArray()).Rejected);
    [Fact] public void RejectsInvalidUtf8() => Assert.Equal("ENCODING", new CsvValidator().Validate([0xff, 0xff]).Issues[0].Code);
    [Fact] public void RejectsTooManyRows() => Assert.Equal("ROWS", Validate(string.Join('\n', Enumerable.Range(0, 10001).Select(i => $"{i},Nome,1,1"))).Issues[0].Code);
    [Fact] public void RejectsOversizedFile() => Assert.Equal("SIZE", new CsvValidator().Validate(new byte[DocumentLimits.MaxBytes + 1]).Issues[0].Code);
    [Fact] public void ReportNeverExportsUserFormulas()
    { var r = Validate("A,=HYPERLINK(1),-1,0"); Assert.DoesNotContain("HYPERLINK", Encoding.UTF8.GetString(CsvValidator.ToCsv(r))); }
}
