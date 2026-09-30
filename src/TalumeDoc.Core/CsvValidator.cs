using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace TalumeDoc.Core;

/// <summary>UTF-8 CSV, comma delimiter, quoted fields and embedded line breaks supported.</summary>
public sealed class CsvValidator
{
    public ValidationReport Validate(byte[] bytes)
    {
        if (bytes.Length > DocumentLimits.MaxBytes) return Reject("SIZE", "O arquivo excede o limite de 2 MiB.");
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields();
            string[] expected = ["sku", "nome", "quantidade", "preco"];
            if (headers is null || !headers.Select(h => h.Trim().ToLowerInvariant()).SequenceEqual(expected))
                return Reject("HEADER", "Cabeçalho esperado: sku,nome,quantidade,preco (nesta ordem).");
            var issues = new List<ValidationIssue>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var total = 0;
            var invalid = 0;
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields()!;
                if (++total > DocumentLimits.MaxRows) return Reject("ROWS", "O limite é de 10.000 registros por arquivo.");
                var errors = new List<(string Code, string Message)>();
                if (fields.Length != 4) errors.Add(("COLUMNS", "O registro precisa ter exatamente quatro campos."));
                else
                {
                    var sku = fields[0].Trim();
                    var name = fields[1].Trim();
                    if (sku.Length is < 1 or > 64) errors.Add(("SKU", "SKU obrigatório, com até 64 caracteres."));
                    else if (!seen.Add(sku)) errors.Add(("DUPLICATE", "SKU repetido neste arquivo."));
                    if (name.Length is < 1 or > 200) errors.Add(("NAME", "Nome obrigatório, com até 200 caracteres."));
                    if (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var qty) || qty < 0)
                        errors.Add(("QUANTITY", "Quantidade deve ser um inteiro não negativo."));
                    if (!decimal.TryParse(fields[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
                        || price <= 0 || price > 999999999 || decimal.Round(price, 2) != price)
                        errors.Add(("PRICE", "Preço deve ser positivo, até 999999999, com ponto decimal e até duas casas."));
                }
                if (errors.Count > 0) invalid++;
                issues.AddRange(errors.Select(e => new ValidationIssue(total + 1, e.Code, e.Message)));
            }
            if (total == 0) return Reject("EMPTY", "O arquivo precisa conter pelo menos um registro.");
            return new(total, total - invalid, invalid, issues);
        }
        catch (MalformedLineException) { return Reject("CSV", "CSV malformado. Verifique aspas e separadores."); }
        catch (DecoderFallbackException) { return Reject("ENCODING", "Salve o arquivo usando a codificação UTF-8."); }
    }
    private static ValidationReport Reject(string code, string message) => new(0, 0, 0, [new(1, code, message)], true);
    public static byte[] ToCsv(ValidationReport report)
    {
        // Only fixed validation messages are exported: no user-provided formula can enter a cell.
        var result = new StringBuilder("registro,codigo,mensagem\r\n");
        foreach (var issue in report.Issues)
            result.Append(issue.Row).Append(',').Append(issue.Code).Append(',').Append('"')
                .Append(issue.Message.Replace("\"", "\"\"")).Append("\"\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.ToString())).ToArray();
    }
}
