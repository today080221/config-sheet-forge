using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ConfigSheetForge.Core
{
    public static class SemanticHasher
    {
        public static string ComputeHash(WorkbookDocument workbook)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            var builder = new StringBuilder();
            AppendWorkbook(builder, workbook);
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                return ToHex(hash);
            }
        }

        internal static string CellFingerprint(CellValue cell)
        {
            if (cell == null)
            {
                return "";
            }

            return Normalize(cell.ValueKind) + ":" + Normalize(cell.SemanticText);
        }

        private static void AppendWorkbook(StringBuilder builder, WorkbookDocument workbook)
        {
            builder.Append("workbook|schema=").Append(Normalize(workbook.SchemaVersion)).AppendLine();
            builder.Append("provider=").Append(Normalize(workbook.ProviderId)).AppendLine();
            builder.Append("source=").Append(Normalize(workbook.SourceId)).AppendLine();

            foreach (var sheet in workbook.Sheets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                AppendSheet(builder, sheet);
            }
        }

        private static void AppendSheet(StringBuilder builder, SheetDocument sheet)
        {
            builder.Append("sheet|").Append(Normalize(sheet.Name)).Append("|").Append(Normalize(sheet.Id)).AppendLine();

            foreach (var column in sheet.Columns.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("column|")
                    .Append(Normalize(column.Key)).Append("|")
                    .Append(Normalize(column.DisplayName)).Append("|")
                    .Append(Normalize(column.ValueKind)).Append("|")
                    .Append(column.Required ? "required" : "optional")
                    .AppendLine();
            }

            foreach (var row in sheet.Rows.OrderBy(r => r.StableId, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.SourceIndex))
            {
                builder.Append("row|").Append(Normalize(row.StableId)).AppendLine();
                foreach (var cell in row.Cells.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append("cell|")
                        .Append(Normalize(cell.Key)).Append("|")
                        .Append(CellFingerprint(cell.Value))
                        .AppendLine();
                }
            }
        }

        private static string Normalize(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.Trim().Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("|", "\\|");
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
