#!/usr/bin/env dotnet-script
#r "nuget: YamlDotNet, 16.2.0"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// Get the repository root
// Use current working directory as starting point, go up 2 levels from tools/standards
string currentDir = Directory.GetCurrentDirectory();
string repoRoot = currentDir.Contains("tools/standards") || currentDir.Contains("tools\\standards")
    ? Path.GetFullPath(Path.Combine(currentDir, "..", ".."))
    : currentDir;

Console.WriteLine($"📂 Repository root: {repoRoot}");
Console.WriteLine($"📜 Current directory: {currentDir}");
Console.WriteLine();

// Load STANDARDS.yaml
string yamlPath = Path.Combine(repoRoot, "tools", "standards", "STANDARDS.yaml");
Console.WriteLine($"📖 Loading STANDARDS.yaml from: {yamlPath}");

if (!File.Exists(yamlPath)) {
    Console.WriteLine($"❌ ERROR: STANDARDS.yaml not found at {yamlPath}");
    Environment.Exit(1);
}

string yamlContent = File.ReadAllText(yamlPath);
IDeserializer deserializer = new DeserializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .Build();

dynamic standards = deserializer.Deserialize<dynamic>(yamlContent);

Console.WriteLine($"✅ Loaded STANDARDS.yaml version {standards["version"]}");
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
        sb.AppendLine($"{count}. ❌ **{rule["id"].Replace('_', ' ')}** - {desc}");
        count++;
    }
    
    sb.AppendLine();
    sb.AppendLine("## Always Required");
    sb.AppendLine("- ✅ Named parameters (non-obvious args)");
    sb.AppendLine("- ✅ Trailing commas (multi-line collections)");
    sb.AppendLine("- ✅ K&R brace style (same line)");
    sb.AppendLine("- ✅ File-scoped namespaces");
    sb.AppendLine("- ✅ Target-typed `new()`");
    sb.AppendLine("- ✅ Collection expressions `[]`");
    sb.AppendLine("- ✅ Result<T> for error handling");
    sb.AppendLine("- ✅ UnifiedOperation for polymorphic dispatch");
    sb.AppendLine("- ✅ E.* error registry");
    
    return sb.ToString();
}

// Helper to generate copilot-instructions.md IMMEDIATE BLOCKERS section
string GenerateImmediateBlockers() {
    StringBuilder sb = new();
    
    sb.AppendLine("## 🚫 IMMEDIATE BLOCKERS (Fix Before Proceeding)");
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
        
        sb.AppendLine($"{count}. ❌ **{(exampleWrong != "" ? exampleWrong : id.Replace('_', ' '))}** → {actionText}");
        count++;
    }
    
    // Add organizational limits
    sb.AppendLine($"{count}. ❌ **Folder has >{limits["files_per_folder"]["maximum"]} files** → Consolidate into {limits["files_per_folder"]["ideal"]} files");
    count++;
    sb.AppendLine($"{count}. ❌ **Folder has >{limits["types_per_folder"]["maximum"]} types** → Consolidate into {limits["types_per_folder"]["ideal"]} types");
    count++;
    sb.AppendLine($"{count}. ❌ **Member has >{limits["loc_per_member"]["maximum"]} LOC** → Improve algorithm, don't extract helpers");
    
    return sb.ToString();
}

// Generate CRITICAL RULES section for all agent files
Console.WriteLine("🔄 Updating [CRITICAL RULES] sections in agent files...");
string agentDir = Path.Combine(repoRoot, ".github", "agents");
string[] agentFiles = Directory.GetFiles(agentDir, "*.agent.md");

string criticalRulesSection = GenerateCriticalRulesSection();

int updatedAgents = 0;
foreach (string agentFile in agentFiles) {
    string content = File.ReadAllText(agentFile);
    
    // Find the [CRITICAL RULES] section
    int startIdx = content.IndexOf("# [CRITICAL RULES]");
    if (startIdx == -1) {
        Console.WriteLine($"⚠️  Skipping {Path.GetFileName(agentFile)} - no [CRITICAL RULES] section found");
        continue;
    }
    
    // Find the next section (starts with # [
    int endIdx = content.IndexOf("\n# [", startIdx + 1);
    if (endIdx == -1) {
        Console.WriteLine($"⚠️  Skipping {Path.GetFileName(agentFile)} - could not find end of section");
        continue;
    }
    
    // Replace the section
    string before = content.Substring(0, startIdx);
    string after = content.Substring(endIdx);
    string newContent = before + criticalRulesSection + after;
    
    File.WriteAllText(agentFile, newContent);
    Console.WriteLine($"   ✅ Updated {Path.GetFileName(agentFile)}");
    updatedAgents++;
}

Console.WriteLine($"✅ Updated {updatedAgents} agent files");
Console.WriteLine();

// Update copilot-instructions.md IMMEDIATE BLOCKERS section
Console.WriteLine("🔄 Updating copilot-instructions.md...");
string copilotPath = Path.Combine(repoRoot, ".github", "copilot-instructions.md");

if (File.Exists(copilotPath)) {
    string copilotContent = File.ReadAllText(copilotPath);
    
    int startIdx = copilotContent.IndexOf("## 🚫 IMMEDIATE BLOCKERS");
    if (startIdx != -1) {
        int endIdx = copilotContent.IndexOf("\n---", startIdx);
        if (endIdx != -1) {
            string before = copilotContent.Substring(0, startIdx);
            string after = copilotContent.Substring(endIdx);
            
            string newBlockers = GenerateImmediateBlockers();
            string newContent = before + newBlockers + "\n" + after;
            
            File.WriteAllText(copilotPath, newContent);
            Console.WriteLine("   ✅ Updated copilot-instructions.md");
        } else {
            Console.WriteLine("   ⚠️  Could not find end marker (---) in copilot-instructions.md");
        }
    } else {
        Console.WriteLine("   ⚠️  Could not find IMMEDIATE BLOCKERS section in copilot-instructions.md");
    }
} else {
    Console.WriteLine("   ⚠️  copilot-instructions.md not found");
}

Console.WriteLine();
Console.WriteLine("🎉 Standards generation complete!");
Console.WriteLine();
Console.WriteLine("📊 Summary:");
Console.WriteLine($"   - Updated {updatedAgents} agent files with synchronized [CRITICAL RULES]");
Console.WriteLine($"   - Updated copilot-instructions.md with IMMEDIATE BLOCKERS");
Console.WriteLine($"   - Source of truth: {yamlPath}");
Console.WriteLine();
Console.WriteLine("💡 Note: CLAUDE.md is manually curated and not auto-generated.");
Console.WriteLine("         Use STANDARDS.yaml as the authoritative reference.");
