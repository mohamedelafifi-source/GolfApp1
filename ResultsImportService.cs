
//ResultsImportService.cs
//==========================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using GolfApp1.Models;

namespace GolfApp1.Services
{
    internal sealed class ResultsImportService
    {
        private const double RowTolerance = 3.0;

        public async Task<List<ParsedPlayerRecord>> ParsePdfAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            var list = new List<ParsedPlayerRecord>();

            await Task.Run(() =>
            {
                using var doc = PdfDocument.Open(filePath);
                for (int i = 1; i <= doc.NumberOfPages; i++)
                {
                    var page = doc.GetPage(i);
                    var rows = ExtractLinesFromPage(page);
                    foreach (var line in rows)
                    {
                        var parsed = TryParseLine(line);
                        parsed.Page = i;
                        parsed.RawLine = line;
                        list.Add(parsed);
                    }
                }
            });

            return list;
        }

        private static List<string> ExtractLinesFromPage(Page page)
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0) return new List<string>();

            var buckets = new List<(double Y, List<(double X, string Text)>)>();

            foreach (var w in words)
            {
                var y = w.BoundingBox.Bottom;
                var x = w.BoundingBox.Left;
                var text = w.Text;

                var bucket = buckets.FirstOrDefault(b => Math.Abs(b.Y - y) <= RowTolerance);
                if (bucket.Y == 0 && buckets.Count == 0)
                {
                    buckets.Add((y, new List<(double X, string Text)> { ValueTuple.Create(x, text) }));
                }
                else if (Math.Abs(bucket.Y - y) <= RowTolerance)
                {
                    bucket.Item2.Add(ValueTuple.Create(x, text));
                    var idx = buckets.FindIndex(b => Math.Abs(b.Y - y) <= RowTolerance);
                    buckets[idx] = (bucket.Y, bucket.Item2);
                }
                else
                {
                    buckets.Add((y, new List<(double X, string Text)> { ValueTuple.Create(x, text) }));
                }
            }

            var ordered = buckets.OrderByDescending(b => b.Y).ToList();

            var lines = new List<string>();
            foreach (var b in ordered)
            {
                var row = string.Join(' ', b.Item2.OrderBy(t => t.X).Select(t => t.Text));
                row = Regex.Replace(row, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(row)) lines.Add(row);
            }

            return lines;
        }

        private static ParsedPlayerRecord TryParseLine(string line)
        {
            var rec = new ParsedPlayerRecord
            {
                RawLine = line,
                Name = string.Empty,
                HandicapIndex = string.Empty,
                Result = string.Empty,
                Confidence = 0.0
            };

            if (string.IsNullOrWhiteSpace(line)) return rec;

            // Labelled pattern e.g. "Name : Mohamed Kabbani . Score 42 pts , Handicap (17) ."
            var labelledRx = new Regex(
                @"Name\s*[:\-]\s*(?<name>[\w\.\-,'\u00C0-\u017F\s]{2,80})\s*[\.\,]?\s*Score\s*[:\-]?\s*(?<score>-?\d+)(?:\s*pts?)?\s*[,;]?\s*Handicap\s*(?:[:\-]?\s*)\(?\s*(?<index>\d{1,2}(?:\.\d)?)\s*\)?",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var mLab = labelledRx.Match(line);
            if (mLab.Success)
            {
                rec.Name = mLab.Groups["name"].Value.Trim();
                rec.Result = mLab.Groups["score"].Value.Trim();
                rec.HandicapIndex = mLab.Groups["index"].Value.Trim();

                var conf = 0.0;
                if (!string.IsNullOrWhiteSpace(rec.Name)) conf += 0.5;
                if (!string.IsNullOrWhiteSpace(rec.HandicapIndex)) conf += 0.25;
                if (!string.IsNullOrWhiteSpace(rec.Result)) conf += 0.25;
                rec.Confidence = Math.Min(1.0, conf);
                return rec;
            }

            // Direct pattern for "<pos?> <Name> <Score> (Handicap)"
            var scoreThenParenRx = new Regex(
                @"^\s*(?:\d+\s+)?(?<name>.+?)\s+(?<score>-?\d+|WD|DQ|DNS)(?:\s*pts?)?\s*\(\s*(?<index>[+-]?\d{1,2}(?:\.\d)?)\s*\)\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var mScoreParen = scoreThenParenRx.Match(line);
            if (mScoreParen.Success)
            {
                rec.Name = mScoreParen.Groups["name"].Value.Trim();
                rec.Result = mScoreParen.Groups["score"].Value.Trim();
                rec.HandicapIndex = mScoreParen.Groups["index"].Value.Trim();

                var conf = 0.0;
                if (!string.IsNullOrWhiteSpace(rec.Name)) conf += 0.5;
                if (!string.IsNullOrWhiteSpace(rec.HandicapIndex)) conf += 0.25;
                if (!string.IsNullOrWhiteSpace(rec.Result)) conf += 0.25;
                rec.Confidence = Math.Min(1.0, conf);
                return rec;
            }

            // Flexible legacy patterns
            var patterns = new[]
            {
                new Regex(@"^\s*(?:\d+\s+)?(?<name>[A-Za-z\.\-,'\u00C0-\u017F\s]{3,60})\s+(?<index>\d{1,2}(?:\.\d)?)\s+(?<score>-?\d+|WD|DQ|DNS)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"^\s*(?<name>[A-Za-z\.\-,'\u00C0-\u017F\s]{3,60})\s*\(\s*(?<index>\d{1,2}(?:\.\d)?)\s*\)\s+(?<score>-?\d+|WD|DQ|DNS)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"^\s*(?<name>.+?)\s{2,}(?<index>\d{1,2}(?:\.\d)?)\s{2,}(?<score>-?\d+|WD|DQ|DNS)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"^\s*(?:\d+\s+)?(?<name>.+?)\s+(?<score>-?\d+|WD|DQ|DNS)\s+(?<index>\d{1,2}(?:\.\d)?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            };

            foreach (var rx in patterns)
            {
                var m = rx.Match(line);
                if (!m.Success) continue;

                rec.Name = m.Groups["name"].Value.Trim();
                rec.HandicapIndex = m.Groups["index"].Success ? m.Groups["index"].Value.Trim() : string.Empty;
                rec.Result = m.Groups["score"].Success ? m.Groups["score"].Value.Trim() : string.Empty;

                var conf = 0.0;
                if (!string.IsNullOrWhiteSpace(rec.Name)) conf += 0.5;
                if (!string.IsNullOrWhiteSpace(rec.HandicapIndex)) conf += 0.25;
                if (!string.IsNullOrWhiteSpace(rec.Result)) conf += 0.25;
                rec.Confidence = Math.Min(1.0, conf);

                return rec;
            }

            // Token fallback
            var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2)
            {
                var last = tokens[^1].Trim().TrimEnd('.', ',');
                var secondLast = tokens.Length >= 2 ? tokens[^2].Trim().TrimEnd('.', ',') : string.Empty;

                if (Regex.IsMatch(last, @"^(?:-?\d+|WD|DQ|DNS)$", RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(secondLast, @"^\d{1,2}(?:\.\d)?$"))
                {
                    rec.Result = last;
                    rec.HandicapIndex = secondLast;
                    rec.Name = string.Join(' ', tokens, 0, tokens.Length - 2);
                    rec.Confidence = 0.85;
                    return rec;
                }
            }

            rec.Name = line.Trim();
            rec.Confidence = 0.1;
            return rec;
        }
    }
}