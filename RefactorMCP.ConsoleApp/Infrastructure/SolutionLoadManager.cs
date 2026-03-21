using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using ModelContextProtocol;
using RefactorMCP.ConsoleApp.Tools;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using System.Text.RegularExpressions;

public sealed class SolutionLoadStatus
{
    public string OperationId { get; init; } = string.Empty;
    public string SolutionPath { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int TotalProjects { get; init; }
    public int CompletedProjects { get; init; }
    public int TotalProjectFiles { get; init; }
    public int SeenProjectFiles { get; init; }
    public int CurrentProjectOrdinal { get; init; }
    public double ProgressPercent { get; init; }
    public string? CurrentProjectPath { get; init; }
    public string? CurrentProjectName { get; init; }
    public string? CurrentOperation { get; init; }
    public string? CurrentTargetFramework { get; init; }
    public long LastOperationElapsedMilliseconds { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public string CreatedAtUtc { get; init; } = string.Empty;
    public string UpdatedAtUtc { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? Message { get; init; }
    public bool IsTerminal { get; init; }
}

internal static class SolutionLoadManager
{
    private static readonly ConcurrentDictionary<string, SolutionLoadOperationState> _operations =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _sync = new();
    private static string? _activeOperationId;

    internal static SolutionLoadStatus BeginLoadSolution(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedSolutionPath = RefactoringHelpers.ResolveSolutionPath(solutionPath);

        if (RefactoringHelpers.TryGetReusableLoadedSolution(resolvedSolutionPath, out var loadedSolution))
        {
            var completed = SolutionLoadOperationState.CreateCompleted(
                resolvedSolutionPath,
                loadedSolution!,
                GetEstimatedProjectInfo(resolvedSolutionPath),
                RefactoringHelpers.BuildLoadedSolutionMessage(resolvedSolutionPath, loadedSolution!));
            _operations[completed.OperationId] = completed;
            return completed.CreateSnapshot();
        }

        lock (_sync)
        {
            if (_activeOperationId is not null &&
                _operations.TryGetValue(_activeOperationId, out var activeOperation) &&
                !activeOperation.IsTerminal)
            {
                if (RefactoringHelpers.PathEquals(activeOperation.SolutionPath, resolvedSolutionPath))
                {
                    return activeOperation.CreateSnapshot();
                }

                throw new McpException(
                    $"Solution load '{_activeOperationId}' is still running for '{activeOperation.SolutionPath}'. Wait for it to finish or cancel it first.");
            }

            var estimatedProjectInfo = GetEstimatedProjectInfo(resolvedSolutionPath);
            var operation = new SolutionLoadOperationState(
                Guid.NewGuid().ToString("N"),
                resolvedSolutionPath,
                estimatedProjectInfo.TotalProjects,
                estimatedProjectInfo.TotalProjectFiles);

            _operations[operation.OperationId] = operation;
            _activeOperationId = operation.OperationId;
            _ = Task.Run(() => RunLoadAsync(operation));
            return operation.CreateSnapshot();
        }
    }

    internal static SolutionLoadStatus GetLoadSolutionStatus(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            throw new McpException($"Solution load operation '{operationId}' was not found.");
        }

        return operation.CreateSnapshot();
    }

    internal static SolutionLoadStatus CancelLoadSolution(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            throw new McpException($"Solution load operation '{operationId}' was not found.");
        }

        operation.Cancel();
        return operation.CreateSnapshot();
    }

    internal static void CancelAllLoads()
    {
        foreach (var operation in _operations.Values)
        {
            operation.Cancel();
        }
    }

    private static async Task RunLoadAsync(SolutionLoadOperationState operation)
    {
        try
        {
            operation.MarkLoading();
            RefactoringHelpers.ClearAllCaches();
            MoveMethodTool.ResetMoveHistory();

            var solutionPath = operation.SolutionPath;
            Directory.SetCurrentDirectory(Path.GetDirectoryName(solutionPath)!);

            var logDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".refactor-mcp");
            ToolCallLogger.SetLogDirectory(logDir);
            ToolCallLogger.Log("BeginLoadSolution", new Dictionary<string, string?> { ["solutionPath"] = solutionPath });

            var progress = new Progress<ProjectLoadProgress>(operation.ReportProgress);
            var solution = await RefactoringHelpers.LoadSolutionIntoSessionWithProjectProgress(
                solutionPath,
                progress,
                operation.CancellationToken);

            var metricsDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".refactor-mcp", "metrics");
            Directory.CreateDirectory(metricsDir);

            operation.MarkCompleted(
                solution.Projects.Count(),
                RefactoringHelpers.BuildLoadedSolutionMessage(solutionPath, solution));
        }
        catch (OperationCanceledException)
        {
            operation.MarkCancelled();
        }
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
        }
        finally
        {
            lock (_sync)
            {
                if (string.Equals(_activeOperationId, operation.OperationId, StringComparison.Ordinal))
                {
                    _activeOperationId = null;
                }
            }
        }
    }

    private static EstimatedProjectInfo GetEstimatedProjectInfo(string solutionPath)
    {
        var projectPaths = EnumerateSolutionProjectPaths(solutionPath)
            .Select(projectPath => ResolveProjectPath(solutionPath, projectPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalProjects = 0;
        foreach (var projectPath in projectPaths)
        {
            totalProjects += EstimateProjectLoadUnits(projectPath);
        }

        if (totalProjects == 0 && projectPaths.Count > 0)
        {
            totalProjects = projectPaths.Count;
        }

        return new EstimatedProjectInfo(totalProjects, projectPaths.Count);
    }

    private static IReadOnlyList<string> EnumerateSolutionProjectPaths(string solutionPath)
    {
        var extension = Path.GetExtension(solutionPath);
        return string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
            ? EnumerateSlnxProjectPaths(solutionPath)
            : EnumerateSlnProjectPaths(solutionPath);
    }

    private static IReadOnlyList<string> EnumerateSlnxProjectPaths(string solutionPath)
    {
        var document = XDocument.Load(solutionPath);
        return document.Descendants("Project")
            .Select(node => node.Attribute("Path")?.Value)
            .Where(path => IsSupportedProjectPath(path))
            .Cast<string>()
            .ToList();
    }

    private static IReadOnlyList<string> EnumerateSlnProjectPaths(string solutionPath)
    {
        var projectPaths = new List<string>();
        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = Regex.Match(line, "^Project\\(\"\\{[^}]+\\}\"\\) = \"[^\"]+\", \"(?<path>[^\"]+)\"");
            if (!match.Success)
            {
                continue;
            }

            var projectPath = match.Groups["path"].Value;
            if (IsSupportedProjectPath(projectPath))
            {
                projectPaths.Add(projectPath);
            }
        }

        return projectPaths;
    }

    private static string ResolveProjectPath(string solutionPath, string projectPath)
    {
        if (Path.IsPathRooted(projectPath))
        {
            return Path.GetFullPath(projectPath);
        }

        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        return Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
    }

    private static int EstimateProjectLoadUnits(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return 1;
        }

        try
        {
            var document = XDocument.Load(projectPath);
            var targetFrameworks = document.Descendants()
                .FirstOrDefault(node => string.Equals(node.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(targetFrameworks))
            {
                var count = targetFrameworks
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Length;
                if (count > 0)
                {
                    return count;
                }
            }

            var targetFramework = document.Descendants()
                .FirstOrDefault(node => string.Equals(node.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return string.IsNullOrWhiteSpace(targetFramework) ? 1 : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static bool IsSupportedProjectPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return false;
        }

        var extension = Path.GetExtension(projectPath);
        return string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".fsproj", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct EstimatedProjectInfo(int TotalProjects, int TotalProjectFiles);

    private sealed class SolutionLoadOperationState(
        string operationId,
        string solutionPath,
        int totalProjects,
        int totalProjectFiles)
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly DateTimeOffset _createdAtUtc = DateTimeOffset.UtcNow;
        private readonly HashSet<string> _seenProjectFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completedProjects = new(StringComparer.OrdinalIgnoreCase);
        private string _state = "Queued";
        private string? _currentProjectPath;
        private string? _currentOperation;
        private string? _currentTargetFramework;
        private long _lastOperationElapsedMilliseconds;
        private string? _errorMessage;
        private string? _message = "Queued";
        private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;
        private int _totalProjects = totalProjects;
        private int _totalProjectFiles = totalProjectFiles;

        internal string OperationId { get; } = operationId;
        internal string SolutionPath { get; } = solutionPath;
        internal CancellationToken CancellationToken => _cancellationTokenSource.Token;

        internal bool IsTerminal
        {
            get
            {
                lock (_sync)
                {
                    return IsTerminalState(_state);
                }
            }
        }

        internal static SolutionLoadOperationState CreateCompleted(
            string solutionPath,
            Solution solution,
            EstimatedProjectInfo estimatedProjectInfo,
            string message)
        {
            var operation = new SolutionLoadOperationState(
                Guid.NewGuid().ToString("N"),
                solutionPath,
                Math.Max(estimatedProjectInfo.TotalProjects, solution.Projects.Count()),
                Math.Max(estimatedProjectInfo.TotalProjectFiles, solution.Projects.Select(p => p.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
            operation.MarkCompleted(solution.Projects.Count(), message);
            return operation;
        }

        internal void MarkLoading()
        {
            lock (_sync)
            {
                _state = "Loading";
                _message = "Loading solution";
                _updatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        internal void ReportProgress(ProjectLoadProgress progress)
        {
            lock (_sync)
            {
                var normalizedProjectPath = NormalizeProjectPath(progress.FilePath);
                if (!string.IsNullOrWhiteSpace(normalizedProjectPath))
                {
                    _seenProjectFiles.Add(normalizedProjectPath);
                    if (progress.Operation == ProjectLoadOperation.Resolve)
                    {
                        _completedProjects.Add(BuildCompletedProjectKey(normalizedProjectPath, progress.TargetFramework));
                        _totalProjects = Math.Max(_totalProjects, _completedProjects.Count);
                    }
                }

                _currentProjectPath = progress.FilePath;
                _currentOperation = progress.Operation.ToString();
                _currentTargetFramework = progress.TargetFramework;
                _lastOperationElapsedMilliseconds = (long)progress.ElapsedTime.TotalMilliseconds;
                _message = BuildProgressMessage(progress);
                _updatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        internal void MarkCompleted(int actualProjectCount, string message)
        {
            lock (_sync)
            {
                _state = "Completed";
                _totalProjects = Math.Max(_totalProjects, actualProjectCount);
                _totalProjectFiles = Math.Max(_totalProjectFiles, _seenProjectFiles.Count);
                _completedProjects.Clear();
                for (var i = 0; i < _totalProjects; i++)
                {
                    _completedProjects.Add($"completed-{i}");
                }

                _message = message;
                _updatedAtUtc = DateTimeOffset.UtcNow;
                _stopwatch.Stop();
            }
        }

        internal void MarkCancelled()
        {
            lock (_sync)
            {
                _state = "Cancelled";
                _message = "Solution load was cancelled";
                _updatedAtUtc = DateTimeOffset.UtcNow;
                _stopwatch.Stop();
            }
        }

        internal void MarkFailed(Exception exception)
        {
            lock (_sync)
            {
                _state = "Failed";
                _errorMessage = exception.Message;
                _message = "Solution load failed";
                _updatedAtUtc = DateTimeOffset.UtcNow;
                _stopwatch.Stop();
            }
        }

        internal void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        internal SolutionLoadStatus CreateSnapshot()
        {
            lock (_sync)
            {
                var currentProjectOrdinal = _seenProjectFiles.Count;
                var completedProjects = _completedProjects.Count;
                var totalProjects = Math.Max(_totalProjects, completedProjects);
                var progressPercent = totalProjects == 0
                    ? 0
                    : Math.Round(Math.Min(100, completedProjects * 100.0 / totalProjects), 1);

                return new SolutionLoadStatus
                {
                    OperationId = OperationId,
                    SolutionPath = SolutionPath,
                    State = _state,
                    TotalProjects = totalProjects,
                    CompletedProjects = completedProjects,
                    TotalProjectFiles = Math.Max(_totalProjectFiles, _seenProjectFiles.Count),
                    SeenProjectFiles = _seenProjectFiles.Count,
                    CurrentProjectOrdinal = currentProjectOrdinal,
                    ProgressPercent = progressPercent,
                    CurrentProjectPath = _currentProjectPath,
                    CurrentProjectName = string.IsNullOrWhiteSpace(_currentProjectPath)
                        ? null
                        : Path.GetFileNameWithoutExtension(_currentProjectPath),
                    CurrentOperation = _currentOperation,
                    CurrentTargetFramework = _currentTargetFramework,
                    LastOperationElapsedMilliseconds = _lastOperationElapsedMilliseconds,
                    ElapsedMilliseconds = (long)_stopwatch.Elapsed.TotalMilliseconds,
                    CreatedAtUtc = _createdAtUtc.ToUniversalTime().ToString("O"),
                    UpdatedAtUtc = _updatedAtUtc.ToUniversalTime().ToString("O"),
                    ErrorMessage = _errorMessage,
                    Message = _message,
                    IsTerminal = IsTerminalState(_state)
                };
            }
        }

        private static string BuildCompletedProjectKey(string projectPath, string? targetFramework)
        {
            return string.IsNullOrWhiteSpace(targetFramework)
                ? projectPath
                : $"{projectPath}|{targetFramework}";
        }

        private static string NormalizeProjectPath(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return string.Empty;
            }

            return Path.GetFullPath(projectPath);
        }

        private static string BuildProgressMessage(ProjectLoadProgress progress)
        {
            var projectName = string.IsNullOrWhiteSpace(progress.FilePath)
                ? "unknown"
                : Path.GetFileNameWithoutExtension(progress.FilePath);
            return string.IsNullOrWhiteSpace(progress.TargetFramework)
                ? $"{progress.Operation} {projectName}"
                : $"{progress.Operation} {projectName} ({progress.TargetFramework})";
        }

        private static bool IsTerminalState(string state)
        {
            return string.Equals(state, "Completed", StringComparison.Ordinal) ||
                   string.Equals(state, "Failed", StringComparison.Ordinal) ||
                   string.Equals(state, "Cancelled", StringComparison.Ordinal);
        }
    }
}
