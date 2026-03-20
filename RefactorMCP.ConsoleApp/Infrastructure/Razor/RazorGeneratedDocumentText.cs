using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.IO;
using System.Threading;

internal static class RazorGeneratedDocumentText
{
    internal static bool TryGetPrimarySourcePath(
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken,
        out string primarySourcePath)
    {
        primarySourcePath = string.Empty;

        var text = syntaxTree.GetText(cancellationToken);
        var lineCount = Math.Min(text.Lines.Count, 5);
        for (var i = 0; i < lineCount; i++)
        {
            var lineText = text.Lines[i].ToString();
            if (!lineText.StartsWith("#pragma checksum \"", StringComparison.Ordinal))
                continue;

            var start = "#pragma checksum \"".Length;
            var end = lineText.IndexOf('"', start);
            if (end <= start)
                continue;

            primarySourcePath = lineText[start..end];
            return true;
        }

        return false;
    }

    internal static bool IsMappedToFile(FileLinePositionSpan mappedSpan, string filePath)
    {
        if (!mappedSpan.HasMappedPath || string.IsNullOrWhiteSpace(mappedSpan.Path))
            return false;

        return RefactoringHelpers.PathEquals(mappedSpan.Path, filePath);
    }

    internal static bool ContainsSourcePosition(FileLinePositionSpan mappedSpan, int line, int column)
    {
        if (line <= 0 || column <= 0)
            return false;

        var zeroBasedLine = line - 1;
        var zeroBasedColumn = column - 1;

        var start = mappedSpan.Span.Start;
        var end = mappedSpan.Span.End;

        if (zeroBasedLine < start.Line || zeroBasedLine > end.Line)
            return false;

        if (zeroBasedLine == start.Line && zeroBasedColumn < start.Character)
            return false;

        if (zeroBasedLine == end.Line && zeroBasedColumn > end.Character)
            return false;

        return true;
    }

    internal static TextSpan ToTextSpan(SourceText sourceText, FileLinePositionSpan mappedSpan)
    {
        var start = sourceText.Lines[mappedSpan.Span.Start.Line].Start + mappedSpan.Span.Start.Character;
        var end = sourceText.Lines[mappedSpan.Span.End.Line].Start + mappedSpan.Span.End.Character;
        return TextSpan.FromBounds(start, end);
    }
}
