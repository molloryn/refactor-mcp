using ModelContextProtocol.Server;
using ModelContextProtocol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.FindSymbols;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

[McpServerToolType]
public static class RenameSymbolTool
{
    [McpServerTool, Description("Rename a symbol across the solution using Roslyn")]
    public static async Task<string> RenameSymbol(
        [Description("Absolute path to the solution file (.sln or .slnx)")] string solutionPath,
        [Description("Path to the C# file containing the symbol")] string filePath,
        [Description("Current name of the symbol")] string oldName,
        [Description("New name for the symbol")] string newName,
        [Description("Line number of the symbol (1-based, optional)")] int? line = null,
        [Description("Column number of the symbol (1-based, optional)")] int? column = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            solutionPath = RefactoringHelpers.ResolveSolutionPath(solutionPath);
            var solution = await RefactoringHelpers.GetOrLoadSolution(solutionPath, cancellationToken);
            var razorContext = await RefactoringHelpers.GetOrLoadRazorSolutionContext(solutionPath, cancellationToken);
            var fileKind = RazorDocumentClassifier.Classify(filePath);

            if (fileKind is RazorDocumentKind.RazorImports or RazorDocumentKind.RazorViewImports)
                throw new McpException($"Error: RenameSymbol does not support starting from '{Path.GetFileName(filePath)}' in Phase 1");

            ISymbol? symbol;
            if (RazorDocumentClassifier.IsSupportedRenameEntryPoint(fileKind))
            {
                var razorResolution = await razorContext.FindSymbolAsync(
                    solution,
                    filePath,
                    oldName,
                    line,
                    column,
                    cancellationToken);

                if (razorResolution.Symbol == null &&
                    line.HasValue &&
                    column.HasValue &&
                    !razorResolution.HasMappedCSharpTokenAtPosition)
                {
                    throw new McpException(
                        "Error: RenameSymbol does not support the selected Razor span in Phase 1. Select a C#-backed identifier.");
                }

                symbol = razorResolution.Symbol;
            }
            else
            {
                var document = RefactoringHelpers.GetDocumentByPath(solution, filePath);
                if (document == null)
                    throw new McpException($"Error: File {filePath} not found in solution");

                symbol = await FindSymbol(document, oldName, line, column, cancellationToken);
            }

            if (symbol == null)
                throw new McpException($"Error: Symbol '{oldName}' not found");

            var options = new SymbolRenameOptions();
            var razorProjectedChanges = razorContext.HasRazorDocuments
                ? await RazorSourceMappingService.CreateChangeSetAsync(
                    symbol,
                    solution,
                    oldName,
                    newName,
                    cancellationToken)
                : new RazorProjectedChangeSet(
                    ImmutableDictionary<string, ImmutableArray<RazorProjectedEdit>>.Empty);

            var renamed = await Renamer.RenameSymbolAsync(solution, symbol, options, newName, cancellationToken);
            var changedDocuments = await CollectChangedDocumentsAsync(solution, renamed, cancellationToken);

            foreach (var changedDocument in changedDocuments)
            {
                await RefactoringHelpers.WriteFileWithEncodingAsync(
                    changedDocument.FilePath,
                    changedDocument.UpdatedText,
                    changedDocument.Encoding,
                    cancellationToken);

                if (!razorProjectedChanges.HasEdits)
                    RefactoringHelpers.UpdateSolutionCache(changedDocument.Document);
            }

            if (razorProjectedChanges.HasEdits)
            {
                await RazorSourceMappingService.ApplyProjectedChangesAsync(razorProjectedChanges, cancellationToken);
                RefactoringHelpers.InvalidateSolutionCaches(solutionPath);
            }

            return $"Successfully renamed '{oldName}' to '{newName}'";
        }
        catch (Exception ex)
        {
            throw new McpException($"Error renaming symbol: {ex.Message}", ex);
        }
    }

    internal static async Task<ISymbol?> FindSymbol(Document document, string name, int? line, int? column, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken);
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (model == null || root == null)
            return null;

        if (line.HasValue && column.HasValue)
        {
            var text = await document.GetTextAsync(cancellationToken);
            if (line.Value > 0 && line.Value <= text.Lines.Count && column.Value > 0)
            {
                var pos = text.Lines[line.Value - 1].Start + column.Value - 1;
                var symbolAtPosition = GetSymbolFromNode(model, root.FindToken(pos).Parent);
                if (symbolAtPosition != null && symbolAtPosition.Name == name)
                    return symbolAtPosition;
            }
        }

        foreach (var token in root.DescendantTokens().Where(t => t.ValueText == name || t.Text == name))
        {
            var symbolInDocument = GetSymbolFromNode(model, token.Parent);
            if (symbolInDocument != null && symbolInDocument.Name == name)
                return symbolInDocument;
        }

        var decls = await SymbolFinder.FindDeclarationsAsync(document.Project, name, false, cancellationToken);
        return decls.FirstOrDefault();
    }

    internal static ISymbol? GetSymbolFromNode(SemanticModel model, SyntaxNode? node)
    {
        while (node != null)
        {
            var symbol = model.GetDeclaredSymbol(node) ?? model.GetSymbolInfo(node).Symbol;
            if (symbol != null)
                return symbol;

            node = node.Parent;
        }

        return null;
    }

    private static async Task<IReadOnlyList<ChangedDocumentWriteback>> CollectChangedDocumentsAsync(
        Solution originalSolution,
        Solution renamedSolution,
        CancellationToken cancellationToken)
    {
        var writebacks = new List<ChangedDocumentWriteback>();
        var changes = renamedSolution.GetChanges(originalSolution);
        foreach (var projectChange in changes.GetProjectChanges())
        {
            foreach (var id in projectChange.GetChangedDocuments())
            {
                var newDoc = renamedSolution.GetDocument(id);
                if (newDoc?.FilePath == null)
                    continue;

                var text = await newDoc.GetTextAsync(cancellationToken);
                var (_, encoding) = await RefactoringHelpers.ReadFileWithEncodingAsync(newDoc.FilePath, cancellationToken);
                writebacks.Add(new ChangedDocumentWriteback(newDoc, newDoc.FilePath, text.ToString(), encoding));
            }
        }

        return writebacks;
    }

    private sealed record ChangedDocumentWriteback(Document Document, string FilePath, string UpdatedText, Encoding Encoding);
}
