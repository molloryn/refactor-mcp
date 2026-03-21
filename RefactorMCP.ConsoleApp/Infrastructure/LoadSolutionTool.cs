using ModelContextProtocol.Server;
using ModelContextProtocol;
using Microsoft.CodeAnalysis;
using RefactorMCP.ConsoleApp.Tools;
using System.ComponentModel;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;


[McpServerToolType]
public static class LoadSolutionTool
{
    [McpServerTool, Description("Start a new session by clearing caches then load a solution file and set the current directory")]
    public static async Task<string> LoadSolution(
        [Description(RefactoringHelpers.SolutionPathDescription)] string solutionPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            solutionPath = RefactoringHelpers.ResolveSolutionPath(solutionPath);

            var logDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".refactor-mcp");
            ToolCallLogger.SetLogDirectory(logDir);
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["solutionPath"] = solutionPath });

            Directory.SetCurrentDirectory(Path.GetDirectoryName(solutionPath)!);
            progress?.Report($"Loading {solutionPath}");

            if (RefactoringHelpers.TryGetReusableLoadedSolution(solutionPath, out var cached))
            {
                return BuildLoadedMessage(solutionPath, cached!);
            }

            RefactoringHelpers.ClearAllCaches();
            MoveMethodTool.ResetMoveHistory();

            var stopwatch = Stopwatch.StartNew();
            var solution = await RefactoringHelpers.LoadSolutionIntoSession(solutionPath, progress, cancellationToken);
            stopwatch.Stop();
            var metricsDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".refactor-mcp", "metrics");
            Directory.CreateDirectory(metricsDir);

            var message = BuildLoadedMessage(solutionPath, solution);
            progress?.Report($"{message} in {stopwatch.Elapsed:mm\\:ss\\.fff}");
            return message;
        }
        catch (Exception ex)
        {
            throw new McpException($"Error loading solution: {ex.Message}", ex);
        }
    }

    private static string BuildLoadedMessage(string solutionPath, Solution solution)
    {
        var projects = solution.Projects.Select(p => p.Name).ToList();
        return $"Successfully loaded solution '{Path.GetFileName(solutionPath)}' with {projects.Count} projects: {string.Join(", ", projects)}";
    }
}
