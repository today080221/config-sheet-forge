using System;
using System.Collections.Generic;
using System.Linq;

namespace ConfigSheetForge.Core
{
    public enum MergeEntryStatus
    {
        Unchanged,
        TookOurs,
        TookTheirs,
        Conflict
    }

    public sealed class MergeEntry
    {
        public MergeEntryStatus Status { get; set; }
        public string Sheet { get; set; } = "";
        public string RowId { get; set; } = "";
        public string ColumnKey { get; set; } = "";
        public string Message { get; set; } = "";
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class MergeReport
    {
        public WorkbookDocument MergedWorkbook { get; set; }
        public List<MergeEntry> Entries { get; set; } = new List<MergeEntry>();

        public bool HasConflicts
        {
            get { return Entries.Any(e => e.Status == MergeEntryStatus.Conflict); }
        }
    }

    public static class ThreeWayMerger
    {
        public static MergeReport Merge(WorkbookDocument baseWorkbook, WorkbookDocument ours, WorkbookDocument theirs)
        {
            if (baseWorkbook == null) throw new ArgumentNullException(nameof(baseWorkbook));
            if (ours == null) throw new ArgumentNullException(nameof(ours));
            if (theirs == null) throw new ArgumentNullException(nameof(theirs));

            var report = new MergeReport
            {
                MergedWorkbook = WorkbookCloner.Clone(ours)
            };

            var sheetNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSheetNames(sheetNames, baseWorkbook);
            AddSheetNames(sheetNames, ours);
            AddSheetNames(sheetNames, theirs);

            foreach (var sheetName in sheetNames)
            {
                MergeSheet(report, sheetName, FindSheet(baseWorkbook, sheetName), FindSheet(ours, sheetName), FindSheet(theirs, sheetName));
            }

            return report;
        }

        private static void MergeSheet(MergeReport report, string sheetName, SheetDocument baseSheet, SheetDocument oursSheet, SheetDocument theirsSheet)
        {
            if (oursSheet == null && theirsSheet != null && baseSheet == null)
            {
                report.MergedWorkbook.Sheets.Add(WorkbookCloner.CloneSheet(theirsSheet));
                AddEntry(report, MergeEntryStatus.TookTheirs, sheetName, "", "", "Sheet added by theirs.");
                return;
            }

            if (oursSheet == null || theirsSheet == null)
            {
                AddEntry(report, MergeEntryStatus.Conflict, sheetName, "", "", "One side removed or renamed this sheet while the other side still uses it.");
                return;
            }

            var mergedSheet = FindSheet(report.MergedWorkbook, sheetName);
            if (mergedSheet == null)
            {
                mergedSheet = WorkbookCloner.CloneSheet(oursSheet);
                report.MergedWorkbook.Sheets.Add(mergedSheet);
            }

            MergeRows(report, sheetName, baseSheet, oursSheet, theirsSheet, mergedSheet);
        }

        private static void MergeRows(MergeReport report, string sheetName, SheetDocument baseSheet, SheetDocument oursSheet, SheetDocument theirsSheet, SheetDocument mergedSheet)
        {
            var rowIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            AddRowIds(rowIds, baseSheet);
            AddRowIds(rowIds, oursSheet);
            AddRowIds(rowIds, theirsSheet);

            foreach (var rowId in rowIds)
            {
                var baseRow = FindRow(baseSheet, rowId);
                var oursRow = FindRow(oursSheet, rowId);
                var theirsRow = FindRow(theirsSheet, rowId);
                var mergedRow = FindRow(mergedSheet, rowId);

                if (baseRow == null && oursRow == null && theirsRow != null)
                {
                    mergedSheet.Rows.Add(WorkbookCloner.CloneRow(theirsRow));
                    AddEntry(report, MergeEntryStatus.TookTheirs, sheetName, rowId, "", "Row added by theirs.");
                    continue;
                }

                if (oursRow == null || theirsRow == null)
                {
                    AddEntry(report, MergeEntryStatus.Conflict, sheetName, rowId, "", "One side removed this row while the other side changed or kept it.");
                    continue;
                }

                if (mergedRow == null)
                {
                    mergedRow = WorkbookCloner.CloneRow(oursRow);
                    mergedSheet.Rows.Add(mergedRow);
                }

                MergeCells(report, sheetName, rowId, baseRow, oursRow, theirsRow, mergedRow);
            }
        }

        private static void MergeCells(MergeReport report, string sheetName, string rowId, RowDocument baseRow, RowDocument oursRow, RowDocument theirsRow, RowDocument mergedRow)
        {
            var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCellKeys(keys, baseRow);
            AddCellKeys(keys, oursRow);
            AddCellKeys(keys, theirsRow);

            foreach (var key in keys)
            {
                var baseCell = FindCell(baseRow, key);
                var oursCell = FindCell(oursRow, key);
                var theirsCell = FindCell(theirsRow, key);

                var baseFingerprint = SemanticHasher.CellFingerprint(baseCell);
                var oursFingerprint = SemanticHasher.CellFingerprint(oursCell);
                var theirsFingerprint = SemanticHasher.CellFingerprint(theirsCell);

                if (oursFingerprint == theirsFingerprint)
                {
                    continue;
                }

                if (baseFingerprint == oursFingerprint)
                {
                    if (theirsCell == null)
                    {
                        mergedRow.Cells.Remove(key);
                    }
                    else
                    {
                        mergedRow.Cells[key] = WorkbookCloner.CloneCell(theirsCell);
                    }

                    AddEntry(report, MergeEntryStatus.TookTheirs, sheetName, rowId, key, "Applied theirs because ours did not change this cell.");
                    continue;
                }

                if (baseFingerprint == theirsFingerprint)
                {
                    AddEntry(report, MergeEntryStatus.TookOurs, sheetName, rowId, key, "Kept ours because theirs did not change this cell.");
                    continue;
                }

                var entry = AddEntry(report, MergeEntryStatus.Conflict, sheetName, rowId, key, "Both sides changed this cell differently.");
                entry.Details["base"] = baseFingerprint;
                entry.Details["ours"] = oursFingerprint;
                entry.Details["theirs"] = theirsFingerprint;
            }
        }

        private static MergeEntry AddEntry(MergeReport report, MergeEntryStatus status, string sheet, string rowId, string columnKey, string message)
        {
            var entry = new MergeEntry
            {
                Status = status,
                Sheet = sheet ?? "",
                RowId = rowId ?? "",
                ColumnKey = columnKey ?? "",
                Message = message ?? ""
            };
            report.Entries.Add(entry);
            return entry;
        }

        private static void AddSheetNames(ISet<string> names, WorkbookDocument workbook)
        {
            foreach (var sheet in workbook.Sheets)
            {
                names.Add(string.IsNullOrWhiteSpace(sheet.Name) ? sheet.Id : sheet.Name);
            }
        }

        private static void AddRowIds(ISet<string> ids, SheetDocument sheet)
        {
            if (sheet == null) return;
            foreach (var row in sheet.Rows)
            {
                ids.Add(row.StableId);
            }
        }

        private static void AddCellKeys(ISet<string> keys, RowDocument row)
        {
            if (row == null) return;
            foreach (var key in row.Cells.Keys)
            {
                keys.Add(key);
            }
        }

        private static SheetDocument FindSheet(WorkbookDocument workbook, string name)
        {
            return workbook.Sheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Id, name, StringComparison.OrdinalIgnoreCase));
        }

        private static RowDocument FindRow(SheetDocument sheet, string rowId)
        {
            if (sheet == null) return null;
            return sheet.Rows.FirstOrDefault(r => string.Equals(r.StableId, rowId, StringComparison.OrdinalIgnoreCase));
        }

        private static CellValue FindCell(RowDocument row, string key)
        {
            if (row == null) return null;
            CellValue value;
            return row.Cells.TryGetValue(key, out value) ? value : null;
        }
    }
}
