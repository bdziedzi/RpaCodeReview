using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RpaCodeReview
{
    public static class LocalProjectReviewEngine
    {
        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xaml", ".cs", ".json", ".config", ".xml", ".md", ".txt", ".yml", ".yaml", ".uiproj", ".vb"
        };

        private static readonly HashSet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".local", ".git", "bin", "obj", ".screenshots", ".project", ".vs", "packages"
        };

        public static ReviewRunSummary Run(string projectPath, string rulesPath, string outputReportPath, bool includeHiddenFiles, bool failOnMissingRequiredArtifact, string ruleFilter)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("ProjectPath is required.");
            if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException("ProjectPath does not exist: " + projectPath);
            projectPath = Path.GetFullPath(projectPath);
            rulesPath = string.IsNullOrWhiteSpace(rulesPath) ? Path.Combine(AppContext.BaseDirectory, "Rules.json") : Path.GetFullPath(rulesPath);
            if (!File.Exists(rulesPath)) throw new FileNotFoundException("RulesPath does not exist: " + rulesPath);

            var started = DateTime.UtcNow;
            var catalog = JsonSerializer.Deserialize<RuleCatalog>(File.ReadAllText(rulesPath), JsonOptions()) ?? new RuleCatalog();
            var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(rulesPath),
                string.IsNullOrWhiteSpace(outputReportPath) ? "" : Path.GetFullPath(outputReportPath)
            };
            var files = LoadFiles(projectPath, includeHiddenFiles, excludedPaths);
            var results = new List<RuleResult>();
            foreach (var rule in catalog.rules ?? new List<RuleDefinition>())
            {
                if (!string.IsNullOrWhiteSpace(ruleFilter) && !rule.id.Contains(ruleFilter, StringComparison.OrdinalIgnoreCase) && !rule.category.Contains(ruleFilter, StringComparison.OrdinalIgnoreCase)) continue;
                results.Add(Evaluate(rule, projectPath, files, failOnMissingRequiredArtifact));
            }

            return new ReviewRunSummary
            {
                ProjectPath = projectPath,
                RulesPath = rulesPath,
                OutputReportPath = outputReportPath,
                StartedUtc = started.ToString("O"),
                FinishedUtc = DateTime.UtcNow.ToString("O"),
                Results = results,
                RuleCount = results.Count,
                PassCount = results.Count(r => r.Status == "Pass"),
                FailCount = results.Count(r => r.Status == "Fail"),
                WarningCount = results.Count(r => r.Status == "Warning"),
                Inventory = files.Select(f => new InventoryItem
                {
                    RelativePath = f.RelativePath,
                    Extension = Path.GetExtension(f.RelativePath),
                    SizeBytes = new FileInfo(f.FullPath).Length,
                    Lines = f.Lines.Length,
                    Kind = GetKind(f.RelativePath)
                }).OrderBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static List<ProjectFileEvidence> LoadFiles(string root, bool includeHiddenFiles, HashSet<string> excludedPaths)
        {
            var result = new List<ProjectFileEvidence>();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (excludedPaths != null && excludedPaths.Contains(Path.GetFullPath(path))) continue;
                var relative = Path.GetRelativePath(root, path);
                var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => IgnoredDirectories.Contains(s))) continue;
                if (!includeHiddenFiles && (new FileInfo(path).Attributes & FileAttributes.Hidden) != 0) continue;
                if (!TextExtensions.Contains(Path.GetExtension(path)) && !string.Equals(Path.GetFileName(path), "project.json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var content = File.ReadAllText(path);
                    result.Add(new ProjectFileEvidence { FullPath = path, RelativePath = relative.Replace('\\', '/'), Content = content, Lines = content.Replace("\r\n", "\n").Split('\n') });
                }
                catch (IOException ex)
                {
                    result.Add(new ProjectFileEvidence { FullPath = path, RelativePath = relative.Replace('\\', '/'), Content = "", Lines = new[] { "[unreadable: " + ex.Message + "]" } });
                }
            }
            return result;
        }

        private static RuleResult Evaluate(RuleDefinition rule, string root, List<ProjectFileEvidence> files, bool failOnMissingRequiredArtifact)
        {
            var result = new RuleResult { RuleId = rule.id, Category = rule.category, Title = rule.title, Severity = rule.severity, Recommendation = rule.guidance };
            if (string.Equals(rule.checkType, "Manual", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Warning";
                result.Reason = string.IsNullOrWhiteSpace(rule.manualReason) ? "Wymaga ręcznej weryfikacji." : rule.manualReason;
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "manual review", Detail = result.Reason });
                return result;
            }

            var candidates = SelectFiles(rule, files);
            if (string.Equals(rule.checkType, "DirectoryExists", StringComparison.OrdinalIgnoreCase))
            {
                var directory = (rule.filePatterns ?? new List<string>()).FirstOrDefault() ?? "";
                var exists = Directory.Exists(Path.Combine(root, directory));
                result.Status = exists ? "Pass" : (rule.required || failOnMissingRequiredArtifact ? "Fail" : "Warning");
                result.Reason = exists ? "Znaleziono wymagany katalog." : "Nie znaleziono katalogu: " + directory;
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = result.Status, RelativePath = directory, Location = "directory", Quote = exists ? "Directory exists" : "Directory missing", Detail = result.Reason });
                return result;
            }
            if (string.Equals(rule.checkType, "FileExists", StringComparison.OrdinalIgnoreCase))
            {
                var wanted = (rule.filePatterns ?? new List<string>()).FirstOrDefault() ?? "";
                var file = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f.RelativePath), wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(f.RelativePath, wanted, StringComparison.OrdinalIgnoreCase));
                var exists = file != null || File.Exists(Path.Combine(root, wanted));
                result.Status = exists ? "Pass" : (rule.required || failOnMissingRequiredArtifact ? "Fail" : "Warning");
                result.Reason = exists ? "Znaleziono wymagany plik." : "Nie znaleziono pliku: " + wanted;
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = result.Status, RelativePath = exists && file != null ? file.RelativePath : wanted, Location = "file", Quote = exists ? "File exists" : "File missing", Detail = result.Reason });
                return result;
            }
            if (string.Equals(rule.checkType, "ProjectManifest", StringComparison.OrdinalIgnoreCase))
            {
                var manifest = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f.RelativePath), "project.json", StringComparison.OrdinalIgnoreCase));
                var ok = manifest != null && manifest.Content.Contains("dependencies", StringComparison.OrdinalIgnoreCase) && manifest.Content.Contains("targetFramework", StringComparison.OrdinalIgnoreCase);
                result.Status = ok ? "Pass" : "Fail";
                result.Reason = ok ? "project.json zawiera dependencies i targetFramework." : "project.json jest nieobecny lub nie zawiera wymaganych sekcji.";
                result.Evidence.Add(CreateEvidence(rule, manifest, ok ? "manifest sections" : "project.json missing sections", result.Reason));
                return result;
            }
            if (string.Equals(rule.checkType, "PackageDependency", StringComparison.OrdinalIgnoreCase))
            {
                var manifest = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f.RelativePath), "project.json", StringComparison.OrdinalIgnoreCase));
                var match = manifest != null && (rule.patterns ?? new List<string>()).Any(p => Regex.IsMatch(manifest.Content, p, RegexOptions.IgnoreCase));
                result.Status = match ? "Pass" : (rule.required || failOnMissingRequiredArtifact ? "Fail" : "Warning");
                result.Reason = match ? "Znaleziono pasującą zależność lub bibliotekę." : "Nie znaleziono pasującej zależności w project.json.";
                result.Evidence.Add(CreateEvidence(rule, manifest, match ? "dependency" : "dependencies", result.Reason));
                return result;
            }

            var matches = new List<(ProjectFileEvidence file, Match match)>();
            foreach (var file in candidates)
            {
                foreach (var pattern in rule.patterns ?? new List<string>())
                {
                    try
                    {
                        foreach (Match match in Regex.Matches(file.Content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline)) matches.Add((file, match));
                    }
                    catch (ArgumentException ex)
                    {
                        result.Status = "Warning";
                        result.Reason = "Nieprawidłowy wzorzec reguły: " + ex.Message;
                        result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Detail = result.Reason });
                        return result;
                    }
                }
            }

            bool passed;
            if (string.Equals(rule.checkType, "RegexNoMatch", StringComparison.OrdinalIgnoreCase)) passed = matches.Count == 0;
            else if (string.Equals(rule.checkType, "RegexAllMatches", StringComparison.OrdinalIgnoreCase)) passed = candidates.Count > 0 && matches.Count > 0;
            else passed = matches.Count > 0 || (string.Equals(rule.checkType, "AnyFile", StringComparison.OrdinalIgnoreCase) && candidates.Count > 0);
            result.Status = passed ? "Pass" : (rule.required || failOnMissingRequiredArtifact ? "Fail" : "Warning");
            result.Reason = passed ? "Warunek reguły został potwierdzony w źródłach." : "Nie znaleziono wymaganego dowodu w źródłach projektu.";
            foreach (var item in matches.Take(5)) result.Evidence.Add(CreateEvidence(rule, item.file, item.match.Value, result.Reason, item.match.Index));
            if (result.Evidence.Count == 0 && candidates.Count > 0) result.Evidence.Add(CreateEvidence(rule, candidates[0], "No matching evidence", result.Reason));
            if (result.Evidence.Count == 0) result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = result.Status, Detail = result.Reason });
            return result;
        }

        private static List<ProjectFileEvidence> SelectFiles(RuleDefinition rule, List<ProjectFileEvidence> files)
        {
            if (rule.filePatterns == null || rule.filePatterns.Count == 0) return files;
            return files.Where(f => rule.filePatterns.Any(pattern => WildcardMatch(f.RelativePath, pattern) || WildcardMatch(Path.GetFileName(f.RelativePath), pattern))).ToList();
        }

        private static bool WildcardMatch(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(text ?? "", regex, RegexOptions.IgnoreCase);
        }

        private static RuleEvidence CreateEvidence(RuleDefinition rule, ProjectFileEvidence file, string quote, string detail, int index = 0)
        {
            if (file == null) return new RuleEvidence { RuleId = rule.id, Status = "Warning", Detail = detail };
            var line = Math.Max(1, file.Content.Take(Math.Min(index, file.Content.Length)).Count(c => c == '\n') + 1);
            var lineText = file.Lines.Length >= line ? file.Lines[line - 1].Trim() : quote;
            if (lineText.Length > 240) lineText = lineText.Substring(0, 237) + "...";
            return new RuleEvidence { RuleId = rule.id, Status = "", RelativePath = file.RelativePath, Location = "line " + line, Quote = lineText, Detail = detail };
        }

        private static string GetKind(string path)
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, "project.json", StringComparison.OrdinalIgnoreCase)) return "ProjectManifest";
            if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) return "Workflow";
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return "CSharp";
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return "Workbook";
            return "SupportingFile";
        }

        private static JsonSerializerOptions JsonOptions() => new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
    }
}
