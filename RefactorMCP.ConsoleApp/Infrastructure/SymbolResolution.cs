using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

internal static class SymbolResolution
{
    internal static async Task<ISymbol?> FindSymbolAsync(
        Document document,
        string name,
        int? line,
        int? column,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (semanticModel == null || root == null)
        {
            return null;
        }

        if (line.HasValue && column.HasValue)
        {
            var text = await document.GetTextAsync(cancellationToken);
            if (line.Value > 0 &&
                line.Value <= text.Lines.Count &&
                column.Value > 0 &&
                column.Value <= text.Lines[line.Value - 1].Span.Length + 1)
            {
                var position = text.Lines[line.Value - 1].Start + column.Value - 1;
                var symbolAtPosition = GetSymbolFromNode(semanticModel, root.FindToken(position).Parent);
                if (symbolAtPosition != null && symbolAtPosition.Name == name)
                {
                    return symbolAtPosition;
                }
            }
        }

        foreach (var token in root.DescendantTokens().Where(token => token.ValueText == name || token.Text == name))
        {
            var symbolInDocument = GetSymbolFromNode(semanticModel, token.Parent);
            if (symbolInDocument != null && symbolInDocument.Name == name)
            {
                return symbolInDocument;
            }
        }

        var declarations = await SymbolFinder.FindDeclarationsAsync(document.Project, name, false, cancellationToken);
        return declarations.FirstOrDefault();
    }

    internal static ISymbol? GetSymbolFromNode(SemanticModel semanticModel, SyntaxNode? node)
    {
        while (node != null)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node) ?? semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol != null)
            {
                return symbol;
            }

            node = node.Parent;
        }

        return null;
    }
}
