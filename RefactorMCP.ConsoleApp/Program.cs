using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("RefactorMCP.Tests")]

var commandLine = McpCommandLineOptions.Parse(args);
McpServerFeatureCatalog.Configure(commandLine.Mode);

if (commandLine.IsJsonMode)
{
    await RunJsonMode(commandLine.RemainingArgs, commandLine.Mode);
    return;
}

var builder = Host.CreateApplicationBuilder(commandLine.RemainingArgs);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr.
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();

RegisterServerFeatures(mcpBuilder, commandLine.Mode);

await builder.Build().RunAsync();

static void RegisterServerFeatures(IMcpServerBuilder builder, McpFeatureMode mode)
{
    builder.WithTools(McpServerFeatureCatalog.GetToolTypes(mode));

    var promptTypes = McpServerFeatureCatalog.GetPromptTypes(mode);
    if (promptTypes.Count > 0)
    {
        builder.WithPrompts(promptTypes);
    }

    var resourceTypes = McpServerFeatureCatalog.GetResourceTypes(mode);
    if (resourceTypes.Count > 0)
    {
        builder.WithResources(resourceTypes);
    }
}

static async Task RunJsonMode(string[] args, McpFeatureMode mode)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: --json <ToolName> '{\"param\":\"value\"}'");
        return;
    }

    ToolCallLogger.RestoreFromEnvironment();

    var toolName = args[1];
    var json = string.Join(" ", args.Skip(2));
    Dictionary<string, JsonElement>? paramDict;
    try
    {
        paramDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (paramDict == null)
        {
            Console.WriteLine("Error: Failed to parse parameters");
            return;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error parsing JSON: {ex.Message}");
        return;
    }

    var method = McpServerFeatureCatalog.FindToolMethod(toolName, mode);
    if (method == null)
    {
        var message = McpServerFeatureCatalog.IsKnownTool(toolName)
            ? McpServerFeatureCatalog.BuildDisabledToolMessage(toolName)
            : $"Unknown tool: {toolName}. Use the ListTools tool to see available commands.";
        Console.WriteLine(message);
        return;
    }

    var parameters = method.GetParameters();
    var invokeArgs = new object?[parameters.Length];
    var rawValues = new Dictionary<string, string?>();
    for (var index = 0; index < parameters.Length; index++)
    {
        var parameter = parameters[index];
        if (paramDict.TryGetValue(parameter.Name!, out var value))
        {
            rawValues[parameter.Name!] = value.ToString();
            invokeArgs[index] = value.ValueKind == JsonValueKind.String
                ? ConvertInput(value.GetString()!, parameter.ParameterType)
                : value.Deserialize(parameter.ParameterType, new JsonSerializerOptions());
        }
        else if (parameter.HasDefaultValue)
        {
            rawValues[parameter.Name!] = null;
            invokeArgs[index] = parameter.DefaultValue;
        }
        else
        {
            Console.WriteLine($"Error: Missing required parameter '{parameter.Name}'");
            return;
        }
    }

    try
    {
        var result = method.Invoke(null, invokeArgs);
        if (result is Task task)
        {
            await task;
            if (task.GetType().IsGenericType)
            {
                WriteResult(task.GetType().GetProperty("Result")?.GetValue(task));
            }
            else
            {
                Console.WriteLine("Done");
            }
        }
        else if (result != null)
        {
            WriteResult(result);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error executing tool: {ex.Message}");
    }
    finally
    {
        if (!string.Equals(method.Name, nameof(LoadSolutionTool.LoadSolution), StringComparison.Ordinal))
        {
            ToolCallLogger.Log(method.Name, rawValues);
        }
    }
}

static void WriteResult(object? result)
{
    if (result is null)
    {
        return;
    }

    if (result is string text)
    {
        Console.WriteLine(text);
        return;
    }

    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));
}

static object? ConvertInput(string value, Type targetType)
{
    if (targetType == typeof(string))
    {
        return value;
    }

    if (targetType == typeof(string[]))
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    if (targetType == typeof(int))
    {
        return int.Parse(value);
    }

    if (targetType == typeof(bool))
    {
        return bool.Parse(value);
    }

    return Convert.ChangeType(value, targetType);
}
