using System;
using System.Collections.Generic;
using System.Linq;

namespace ConfigSheetForge.Core
{
    public static class SchemaReviewer
    {
        public static ValidationReport Review(WorkbookDocument workbook)
        {
            var report = PortableSubsetValidator.Validate(workbook);
            if (workbook == null)
            {
                return report;
            }

            foreach (var sheet in workbook.Sheets)
            {
                ReviewSheet(report, sheet);
            }

            return report;
        }

        private static void ReviewSheet(ValidationReport report, SheetDocument sheet)
        {
            var location = "$.sheets[" + (sheet.Name ?? sheet.Id) + "]";
            var hasStableIdColumn = sheet.Columns.Any(c =>
                string.Equals(c.Key, "id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Key, "key", StringComparison.OrdinalIgnoreCase));

            if (!hasStableIdColumn)
            {
                report.Add(FindingSeverity.Warning, "schema.stable_id_column_missing", "This sheet has no id/key column. Merges are safer when planners can see the stable id.", location);
            }

            foreach (var column in sheet.Columns)
            {
                if (string.IsNullOrWhiteSpace(column.DisplayName))
                {
                    report.Add(FindingSeverity.Warning, "schema.display_name_missing", "A column has no human-facing display name.", location + ".columns[" + column.Key + "]");
                }

                if (ContainsSensitiveWord(column.Key) || ContainsSensitiveWord(column.DisplayName))
                {
                    report.Add(FindingSeverity.Warning, "schema.sensitive_name", "A column name looks like it may contain private credentials or internal routing. Keep secrets out of config sheets.", location + ".columns[" + column.Key + "]");
                }
            }

            foreach (var metadataKey in sheet.Metadata.Keys)
            {
                if (ContainsSensitiveWord(metadataKey))
                {
                    report.Add(FindingSeverity.Warning, "schema.sensitive_metadata", "Sheet metadata contains a sensitive-looking key. Keep project-specific secrets in local config, not the workbook model.", location + ".metadata[" + metadataKey + "]");
                }
            }
        }

        private static bool ContainsSensitiveWord(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var words = new List<string> { "secret", "token", "password", "passwd", "credential", "owner_email", "private_url" };
            return words.Any(w => value.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
