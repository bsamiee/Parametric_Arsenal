using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

internal static class Program {
    private static async Task<int> Main() {
        Console.WriteLine("[INFO] ContextGen starting...");

        // Register MSBuild before creating workspace
        // Note: Return value (VisualStudioInstance) not needed for basic MSBuild operations
        _ = MSBuildLocator.RegisterDefaults();

        // Get repository root - navigate from current directory
        string currentDir = Directory.GetCurrentDirectory();
        string baseDir = Path.GetFullPath(Path.Combine(currentDir, "..", ".."));
        string solutionPath = Path.Combine(baseDir, "Parametric_Arsenal.sln");
        string outputDir = Path.Combine(baseDir, "docs", "agent-context");

        Console.WriteLine($"[INFO] Base directory: {baseDir}");
        Console.WriteLine($"[INFO] Solution: {solutionPath}");
        Console.WriteLine($"[INFO] Output directory: {outputDir}");

        Directory.CreateDirectory(outputDir);

        JsonSerializerOptions jsonOptions = new() { WriteIndented = true, };

        try {
            // Open solution
            Console.WriteLine("[INFO] Opening solution with Roslyn...");
            using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
            Solution solution = await workspace.OpenSolutionAsync(solutionPath);
            Console.WriteLine($"[PASS] Solution opened: {solution.Projects.Count()} projects");

            // Generate architecture.json
            Console.WriteLine("[INFO] Generating architecture.json...");
            await GenerateArchitectureJson(solution, outputDir, jsonOptions);
            Console.WriteLine("[PASS] architecture.json generated");

            // Generate error-catalog.json
            Console.WriteLine("[INFO] Generating error-catalog.json...");
            await GenerateErrorCatalogJson(baseDir, outputDir, jsonOptions);
            Console.WriteLine("[PASS] error-catalog.json generated");

            // Generate validation-modes.json
            Console.WriteLine("[INFO] Generating validation-modes.json...");
            await GenerateValidationModesJson(baseDir, outputDir, jsonOptions);
            Console.WriteLine("[PASS] validation-modes.json generated");

            // Generate exemplar-metrics.json
            Console.WriteLine("[INFO] Generating exemplar-metrics.json...");
            await GenerateExemplarMetricsJson(baseDir, outputDir, jsonOptions);
            Console.WriteLine("[PASS] exemplar-metrics.json generated");

            // Generate domain-map.json
            Console.WriteLine("[INFO] Generating domain-map.json...");
            await GenerateDomainMapJson(baseDir, outputDir, jsonOptions);
            Console.WriteLine("[PASS] domain-map.json generated");

            Console.WriteLine("[PASS] ContextGen completed successfully");
        } catch (Exception ex) {
            Console.WriteLine($"[ERROR] {ex.Message}");
            Console.WriteLine($"[ERROR] {ex.StackTrace}");
            return 1;
        }

        return 0;
    }

    private static async Task GenerateArchitectureJson(Solution solution, string outputDir, JsonSerializerOptions jsonOptions) {
        List<object> projects = [];

        foreach (Project project in solution.Projects.Where(p => p.Name.StartsWith("Arsenal", StringComparison.Ordinal) || p.Name.Contains("Core", StringComparison.Ordinal) || p.Name.Contains("Rhino", StringComparison.Ordinal) || p.Name.Contains("Grasshopper", StringComparison.Ordinal))) {
            Compilation? compilation = await project.GetCompilationAsync();
            if (compilation is null) {
                continue;
            }

            // Only get types from source files in THIS project, not referenced assemblies
            INamedTypeSymbol[] types = [.. compilation.SyntaxTrees
                .SelectMany(tree => {
                    SemanticModel model = compilation.GetSemanticModel(tree);
                    return tree.GetRoot()
                        .DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                        .Select(typeDeclaration => model.GetDeclaredSymbol(typeDeclaration))
                        .OfType<INamedTypeSymbol>();
                })
                .Where(t => t.ContainingNamespace.ToDisplayString().StartsWith("Arsenal", StringComparison.Ordinal)),];

            projects.Add(new {
                Name = project.AssemblyName ?? project.Name,
                Path = project.FilePath ?? "",
                Namespace = types.FirstOrDefault()?.ContainingNamespace.ToDisplayString() ?? "",
                TypeCount = types.Length,
                Types = types.Select(t => new {
                    t.Name,
                    Namespace = t.ContainingNamespace.ToDisplayString(),
                    Kind = t.TypeKind.ToString(),
                    MemberCount = t.GetMembers().Length,
                    IsPublic = t.DeclaredAccessibility == Accessibility.Public,
                }).ToArray(),
            });
        }

        string json = JsonSerializer.Serialize(projects, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "architecture.json"), json);
    }

    private static async Task GenerateErrorCatalogJson(string baseDir, string outputDir, JsonSerializerOptions jsonOptions) {
        string ecsPath = Path.Combine(baseDir, "libs", "core", "errors", "E.cs");

        if (!File.Exists(ecsPath)) {
            Console.WriteLine($"[WARN] E.cs not found at {ecsPath}, skipping error catalog");
            return;
        }

        string sourceCode = await File.ReadAllTextAsync(ecsPath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        ClassDeclarationSyntax? eClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => string.Equals(c.Identifier.Text, "E", StringComparison.Ordinal));

        if (eClass is null) {
            Console.WriteLine("[WARN] E class not found in E.cs");
            return;
        }

        Dictionary<string, List<object>> catalog = [];

        foreach (ClassDeclarationSyntax domain in eClass.Members.OfType<ClassDeclarationSyntax>()) {
            string domainName = domain.Identifier.Text;
            List<object> errors = [.. domain.Members
                .OfType<FieldDeclarationSyntax>()
                .Where(f => f.Declaration.Type.ToString().Contains("SystemError", StringComparison.Ordinal))
                .Select(f => new {
                    Name = f.Declaration.Variables.First().Identifier.Text,
                    Declaration = f.Declaration.Type.ToString(),
                }),];

            if (errors.Count > 0) {
                catalog[domainName] = errors;
            }
        }

        string json = JsonSerializer.Serialize(catalog, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "error-catalog.json"), json);
    }

    private static async Task GenerateValidationModesJson(string baseDir, string outputDir, JsonSerializerOptions jsonOptions) {
        string vPath = Path.Combine(baseDir, "libs", "core", "validation", "V.cs");

        if (!File.Exists(vPath)) {
            Console.WriteLine($"[WARN] V.cs not found at {vPath}, creating minimal validation modes");
            object minimalModes = new {
                Flags = new[] {
                    new { Name = "None", Value = 0 },
                    new { Name = "Standard", Value = 1 },
                    new { Name = "Degeneracy", Value = 2 },
                    new { Name = "Topology", Value = 4 },
                    new { Name = "BoundingBox", Value = 8 },
                    new { Name = "All", Value = 15 },
                },
            };
            string json = JsonSerializer.Serialize(minimalModes, jsonOptions);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "validation-modes.json"), json);
            return;
        }

        string sourceCode = await File.ReadAllTextAsync(vPath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        // V is a struct with static readonly fields, not an enum
        StructDeclarationSyntax? vStruct = root.DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .FirstOrDefault(s => string.Equals(s.Identifier.Text, "V", StringComparison.Ordinal));

        if (vStruct is null) {
            Console.WriteLine("[WARN] V struct not found in V.cs");
            return;
        }

        // Extract static readonly V fields with their values
        // First pass: collect all non-All flags
        List<(string Name, string Value)> flagValues = [];
        foreach (FieldDeclarationSyntax field in vStruct.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(f => f.Modifiers.Any(m => string.Equals(m.Text, "static", StringComparison.Ordinal))
                     && f.Modifiers.Any(m => string.Equals(m.Text, "readonly", StringComparison.Ordinal))
                     && string.Equals(f.Declaration.Type.ToString(), "V", StringComparison.Ordinal))) {
            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables) {
                string name = variable.Identifier.Text;
                string value = string.Equals(name, "All", StringComparison.Ordinal) ? "" : ExtractValue(variable);
                flagValues.Add((name, value));
            }
        }
        // Second pass: calculate 'All' as bitwise OR of collected flags
        int allValue = flagValues.Where(f => !string.Equals(f.Name, "All", StringComparison.Ordinal))
            .Sum(f => int.Parse(s: f.Value, provider: null));
        // Build final list
        List<object> flags = [.. flagValues.Select(f =>
            string.Equals(f.Name, "All", StringComparison.Ordinal)
                ? new { f.Name, Value = allValue.ToString(provider: null), }
                : new { f.Name, f.Value, }),];

        static string ExtractValue(VariableDeclaratorSyntax variable) {
            SyntaxToken valueToken = variable.Initializer?.Value?.DescendantTokens()
                .FirstOrDefault(t => int.TryParse(s: t.Text, provider: null, out _)) ?? default;
            return !string.IsNullOrEmpty(valueToken.Text) ? valueToken.Text : "0";
        }

        object modes = new { Flags = flags, };
        string validationJson = JsonSerializer.Serialize(modes, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "validation-modes.json"), validationJson);
    }

    private static async Task GenerateExemplarMetricsJson(string baseDir, string outputDir, JsonSerializerOptions jsonOptions) {
        string[] exemplarPaths = [
            "libs/core/validation/ValidationRules.cs",
            "libs/core/results/ResultFactory.cs",
            "libs/core/operations/UnifiedOperation.cs",
            "libs/core/results/Result.cs",
            "libs/rhino/spatial/Spatial.cs",
        ];

        List<object> metrics = [];

        foreach (string relativePath in exemplarPaths) {
            string fullPath = Path.Combine(baseDir, relativePath);
            if (!File.Exists(fullPath)) {
                Console.WriteLine($"[WARN] Exemplar not found: {relativePath}");
                continue;
            }

            string[] lines = await File.ReadAllLinesAsync(fullPath);
            int loc = lines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.Trim().StartsWith("//", StringComparison.Ordinal));

            metrics.Add(new {
                Path = relativePath,
                LOC = loc,
                TotalLines = lines.Length,
            });
        }

        string json = JsonSerializer.Serialize(metrics, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "exemplar-metrics.json"), json);
    }

    private static async Task GenerateDomainMapJson(string baseDir, string outputDir, JsonSerializerOptions jsonOptions) {
        string libsRhinoPath = Path.Combine(baseDir, "libs", "rhino");

        if (!Directory.Exists(libsRhinoPath)) {
            Console.WriteLine($"[WARN] libs/rhino not found at {libsRhinoPath}");
            return;
        }

        List<object> domains = [];

        foreach (string domainDir in Directory.GetDirectories(libsRhinoPath)) {
            string domainName = Path.GetFileName(domainDir);
            string[] csFiles = Directory.GetFiles(domainDir, "*.cs");

            domains.Add(new {
                Name = domainName,
                Path = $"libs/rhino/{domainName}",
                FileCount = csFiles.Length,
                Files = csFiles.Select(Path.GetFileName).ToArray(),
            });
        }

        string json = JsonSerializer.Serialize(domains, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "domain-map.json"), json);
    }
}
