using System;
using System.IO;

internal enum RazorDocumentKind
{
    Unknown,
    CSharp,
    RazorComponent,
    RazorView,
    RazorCodeBehind,
    RazorViewCodeBehind,
    RazorImports,
    RazorViewImports
}

internal static class RazorDocumentClassifier
{
    internal static RazorDocumentKind Classify(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return RazorDocumentKind.Unknown;

        var fileName = Path.GetFileName(filePath);

        if (filePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.RazorCodeBehind;

        if (filePath.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.RazorViewCodeBehind;

        if (string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.CSharp;

        if (string.Equals(fileName, "_Imports.razor", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.RazorImports;

        if (string.Equals(fileName, "_ViewImports.cshtml", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.RazorViewImports;

        if (string.Equals(Path.GetExtension(filePath), ".razor", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.RazorComponent;

        if (string.Equals(Path.GetExtension(filePath), ".cshtml", StringComparison.OrdinalIgnoreCase))
            return RazorDocumentKind.RazorView;

        return RazorDocumentKind.Unknown;
    }

    internal static bool IsRazorSource(RazorDocumentKind kind) =>
        kind is RazorDocumentKind.RazorComponent
            or RazorDocumentKind.RazorView
            or RazorDocumentKind.RazorImports
            or RazorDocumentKind.RazorViewImports;

    internal static bool IsSupportedRenameEntryPoint(RazorDocumentKind kind) =>
        kind is RazorDocumentKind.RazorComponent or RazorDocumentKind.RazorView;
}
