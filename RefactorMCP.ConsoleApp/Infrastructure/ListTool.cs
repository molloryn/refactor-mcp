using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public static class ListTools
{
    [McpServerTool, Description("List all available refactoring tools")]
    public static string ListToolsCommand()
    {
        var toolNames = McpServerFeatureCatalog.GetToolNames();
        return string.Join('\n', toolNames);
    }
}
