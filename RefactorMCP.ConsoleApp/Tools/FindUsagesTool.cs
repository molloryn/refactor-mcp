using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public static class FindUsagesTool
{
    [McpServerTool, Description("Find usages of a C# symbol across the solution")]
    public static async Task<FindUsagesResult> FindUsages(
        [Description(RefactoringHelpers.SolutionPathDescription)] string solutionPath,
        [Description("Path to the C# file containing the symbol")] string filePath,
        [Description("Name of the symbol to inspect")] string symbolName,
        [Description("Line number of the symbol (1-based, optional)")] int? line = null,
        [Description("Column number of the symbol (1-based, optional)")] int? column = null,
        [Description("Maximum number of reference locations to return")] int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (maxResults <= 0)
            {
                throw new McpException("Error: maxResults must be greater than zero.");
            }

            solutionPath = RefactoringHelpers.ResolveSolutionPath(solutionPath);
            if (!string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException(
                    $"Error: FindUsages currently supports only C# source files. '{Path.GetFileName(filePath)}' is not supported.");
            }

            var solution = await RefactoringHelpers.GetOrLoadSolution(solutionPath, cancellationToken);
            var document = RefactoringHelpers.GetDocumentByPath(solution, filePath);
            if (document == null)
            {
                throw new McpException($"Error: File {filePath} not found in solution");
            }

            var symbol = await SymbolResolution.FindSymbolAsync(document, symbolName, line, column, cancellationToken);
            if (symbol == null)
            {
                throw new McpException($"Error: Symbol '{symbolName}' not found");
            }

            symbol = await SymbolFinder.FindSourceDefinitionAsync(symbol, solution, cancellationToken) ?? symbol;

            var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
            var declarations = await CollectDeclarationLocationsAsync(referencedSymbols, solution, cancellationToken);
            var references = await CollectReferenceLocationsAsync(referencedSymbols, cancellationToken);

            var orderedReferences = references
                .OrderBy(location => RefactoringHelpers.NormalizePathForComparison(location.FilePath))
                .ThenBy(location => location.Line)
                .ThenBy(location => location.Column)
                .ToList();

            var returnedReferences = orderedReferences.Take(maxResults).ToArray();

            return new FindUsagesResult
            {
                SymbolName = symbol.Name,
                SymbolKind = symbol.Kind.ToString(),
                DisplayName = symbol.ToDisplayString(),
                ContainingSymbol = symbol.ContainingSymbol?.ToDisplayString(),
                TotalReferenceCount = orderedReferences.Count,
                ReturnedReferenceCount = returnedReferences.Length,
                IsTruncated = orderedReferences.Count > returnedReferences.Length,
                Declarations = declarations,
                References = returnedReferences
            };
        }
        catch (Exception ex) when (ex is not McpException)
        {
            throw new McpException($"Error finding usages: {ex.Message}", ex);
        }
    }

    private static async Task<IReadOnlyList<FindUsageLocation>> CollectDeclarationLocationsAsync(
        IEnumerable<ReferencedSymbol> referencedSymbols,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var seenLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declarations = new List<FindUsageLocation>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Definition.Locations.Where(location => location.IsInSource))
            {
                var usage = await TryCreateLocationAsync(location, solution, cancellationToken);
                if (usage == null || !seenLocations.Add(CreateLocationKey(usage)))
                {
                    continue;
                }

                declarations.Add(usage);
            }
        }

        return declarations
            .OrderBy(location => RefactoringHelpers.NormalizePathForComparison(location.FilePath))
            .ThenBy(location => location.Line)
            .ThenBy(location => location.Column)
            .ToArray();
    }

    private static async Task<IReadOnlyList<FindUsageLocation>> CollectReferenceLocationsAsync(
        IEnumerable<ReferencedSymbol> referencedSymbols,
        CancellationToken cancellationToken)
    {
        var seenLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<FindUsageLocation>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var usage = await TryCreateLocationAsync(location, cancellationToken);
                if (usage == null || !seenLocations.Add(CreateLocationKey(usage)))
                {
                    continue;
                }

                references.Add(usage);
            }
        }

        return references;
    }

    private static async Task<FindUsageLocation?> TryCreateLocationAsync(
        Location location,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var lineSpan = location.GetLineSpan();
        var filePath = lineSpan.Path;
        if (string.IsNullOrWhiteSpace(filePath) && location.SourceTree != null)
        {
            filePath = location.SourceTree.FilePath;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !lineSpan.IsValid)
        {
            return null;
        }

        var lineText = string.Empty;
        if (location.SourceTree != null)
        {
            var sourceText = await location.SourceTree.GetTextAsync(cancellationToken);
            lineText = GetLineText(sourceText, lineSpan.StartLinePosition.Line);
        }
        else
        {
            var document = solution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(document => RefactoringHelpers.PathEquals(document.FilePath, filePath));

            if (document != null)
            {
                var sourceText = await document.GetTextAsync(cancellationToken);
                lineText = GetLineText(sourceText, lineSpan.StartLinePosition.Line);
            }
        }

        return new FindUsageLocation
        {
            FilePath = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            LineText = lineText
        };
    }

    private static async Task<FindUsageLocation?> TryCreateLocationAsync(
        ReferenceLocation location,
        CancellationToken cancellationToken)
    {
        var filePath = location.Document.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var lineSpan = location.Location.GetLineSpan();
        if (!lineSpan.IsValid)
        {
            return null;
        }

        var sourceText = await location.Document.GetTextAsync(cancellationToken);
        return new FindUsageLocation
        {
            FilePath = filePath,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            LineText = GetLineText(sourceText, lineSpan.StartLinePosition.Line)
        };
    }

    private static string CreateLocationKey(FindUsageLocation location)
    {
        return $"{RefactoringHelpers.NormalizePathForComparison(location.FilePath)}:{location.Line}:{location.Column}";
    }

    private static string GetLineText(SourceText sourceText, int lineIndex)
    {
        return lineIndex >= 0 && lineIndex < sourceText.Lines.Count
            ? sourceText.Lines[lineIndex].ToString().Trim()
            : string.Empty;
    }
}

public sealed class FindUsagesResult
{
    public string SymbolName { get; init; } = string.Empty;
    public string SymbolKind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? ContainingSymbol { get; init; }
    public int TotalReferenceCount { get; init; }
    public int ReturnedReferenceCount { get; init; }
    public bool IsTruncated { get; init; }
    public IReadOnlyList<FindUsageLocation> Declarations { get; init; } = [];
    public IReadOnlyList<FindUsageLocation> References { get; init; } = [];
}

public sealed class FindUsageLocation
{
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string LineText { get; init; } = string.Empty;
}
