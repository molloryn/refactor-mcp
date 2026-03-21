using ModelContextProtocol.Server;
using System.Reflection;
using System.Text;

internal enum McpFeatureMode
{
    Basic,
    Advanced
}

internal readonly record struct McpCommandLineOptions(
    McpFeatureMode Mode,
    bool IsJsonMode,
    string[] RemainingArgs)
{
    internal static McpCommandLineOptions Parse(string[] args)
    {
        var remainingArgs = new List<string>(args.Length);
        var mode = McpFeatureMode.Basic;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--advanced", StringComparison.OrdinalIgnoreCase))
            {
                mode = McpFeatureMode.Advanced;
                continue;
            }

            remainingArgs.Add(arg);
        }

        var isJsonMode = remainingArgs.Count > 0 &&
                         string.Equals(remainingArgs[0], "--json", StringComparison.OrdinalIgnoreCase);

        return new McpCommandLineOptions(mode, isJsonMode, remainingArgs.ToArray());
    }
}

internal static class McpServerFeatureCatalog
{
    private static readonly Assembly _assembly = typeof(LoadSolutionTool).Assembly;
    private static readonly IReadOnlyList<Type> _basicToolTypes =
    [
        typeof(LoadSolutionTool),
        typeof(AsyncSolutionLoadTool),
        typeof(UnloadSolutionTool),
        typeof(VersionTool),
        typeof(ListTools),
        typeof(RenameSymbolTool),
        typeof(FindUsagesTool),
        typeof(MoveTypeToFileTool)
    ];

    private static McpFeatureMode _currentMode = McpFeatureMode.Basic;

    internal static McpFeatureMode CurrentMode => _currentMode;

    internal static void Configure(McpFeatureMode mode)
    {
        _currentMode = mode;
    }

    internal static IReadOnlyList<Type> GetToolTypes(McpFeatureMode? mode = null)
    {
        return ResolveMode(mode) == McpFeatureMode.Advanced
            ? GetAttributedTypes<McpServerToolTypeAttribute>()
            : _basicToolTypes;
    }

    internal static IReadOnlyList<Type> GetPromptTypes(McpFeatureMode? mode = null)
    {
        return ResolveMode(mode) == McpFeatureMode.Advanced
            ? GetAttributedTypes<McpServerPromptTypeAttribute>()
            : [];
    }

    internal static IReadOnlyList<Type> GetResourceTypes(McpFeatureMode? mode = null)
    {
        return ResolveMode(mode) == McpFeatureMode.Advanced
            ? GetAttributedTypes<McpServerResourceTypeAttribute>()
            : [];
    }

    internal static IReadOnlyList<MethodInfo> GetToolMethods(McpFeatureMode? mode = null)
    {
        return GetToolTypes(mode)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Any())
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static MethodInfo? FindToolMethod(string toolName, McpFeatureMode? mode = null)
    {
        return GetToolMethods(mode)
            .FirstOrDefault(method => GetToolLookupNames(method)
                .Any(name => name.Equals(toolName, StringComparison.OrdinalIgnoreCase)));
    }

    internal static bool IsKnownTool(string toolName)
    {
        return GetToolMethods(McpFeatureMode.Advanced)
            .Any(method => GetToolLookupNames(method)
                .Any(name => name.Equals(toolName, StringComparison.OrdinalIgnoreCase)));
    }

    internal static IReadOnlyList<string> GetToolNames(McpFeatureMode? mode = null)
    {
        return GetToolMethods(mode)
            .Select(GetPublicToolName)
            .ToArray();
    }

    internal static string BuildDisabledToolMessage(string toolName)
    {
        return $"Tool '{toolName}' is disabled in default mode. Restart the MCP server with --advanced to enable it.";
    }

    internal static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static IEnumerable<string> GetToolLookupNames(MethodInfo method)
    {
        yield return method.Name;

        if (method.Name.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
        {
            yield return method.Name[..^"Command".Length];
        }

        yield return GetPublicToolName(method);
    }

    private static string GetPublicToolName(MethodInfo method)
    {
        var methodName = method.Name.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
            ? method.Name[..^"Command".Length]
            : method.Name;
        return ToKebabCase(methodName);
    }

    private static IReadOnlyList<Type> GetAttributedTypes<TAttribute>()
        where TAttribute : Attribute
    {
        return _assembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(TAttribute), false).Any())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static McpFeatureMode ResolveMode(McpFeatureMode? mode)
    {
        return mode ?? _currentMode;
    }
}
