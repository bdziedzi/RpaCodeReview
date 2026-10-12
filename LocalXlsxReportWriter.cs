using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RpaCodeReview
{
    public static class LocalXlsxReportWriter
    {
        public static void Write(ReviewRunSummary summary, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) outputPath = Path.Combine(summary.ProjectPath, "RpaCodeReviewReport.xlsx");
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? summary.ProjectPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                Add(archive, "[Content_Types].xml", ContentTypes());
                Add(archive, "_rels/.rels", RootRels());
                Add(archive, "xl/workbook.xml", Workbook(summary));
                Add(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
                Add(archive, "xl/styles.xml", Styles());
                Add(archive, "xl/worksheets/sheet1.xml", Sheet(SummaryRows(summary)));
                Add(archive, "xl/worksheets/sheet2.xml", Sheet(ResultRows(summary)));
                Add(archive, "xl/worksheets/sheet3.xml", Sheet(EvidenceRows(summary)));
                Add(archive, "xl/worksheets/sheet4.xml", Sheet(InventoryRows(summary)));
            }
            summary.OutputReportPath = outputPath;
        }

        private static List<string[]> SummaryRows(ReviewRunSummary s) => new List<string[]>
        {
            new [] { "RPA Code Review - Summary", "", "", "" },
            new [] { "ProjectPath", s.ProjectPath, "", "" },
            new [] { "RulesPath", s.RulesPath, "", "" },
            new [] { "StartedUtc", s.StartedUtc, "", "" },
            new [] { "FinishedUtc", s.FinishedUtc, "", "" },
            new [] { "RuleCount", s.RuleCount.ToString(), "", "" },
            new [] { "Pass", s.PassCount.ToString(), "", "" },
            new [] { "Fail", s.FailCount.ToString(), "", "" },
            new [] { "Warning", s.WarningCount.ToString(), "", "" },
            new [] { "Privacy", "Analysis is local and deterministic. No AI or external service is called.", "", "" }
        };

        private static List<string[]> ResultRows(ReviewRunSummary s)
        {
            var rows = new List<string[]> { new [] { "RuleId", "Category", "Title", "Severity", "Status", "Reason", "Recommendation" } };
            rows.AddRange(s.Results.Select(r => new [] { r.RuleId, r.Category, r.Title, r.Severity, r.Status, r.Reason, r.Recommendation }));
            return rows;
        }

        private static List<string[]> EvidenceRows(ReviewRunSummary s)
        {
            var rows = new List<string[]> { new [] { "RuleId", "Status", "RelativePath", "Location", "Quote", "Detail" } };
            rows.AddRange(s.Results.SelectMany(r => r.Evidence.Select(e => new [] { r.RuleId, r.Status, e.RelativePath, e.Location, e.Quote, e.Detail })));
            return rows;
        }

        private static List<string[]> InventoryRows(ReviewRunSummary s)
        {
            var rows = new List<string[]> { new [] { "RelativePath", "Extension", "SizeBytes", "Lines", "Kind" } };
            rows.AddRange(s.Inventory.Select(i => new [] { i.RelativePath, i.Extension, i.SizeBytes.ToString(), i.Lines.ToString(), i.Kind }));
            return rows;
        }

        private static string Sheet(List<string[]> rows)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var root = new XElement(ns + "worksheet", new XAttribute(XNamespace.Xmlns + "r", rel.NamespaceName));
            var data = new XElement(ns + "sheetData");
            for (var r = 0; r < rows.Count; r++)
            {
                var row = new XElement(ns + "row", new XAttribute("r", r + 1));
                for (var c = 0; c < rows[r].Length; c++)
                {
                    var value = rows[r][c] ?? "";
                    row.Add(new XElement(ns + "c", new XAttribute("r", Column(c + 1) + (r + 1)), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))));
                }
                data.Add(row);
            }
            root.Add(data);
            return root.ToString(SaveOptions.DisableFormatting);
        }

        private static string Column(int number)
        {
            var result = "";
            while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; }
            return result;
        }

        private static void Add(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(content);
        }

        private static string ContentTypes() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet4.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>";
        private static string RootRels() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
        private static string Workbook(ReviewRunSummary s) => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Summary\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"RuleResults\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"Evidence\" sheetId=\"3\" r:id=\"rId3\"/><sheet name=\"ProjectInventory\" sheetId=\"4\" r:id=\"rId4\"/></sheets></workbook>";
        private static string WorkbookRels() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/><Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet4.xml\"/><Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
        private static string Styles() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills><borders count=\"1\"><border/></borders><cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellXfs></styleSheet>";
    }
}
