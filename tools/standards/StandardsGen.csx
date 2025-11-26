#!/usr/bin/env dotnet-script
#r "nuget: YamlDotNet, 16.2.0"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// Algorithmic repository root finder - walks up until solution file found
static string FindRepositoryRoot(string startPath) {
    string current = startPath;
    while (current != null && !File.Exists(Path.Combine(current, "Parametric_Arsenal.sln"))) {
        DirectoryInfo parent = Directory.GetParent(current);
        current = parent?.FullName;
    }
    return current ?? throw new InvalidOperationException($"Repository root not found from {startPath}");
}

string currentDir = Directory.GetCurrentDirectory();
string repoRoot = FindRepositoryRoot(currentDir);

Console.WriteLine($"[INFO] Repository root: {repoRoot}");
Console.WriteLine($"[INFO] Current directory: {currentDir}");
Console.WriteLine();

// Load STANDARDS.yaml
string yamlPath = Path.Combine(repoRoot, "tools", "standards", "STANDARDS.yaml");
Console.WriteLine($"[INFO] Loading STANDARDS.yaml from: {yamlPath}");

if (!File.Exists(yamlPath)) {
    Console.WriteLine($"[ERROR] STANDARDS.yaml not found at {yamlPath}");
    Environment.Exit(1);
}

string yamlContent = File.ReadAllText(yamlPath);
IDeserializer deserializer = new DeserializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .Build();

dynamic standards = deserializer.Deserialize<dynamic>(yamlContent);

Console.WriteLine($"[PASS] Loaded STANDARDS.yaml version {standards["version"]}");
Console.WriteLine();

// Helper to generate [CRITICAL RULES] section for agent files
string GenerateCriticalRulesSection() {
    StringBuilder sb = new();
    
    sb.AppendLine("# [CRITICAL RULES] - ZERO TOLERANCE");
    sb.AppendLine();
    sb.AppendLine("## Universal Limits (ABSOLUTE MAXIMUMS)");
    
    dynamic limits = standards["limits"];
    sb.AppendLine($"- **{limits["files_per_folder"]["maximum"]} files maximum** per folder (ideal: {limits["files_per_folder"]["ideal"]})");
    sb.AppendLine($"- **{limits["types_per_folder"]["maximum"]} types maximum** per folder (ideal: {limits["types_per_folder"]["ideal"]})");
    sb.AppendLine($"- **{limits["loc_per_member"]["maximum"]} LOC maximum** per member (ideal: {limits["loc_per_member"]["ideal"]})");
    sb.AppendLine($"- **PURPOSE**: {limits["loc_per_member"]["note"]}");
    sb.AppendLine();
    
    sb.AppendLine("## Mandatory Patterns (NEVER DEVIATE)");
    
    // Get syntax rules with error severity
    dynamic rules = standards["rules"];
    List<dynamic> syntaxRules = ((List<object>)rules["syntax"]).Cast<dynamic>().Where(r => r["severity"] == "error").ToList();
    
    int count = 1;
    foreach (dynamic rule in syntaxRules.Take(5)) {
        string desc = rule["description"];
        sb.AppendLine($"{count}. **{rule["id"].Replace('_', ' ')}** - {desc}");
        count++;
    }
    
    sb.AppendLine();
    sb.AppendLine("## Always Required");
    sb.AppendLine("- Named parameters (non-obvious args)");
    sb.AppendLine("- Trailing commas (multi-line collections)");
    sb.AppendLine("- K&R brace style (same line)");
    sb.AppendLine("- File-scoped namespaces");
    sb.AppendLine("- Target-typed `new()`");
    sb.AppendLine("- Collection expressions `[]`");
    sb.AppendLine("- Result<T> for error handling");
    sb.AppendLine("- UnifiedOperation for polymorphic dispatch");
    sb.AppendLine("- E.* error registry");
    
    return sb.ToString();
}

// Helper to generate copilot-instructions.md IMMEDIATE BLOCKERS section
string GenerateImmediateBlockers() {
    StringBuilder sb = new();
    
    sb.AppendLine("## [BLOCKERS] IMMEDIATE BLOCKERS (Fix Before Proceeding)");
    sb.AppendLine();
    sb.AppendLine("These violations fail the build. Check for and fix immediately:");
    sb.AppendLine();
    
    dynamic rules = standards["rules"];
    List<dynamic> allRules = new List<dynamic>();
    allRules.AddRange(((List<object>)rules["syntax"]).Cast<dynamic>().Where(r => r["severity"] == "error"));
    
    // Add limit violations
    dynamic limits = standards["limits"];
    
    int count = 1;
    foreach (dynamic rule in allRules.Take(7)) {
        string id = rule["id"];
        string desc = rule["description"];
        string exampleWrong = rule.ContainsKey("example_wrong") ? rule["example_wrong"] : "";
        string exampleCorrect = rule.ContainsKey("example_correct") ? rule["example_correct"] : "";
        
        string actionText = id switch {
            "NO_VAR" => "Replace with explicit type",
            "NO_IF_ELSE" => "Replace with ternary (binary), switch expression (multiple), or pattern matching (type discrimination)",
            "TRAILING_COMMA" => "Add `,` at end",
            "NAMED_PARAMETERS" => "Add `parameter: value`",
            "TARGET_TYPED_NEW" => "Use target-typed `new()`",
            "COLLECTION_EXPRESSIONS" => "Use collection expressions `[]`",
            "ONE_TYPE_PER_FILE" => "Split into separate files (CA1050)",
            _ => desc
        };
        
        sb.AppendLine($"{count}. **{(exampleWrong != "" ? exampleWrong : id.Replace('_', ' '))}** → {actionText}");
        count++;
    }
    
    // Add organizational limits
    sb.AppendLine($"{count}. **Folder has >{limits["files_per_folder"]["maximum"]} files** → Consolidate into {limits["files_per_folder"]["ideal"]} files");
    count++;
    sb.AppendLine($"{count}. **Folder has >{limits["types_per_folder"]["maximum"]} types** → Consolidate into {limits["types_per_folder"]["ideal"]} types");
    count++;
    sb.AppendLine($"{count}. **Member has >{limits["loc_per_member"]["maximum"]} LOC** → Improve algorithm, don't extract helpers");
    
    return sb.ToString();
}

// Polymorphic section replacer - works for any markdown section
static string ReplaceMarkdownSection(string content, string sectionHeader, string newSectionContent) {
    string pattern = $@"({Regex.Escape(sectionHeader)}.*?)(\n# \[|$)";
    return Regex.Replace(content, pattern, m => newSectionContent + m.Groups[2].Value, RegexOptions.Singleline);
}

// Parameterized file updater with error handling
static bool UpdateFileSection(string filePath, string sectionHeader, string newContent, string displayName) {
    try {
        string content = File.ReadAllText(filePath);
        
        if (!content.Contains(sectionHeader)) {
            Console.WriteLine($"[WARN] Skipping {displayName} - section '{sectionHeader}' not found");
            return false;
        }
        
        string updatedContent = ReplaceMarkdownSection(content, sectionHeader, newContent);
        
        if (updatedContent == content) {
            Console.WriteLine($"[WARN] No changes for {displayName}");
            return false;
        }
        
        File.WriteAllText(filePath, updatedContent);
        Console.WriteLine($"   [PASS] Updated {displayName}");
        return true;
    } catch (Exception ex) {
        Console.WriteLine($"[ERROR] Failed to update {displayName}: {ex.Message}");
        return false;
    }
}

// Generate CRITICAL RULES section for all agent files
Console.WriteLine("[INFO] Updating [CRITICAL RULES] sections in agent files...");
string agentDir = Path.Combine(repoRoot, ".github", "agents");
string[] agentFiles = Directory.GetFiles(agentDir, "*.agent.md");

string criticalRulesSection = GenerateCriticalRulesSection();

int updatedAgents = agentFiles.Count(file => 
    UpdateFileSection(file, "# [CRITICAL RULES]", criticalRulesSection, Path.GetFileName(file))
);

Console.WriteLine($"[PASS] Updated {updatedAgents} agent files");
Console.WriteLine();

// Update copilot-instructions.md IMMEDIATE BLOCKERS section using algorithmic replacer
Console.WriteLine("[INFO] Updating copilot-instructions.md...");
string copilotPath = Path.Combine(repoRoot, ".github", "copilot-instructions.md");

if (File.Exists(copilotPath)) {
    string blockersContent = GenerateImmediateBlockers() + "\n";
    UpdateFileSection(copilotPath, "## [BLOCKERS] IMMEDIATE BLOCKERS", blockersContent, "copilot-instructions.md");
} else {
    Console.WriteLine("   [WARN] copilot-instructions.md not found");
}

Console.WriteLine();
Console.WriteLine("[PASS] Standards generation complete!");
Console.WriteLine();
Console.WriteLine("[INFO] Summary:");
Console.WriteLine($"       - Updated {updatedAgents} agent files with synchronized [CRITICAL RULES]");
Console.WriteLine($"       - Updated copilot-instructions.md with IMMEDIATE BLOCKERS");
Console.WriteLine($"       - Source of truth: {yamlPath}");
Console.WriteLine();
Console.WriteLine("[INFO] Note: CLAUDE.md is manually curated and not auto-generated.");
Console.WriteLine("       Use STANDARDS.yaml as the authoritative reference.");
