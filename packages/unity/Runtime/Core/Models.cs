using System;
using System.Collections.Generic;

namespace ConfigSheetForge.Core
{
    public sealed class WorkbookDocument
    {
        public string SchemaVersion { get; set; } = "1";
        public string ProviderId { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceTitle { get; set; } = "";
        public string Revision { get; set; } = "";
        public List<SheetDocument> Sheets { get; set; } = new List<SheetDocument>();
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class SheetDocument
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<ColumnDefinition> Columns { get; set; } = new List<ColumnDefinition>();
        public List<RowDocument> Rows { get; set; } = new List<RowDocument>();
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ColumnDefinition
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ValueKind { get; set; } = "string";
        public bool Required { get; set; }
        public string SourceColumn { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RowDocument
    {
        public string StableId { get; set; } = "";
        public int SourceIndex { get; set; }
        public Dictionary<string, CellValue> Cells { get; set; } = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CellValue
    {
        public string ValueKind { get; set; } = "string";
        public string RawText { get; set; } = "";
        public string NormalizedText { get; set; } = "";
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string SemanticText
        {
            get { return string.IsNullOrWhiteSpace(NormalizedText) ? (RawText ?? "") : NormalizedText; }
        }
    }

    public static class WorkbookCloner
    {
        public static WorkbookDocument Clone(WorkbookDocument source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var clone = new WorkbookDocument
            {
                SchemaVersion = source.SchemaVersion ?? "",
                ProviderId = source.ProviderId ?? "",
                SourceId = source.SourceId ?? "",
                SourceTitle = source.SourceTitle ?? "",
                Revision = source.Revision ?? ""
            };

            CopyDictionary(source.Metadata, clone.Metadata);

            foreach (var sheet in source.Sheets)
            {
                clone.Sheets.Add(CloneSheet(sheet));
            }

            return clone;
        }

        public static SheetDocument CloneSheet(SheetDocument source)
        {
            var clone = new SheetDocument
            {
                Id = source.Id ?? "",
                Name = source.Name ?? ""
            };

            CopyDictionary(source.Metadata, clone.Metadata);

            foreach (var column in source.Columns)
            {
                var columnClone = new ColumnDefinition
                {
                    Key = column.Key ?? "",
                    DisplayName = column.DisplayName ?? "",
                    ValueKind = column.ValueKind ?? "string",
                    Required = column.Required,
                    SourceColumn = column.SourceColumn ?? ""
                };

                foreach (var tag in column.Tags)
                {
                    columnClone.Tags.Add(tag ?? "");
                }

                CopyDictionary(column.Details, columnClone.Details);
                clone.Columns.Add(columnClone);
            }

            foreach (var row in source.Rows)
            {
                clone.Rows.Add(CloneRow(row));
            }

            return clone;
        }

        public static RowDocument CloneRow(RowDocument source)
        {
            var clone = new RowDocument
            {
                StableId = source.StableId ?? "",
                SourceIndex = source.SourceIndex
            };

            CopyDictionary(source.Metadata, clone.Metadata);

            foreach (var pair in source.Cells)
            {
                clone.Cells[pair.Key] = CloneCell(pair.Value);
            }

            return clone;
        }

        public static CellValue CloneCell(CellValue source)
        {
            var clone = new CellValue
            {
                ValueKind = source.ValueKind ?? "string",
                RawText = source.RawText ?? "",
                NormalizedText = source.NormalizedText ?? ""
            };

            CopyDictionary(source.Details, clone.Details);
            return clone;
        }

        private static void CopyDictionary(IDictionary<string, string> source, IDictionary<string, string> target)
        {
            foreach (var pair in source)
            {
                target[pair.Key] = pair.Value ?? "";
            }
        }
    }
}
