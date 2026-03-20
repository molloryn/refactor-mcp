using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed record RazorGeneratedDocumentDescriptor(
    ProjectId ProjectId,
    string GeneratedFilePath,
    string PrimarySourcePath);

internal sealed record RazorSymbolResolution(ISymbol? Symbol, bool HasMappedCSharpTokenAtPosition);

internal sealed class RazorSolutionContext
{
    private readonly ImmutableArray<RazorGeneratedDocumentDescriptor> _generatedDocuments;
    private readonly ImmutableDictionary<string, ImmutableArray<RazorGeneratedDocumentDescriptor>> _generatedDocumentsBySourcePath;

    private RazorSolutionContext(
        ImmutableArray<RazorGeneratedDocumentDescriptor> generatedDocuments,
        ImmutableDictionary<string, ImmutableArray<RazorGeneratedDocumentDescriptor>> generatedDocumentsBySourcePath)
    {
        _generatedDocuments = generatedDocuments;
        _generatedDocumentsBySourcePath = generatedDocumentsBySourcePath;
    }

    internal bool HasRazorDocuments => !_generatedDocuments.IsEmpty;

    internal ImmutableArray<RazorGeneratedDocumentDescriptor> GeneratedDocuments => _generatedDocuments;

    internal ImmutableArray<RazorGeneratedDocumentDescriptor> GetGeneratedDocumentsForSource(string filePath)
    {
        var normalizedPath = RefactoringHelpers.NormalizePathForComparison(filePath);
        return _generatedDocumentsBySourcePath.TryGetValue(normalizedPath, out var descriptors)
            ? descriptors
            : [];
    }

    internal static async Task<RazorSolutionContext> CreateAsync(Solution solution, CancellationToken cancellationToken)
    {
        var generatedDocuments = ImmutableArray.CreateBuilder<RazorGeneratedDocumentDescriptor>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null)
                continue;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (!RazorGeneratedDocumentText.TryGetPrimarySourcePath(syntaxTree, cancellationToken, out var primarySourcePath))
                    continue;

                var sourceKind = RazorDocumentClassifier.Classify(primarySourcePath);
                if (!RazorDocumentClassifier.IsRazorSource(sourceKind))
                    continue;

                if (string.IsNullOrWhiteSpace(syntaxTree.FilePath))
                    continue;

                generatedDocuments.Add(new RazorGeneratedDocumentDescriptor(
                    project.Id,
                    syntaxTree.FilePath,
                    primarySourcePath));
            }
        }

        var generatedDocumentsBySourcePath = generatedDocuments
            .GroupBy(x => RefactoringHelpers.NormalizePathForComparison(x.PrimarySourcePath))
            .ToImmutableDictionary(
                x => x.Key,
                x => x.ToImmutableArray());

        return new RazorSolutionContext(generatedDocuments.ToImmutable(), generatedDocumentsBySourcePath);
    }

    internal async Task<RazorSymbolResolution> FindSymbolAsync(
        Solution solution,
        string filePath,
        string oldName,
        int? line,
        int? column,
        CancellationToken cancellationToken)
    {
        var generatedDocuments = GetGeneratedDocumentsForSource(filePath);
        if (generatedDocuments.IsEmpty)
            return new RazorSymbolResolution(null, false);

        var hasMappedCSharpTokenAtPosition = false;
        foreach (var descriptor in generatedDocuments)
        {
            var project = solution.GetProject(descriptor.ProjectId);
            if (project == null)
                continue;

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null)
                continue;

            var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(
                tree => RefactoringHelpers.PathEquals(tree.FilePath, descriptor.GeneratedFilePath));
            if (syntaxTree == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var model = compilation.GetSemanticModel(syntaxTree);

            foreach (var token in root.DescendantTokens())
            {
                var mappedSpan = token.GetLocation().GetMappedLineSpan();
                if (!RazorGeneratedDocumentText.IsMappedToFile(mappedSpan, filePath))
                    continue;

                if (line.HasValue && column.HasValue)
                {
                    if (!RazorGeneratedDocumentText.ContainsSourcePosition(mappedSpan, line.Value, column.Value))
                        continue;

                    var positionSymbol = RenameSymbolTool.GetSymbolFromNode(model, token.Parent);
                    if (positionSymbol == null)
                        continue;

                    hasMappedCSharpTokenAtPosition = true;
                    if (positionSymbol.Name == oldName)
                        return new RazorSymbolResolution(positionSymbol, true);

                    continue;
                }

                if (token.ValueText != oldName && token.Text != oldName)
                    continue;

                var symbol = RenameSymbolTool.GetSymbolFromNode(model, token.Parent);
                if (symbol != null && symbol.Name == oldName)
                    return new RazorSymbolResolution(symbol, false);
            }
        }

        return new RazorSymbolResolution(null, hasMappedCSharpTokenAtPosition);
    }
}
