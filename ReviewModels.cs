using System;
using System.Collections.Generic;

namespace RpaCodeReview
{
    public sealed class RuleCatalog
    {
        public string schemaVersion { get; set; } = "1.0";
        public string document { get; set; } = "";
        public string analysisMode { get; set; } = "LocalDeterministic";
        public List<string> statuses { get; set; } = new List<string>();
        public List<RuleDefinition> rules { get; set; } = new List<RuleDefinition>();
    }

    public sealed class RuleDefinition
    {
        public string id { get; set; } = "";
        public string category { get; set; } = "";
        public string title { get; set; } = "";
        public string severity { get; set; } = "Medium";
        public string checkType { get; set; } = "Manual";
        public List<string> filePatterns { get; set; } = new List<string>();
        public List<string> patterns { get; set; } = new List<string>();
        public bool required { get; set; }
        public string guidance { get; set; } = "";
        public string manualReason { get; set; } = "";
        public string implementationStatus { get; set; } = "";
        public string evidenceScope { get; set; } = "";
        public string limitations { get; set; } = "";
        public string recommendedAction { get; set; } = "";
    }

    public sealed class ProjectFileEvidence
    {
        public string RelativePath { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Content { get; set; } = "";
        public string[] Lines { get; set; } = Array.Empty<string>();
    }

    public sealed class RuleEvidence
    {
        public string RuleId { get; set; } = "";
        public string Status { get; set; } = "Warning";
        public string RelativePath { get; set; } = "";
        public string Location { get; set; } = "";
        public string Quote { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    public sealed class RuleResult
    {
        public string RuleId { get; set; } = "";
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Status { get; set; } = "Warning";
        public string Reason { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public List<RuleEvidence> Evidence { get; set; } = new List<RuleEvidence>();
    }

    public sealed class InventoryItem
    {
        public string RelativePath { get; set; } = "";
        public string Extension { get; set; } = "";
        public long SizeBytes { get; set; }
        public int Lines { get; set; }
        public string Kind { get; set; } = "Source";
    }

    public sealed class ReviewRunSummary
    {
        public string ProjectPath { get; set; } = "";
        public string RulesPath { get; set; } = "";
        public string OutputReportPath { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public string FinishedUtc { get; set; } = "";
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int WarningCount { get; set; }
        public int RuleCount { get; set; }
        public List<RuleResult> Results { get; set; } = new List<RuleResult>();
        public List<InventoryItem> Inventory { get; set; } = new List<InventoryItem>();
    }
}
