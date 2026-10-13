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

        private sealed class VariableDeclaration
        {
            public ProjectFileEvidence File { get; set; }
            public string Name { get; set; } = "";
            public string TypeName { get; set; } = "";
            public int Index { get; set; }
            public bool IsGlobal { get; set; }
        }

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
                return ManualResult(rule, result, string.IsNullOrWhiteSpace(rule.manualReason) ? "Wymaga ręcznej weryfikacji." : rule.manualReason);
            }
            if (string.Equals(rule.checkType, "RuleGroup", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Warning";
                result.Reason = "Reguła grupująca; wynik należy odczytywać z atomowych podreguł tej grupy.";
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "rule catalog", Detail = result.Reason });
                return result;
            }

            var candidates = SelectFiles(rule, files);
            if (string.Equals(rule.checkType, "VariableNotation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.checkType, "ForbiddenNameToken", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.checkType, "TypedVariablePrefix", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.checkType, "AbbreviationReview", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.checkType, "GlobalVariablePrefix", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateVariableNamingRule(rule, candidates, failOnMissingRequiredArtifact);
            }
            if (string.Equals(rule.checkType, "ArgumentNaming", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateArgumentNamingRule(rule, candidates, failOnMissingRequiredArtifact);
            }
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

        private static RuleResult EvaluateVariableNamingRule(RuleDefinition rule, List<ProjectFileEvidence> candidates, bool failOnMissingRequiredArtifact)
        {
            var result = new RuleResult { RuleId = rule.id, Category = rule.category, Title = rule.title, Severity = rule.severity, Recommendation = rule.guidance };
            var declarations = ExtractVariableDeclarations(candidates);
            if (string.Equals(rule.checkType, "AbbreviationReview", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Warning";
                result.Reason = declarations.Count == 0
                    ? "Nie znaleziono deklaracji zmiennych; ocena skrótów nie ma materiału wejściowego."
                    : "Skróty wymagają ręcznej oceny względem słownika domenowego.";
                foreach (var declaration in declarations.Take(5)) result.Evidence.Add(CreateEvidence(rule, declaration.File, declaration.Name, result.Reason, declaration.Index));
                if (result.Evidence.Count == 0) result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "manual review", Detail = result.Reason });
                return result;
            }

            if (declarations.Count == 0)
            {
                result.Status = "Warning";
                result.Reason = "Nie znaleziono deklaracji zmiennych w wybranych plikach; kontrola wymaga ręcznego potwierdzenia zakresu.";
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "variable declarations", Detail = result.Reason });
                return result;
            }

            List<VariableDeclaration> violations;
            if (string.Equals(rule.checkType, "VariableNotation", StringComparison.OrdinalIgnoreCase))
                violations = declarations.Where(d => !IsAllowedVariableNotation(d.Name)).ToList();
            else if (string.Equals(rule.checkType, "ForbiddenNameToken", StringComparison.OrdinalIgnoreCase))
                violations = declarations.Where(d => HasStandaloneIToken(d.Name)).ToList();
            else if (string.Equals(rule.checkType, "TypedVariablePrefix", StringComparison.OrdinalIgnoreCase))
                violations = declarations.Where(d => IsDataTable(d.TypeName) && !HasDataTablePrefix(d.Name)).ToList();
            else if (string.Equals(rule.checkType, "GlobalVariablePrefix", StringComparison.OrdinalIgnoreCase))
            {
                var globals = declarations.Where(d => d.IsGlobal).ToList();
                if (globals.Count == 0)
                {
                    result.Status = "Warning";
                    result.Reason = "Nie rozpoznano globalnych deklaracji zmiennych; scope globalny wymaga ręcznego potwierdzenia.";
                    result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "global variable scope", Detail = result.Reason });
                    return result;
                }
                violations = globals.Where(d => !HasGlobalPrefix(d.Name)).ToList();
            }
            else
            {
                result.Status = "Warning";
                result.Reason = "Nieobsługiwany typ kontroli zmiennych: " + rule.checkType;
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Detail = result.Reason });
                return result;
            }

            var passed = violations.Count == 0;
            result.Status = passed ? "Pass" : (rule.required || failOnMissingRequiredArtifact ? "Fail" : "Warning");
            result.Reason = passed ? "Wszystkie rozpoznane deklaracje spełniają warunek nazewnictwa." : "Znaleziono deklaracje niespełniające warunku nazewnictwa.";
            foreach (var violation in violations.Take(10)) result.Evidence.Add(CreateEvidence(rule, violation.File, violation.Name, result.Reason, violation.Index));
            if (result.Evidence.Count == 0) result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = result.Status, Location = "variable declarations", Detail = result.Reason });
            return result;
        }

        private static RuleResult EvaluateArgumentNamingRule(RuleDefinition rule, List<ProjectFileEvidence> candidates, bool failOnMissingRequiredArtifact)
        {
            var result = new RuleResult { RuleId = rule.id, Category = rule.category, Title = rule.title, Severity = rule.severity, Recommendation = rule.guidance };
            var arguments = ExtractArgumentDeclarations(candidates);
            if (arguments.Count == 0)
            {
                result.Status = "Warning";
                result.Reason = "Nie znaleziono deklaracji argumentów w obsługiwanych postaciach XAML/C#; wymaga ręcznego potwierdzenia.";
                result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "argument declarations", Detail = result.Reason });
                return result;
            }
            var violations = arguments.Where(a => !HasArgumentPrefix(a.Name)).ToList();
            var passed = violations.Count == 0;
            result.Status = passed ? "Pass" : (rule.required || failOnMissingRequiredArtifact ? "Fail" : "Warning");
            result.Reason = passed ? "Wszystkie rozpoznane argumenty mają prefiks kierunku." : "Znaleziono argumenty bez prefiksu in_, out_ albo io_.";
            foreach (var argument in violations.Take(10)) result.Evidence.Add(CreateEvidence(rule, argument.File, argument.Name, result.Reason, argument.Index));
            if (result.Evidence.Count == 0) result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = result.Status, Location = "argument declarations", Detail = result.Reason });
            return result;
        }

        private static List<VariableDeclaration> ExtractVariableDeclarations(List<ProjectFileEvidence> files)
        {
            var result = new List<VariableDeclaration>();
            foreach (var file in files)
            {
                var isGlobalFile = Regex.IsMatch(file.RelativePath, "GlobalVariables|GlobalConstants", RegexOptions.IgnoreCase);
                if (file.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Match match in Regex.Matches(file.Content, "<Variable\\b(?<attrs>[^>]*?\\bName\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        var attrs = match.Groups["attrs"].Value;
                        var typeMatch = Regex.Match(attrs, "(?:x:TypeArguments|TypeArguments)\\s*=\\s*[\\\"'](?<type>[^\\\"']+)", RegexOptions.IgnoreCase);
                        result.Add(new VariableDeclaration
                        {
                            File = file,
                            Name = match.Groups["name"].Value,
                            TypeName = typeMatch.Success ? typeMatch.Groups["type"].Value : "",
                            Index = match.Index,
                            IsGlobal = isGlobalFile
                        });
                    }
                }
                if (file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Match match in Regex.Matches(file.Content, "\\b(?<type>(?:System\\.Data\\.)?(?:DataTable|DataSet|DataRow)|[A-Z][A-Za-z0-9_]*(?:<[^>]+>)?)\\s+(?<name>[A-Za-z_]\\w*)\\s*(?==|;|,|\\{)", RegexOptions.Multiline))
                    {
                        result.Add(new VariableDeclaration { File = file, Name = match.Groups["name"].Value, TypeName = match.Groups["type"].Value, Index = match.Index, IsGlobal = isGlobalFile || IsStaticContext(file.Content, match.Index) });
                    }
                    foreach (Match match in Regex.Matches(file.Content, "\\bvar\\s+(?<name>[A-Za-z_]\\w*)\\s*=\\s*(?:new\\s+)?(?<type>[A-Za-z_][A-Za-z0-9_.]*)?", RegexOptions.Multiline))
                    {
                        result.Add(new VariableDeclaration { File = file, Name = match.Groups["name"].Value, TypeName = match.Groups["type"].Value, Index = match.Index, IsGlobal = isGlobalFile || IsStaticContext(file.Content, match.Index) });
                    }
                }
            }
            return result.GroupBy(d => d.File.RelativePath + "|" + d.Index + "|" + d.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        private static List<VariableDeclaration> ExtractArgumentDeclarations(List<ProjectFileEvidence> files)
        {
            var result = new List<VariableDeclaration>();
            foreach (var file in files)
            {
                foreach (Match match in Regex.Matches(file.Content, "x:Property\\s+Name\\s*=\\s*[\\\"'](?<name>[A-Za-z_]\\w*)[\\\"']", RegexOptions.IgnoreCase))
                    result.Add(new VariableDeclaration { File = file, Name = match.Groups["name"].Value, Index = match.Index });
                foreach (Match match in Regex.Matches(file.Content, "this:[A-Za-z_]\\w*\\.(?<name>[A-Za-z_]\\w*)\\s*=", RegexOptions.IgnoreCase))
                    result.Add(new VariableDeclaration { File = file, Name = match.Groups["name"].Value, Index = match.Index });
                foreach (Match method in Regex.Matches(file.Content, "\\bExecute\\s*\\((?<params>[^)]*)\\)", RegexOptions.Singleline))
                {
                    var parameters = method.Groups["params"].Value.Split(',');
                    var offset = method.Groups["params"].Index;
                    var searchOffset = 0;
                    foreach (var parameter in parameters)
                    {
                        var match = Regex.Match(parameter.Trim(), "(?<name>[A-Za-z_]\\w*)\\s*(?:=|$)");
                        if (match.Success)
                        {
                            result.Add(new VariableDeclaration { File = file, Name = match.Groups["name"].Value, Index = offset + searchOffset + Math.Max(0, parameter.IndexOf(match.Groups["name"].Value, StringComparison.Ordinal)) });
                        }
                        searchOffset += parameter.Length + 1;
                    }
                }
            }
            return result.GroupBy(d => d.File.RelativePath + "|" + d.Index + "|" + d.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        private static bool IsStaticContext(string content, int index)
        {
            var start = Math.Max(0, index - 120);
            var prefix = content.Substring(start, index - start);
            var lastLine = prefix.Split('\n').LastOrDefault() ?? "";
            return Regex.IsMatch(lastLine, "\\bstatic\\b|\\bpublic\\s+readonly\\b", RegexOptions.IgnoreCase);
        }

        private static bool IsAllowedVariableNotation(string name)
        {
            var normalized = name ?? "";
            if (normalized.StartsWith("global_", StringComparison.Ordinal)) normalized = normalized.Substring(7);
            else if (normalized.StartsWith("g_", StringComparison.Ordinal)) normalized = normalized.Substring(2);
            if (normalized.StartsWith("dt_", StringComparison.Ordinal)) normalized = normalized.Substring(3);
            return Regex.IsMatch(normalized, "^[A-Z][A-Za-z0-9]*$") || Regex.IsMatch(normalized, "^[a-z][A-Za-z0-9]*$") || Regex.IsMatch(normalized, "^[a-z][a-z0-9]*(?:_[a-z0-9]+)+$");
        }

        private static bool HasStandaloneIToken(string name) => Regex.IsMatch(name ?? "", "(^|[_\\-])i($|[_\\-])", RegexOptions.IgnoreCase);
        private static bool IsDataTable(string typeName) => Regex.IsMatch(typeName ?? "", "(^|\\.)DataTable$", RegexOptions.IgnoreCase);
        private static bool HasDataTablePrefix(string name) => Regex.IsMatch(name ?? "", "^dt(?:_|[A-Z])", RegexOptions.CultureInvariant);
        private static bool HasGlobalPrefix(string name) => (name ?? "").StartsWith("g_", StringComparison.Ordinal) || (name ?? "").StartsWith("global_", StringComparison.Ordinal);
        private static bool HasArgumentPrefix(string name) => Regex.IsMatch(name ?? "", "^(in_|out_|io_)", RegexOptions.CultureInvariant);

        private static RuleResult ManualResult(RuleDefinition rule, RuleResult result, string reason)
        {
            result.Status = "Warning";
            result.Reason = reason;
            result.Evidence.Add(new RuleEvidence { RuleId = rule.id, Status = "Warning", Location = "manual review", Detail = reason });
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
            var regex = "^" + Regex.Escape(pattern).Replace("\\\\*", ".*").Replace("\\\\?", ".") + "$";
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
