using System;
using System.IO;
using UiPath.CodedWorkflows;

namespace RpaCodeReview
{
    public class RpaCodeReviewWorkflow : CodedWorkflow
    {
        [Workflow]
        public string Execute(string ProjectPath, string RulesPath = "", string OutputReportPath = "", bool IncludeHiddenFiles = false, bool FailOnMissingRequiredArtifact = false, string RuleFilter = "")
        {
            var rules = string.IsNullOrWhiteSpace(RulesPath) ? Path.Combine(AppContext.BaseDirectory, "Rules.json") : RulesPath;
            var report = string.IsNullOrWhiteSpace(OutputReportPath) ? Path.Combine(ProjectPath, "RpaCodeReviewReport.xlsx") : OutputReportPath;
            Log("Starting local RPA code review for: " + ProjectPath);
            var summary = LocalProjectReviewEngine.Run(ProjectPath, rules, report, IncludeHiddenFiles, FailOnMissingRequiredArtifact, RuleFilter);
            LocalXlsxReportWriter.Write(summary, report);
            Log("Review finished. Pass=" + summary.PassCount + ", Fail=" + summary.FailCount + ", Warning=" + summary.WarningCount);
            Log("Report written locally to: " + summary.OutputReportPath);
            return summary.OutputReportPath;
        }
    }
}
