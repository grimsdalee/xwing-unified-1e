using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkitBuildAgent;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CommandLineOptions.Parse(args);

            if (options.ShowHelp)
            {
                CommandLineOptions.PrintHelp();
                return 0;
            }

            var configPath = Path.GetFullPath(
                options.ConfigPath
                ?? Path.Combine(AppContext.BaseDirectory, "agentsettings.json"));

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"Configuration file not found: {configPath}");
                return 2;
            }

            var config = JsonSerializer.Deserialize<AgentConfiguration>(
                await File.ReadAllTextAsync(configPath),
                JsonOptions)
                ?? throw new InvalidDataException(
                    $"Could not deserialize configuration: {configPath}");

            config.ResolvePaths(configPath);
            config.Validate();

            using var mutex = new Mutex(
                initiallyOwned: true,
                name: config.SingleInstanceMutexName,
                createdNew: out var createdNew);

            if (!createdNew)
            {
                Console.Error.WriteLine(
                    "Another UnifiedToolkit Build Agent instance is already running.");
                return 3;
            }

            using var cancellation = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (!cancellation.IsCancellationRequested)
                    cancellation.Cancel();
            };

            var agent = new BuildAgent(config, configPath);

            if (options.RunOnce)
                return await agent.RunOnceAsync(cancellation.Token);

            await agent.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

internal sealed class BuildAgent
{
    private readonly AgentConfiguration _configuration;
    private readonly string _configurationPath;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly object _changeLock = new();
    private readonly HashSet<string> _pendingChanges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();

    private Timer? _debounceTimer;
    private DateTimeOffset _lastObservedChangeUtc;
    private int _buildSequence;

    public BuildAgent(
        AgentConfiguration configuration,
        string configurationPath)
    {
        _configuration = configuration;
        _configurationPath = configurationPath;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_configuration.ReportDirectory);
        Directory.CreateDirectory(_configuration.LogDirectory);
        Directory.CreateDirectory(
            Path.GetDirectoryName(_configuration.QueueFilePath)!);

        WriteConsoleHeader();
        WriteStatus(AgentState.Starting, "Agent starting.");

        StartWatchers();

        if (_configuration.BuildOnStartup)
            ScheduleBuild("startup", Array.Empty<string>());

        using var queueTimer = new PeriodicTimer(
            TimeSpan.FromSeconds(_configuration.QueuePollSeconds));

        WriteStatus(
            AgentState.Watching,
            $"Watching {_configuration.ToolkitProjectDirectory}");

        try
        {
            while (await queueTimer.WaitForNextTickAsync(cancellationToken))
            {
                await ProcessQueueAsync(cancellationToken);
            }
        }
        finally
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();

            _debounceTimer?.Dispose();
            WriteStatus(AgentState.Stopped, "Agent stopped.");
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_configuration.ReportDirectory);
        Directory.CreateDirectory(_configuration.LogDirectory);
        WriteConsoleHeader();

        var result = await ExecutePipelineAsync(
            trigger: "manual-run-once",
            changedFiles: Array.Empty<string>(),
            cancellationToken);

        return result.Success ? 0 : 1;
    }

    private void StartWatchers()
    {
        foreach (var relativePath in _configuration.WatchDirectories)
        {
            var directory = Path.GetFullPath(
                Path.Combine(
                    _configuration.ToolkitProjectDirectory,
                    relativePath));

            if (!Directory.Exists(directory))
                continue;

            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter =
                    NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;

            _watchers.Add(watcher);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (!ShouldReactTo(eventArgs.FullPath))
            return;

        RegisterChange(eventArgs.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        if (ShouldReactTo(eventArgs.OldFullPath))
            RegisterChange(eventArgs.OldFullPath);

        if (ShouldReactTo(eventArgs.FullPath))
            RegisterChange(eventArgs.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs)
    {
        AppendAgentLog(
            $"Watcher error: {eventArgs.GetException().Message}");
        ScheduleBuild("watcher-error-recovery", Array.Empty<string>());
    }

    private bool ShouldReactTo(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (fullPath.StartsWith(
                _configuration.ReportDirectory,
                StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(
                _configuration.LogDirectory,
                StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(
                Path.Combine(
                    _configuration.ToolkitProjectDirectory,
                    "bin"),
                StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(
                Path.Combine(
                    _configuration.ToolkitProjectDirectory,
                    "obj"),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(fullPath);
        return _configuration.WatchedExtensions.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase);
    }

    private void RegisterChange(string path)
    {
        lock (_changeLock)
        {
            _pendingChanges.Add(path);
            _lastObservedChangeUtc = DateTimeOffset.UtcNow;

            _debounceTimer ??= new Timer(
                _ => FlushDebouncedChanges(),
                null,
                Timeout.Infinite,
                Timeout.Infinite);

            _debounceTimer.Change(
                TimeSpan.FromMilliseconds(
                    _configuration.DebounceMilliseconds),
                Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushDebouncedChanges()
    {
        string[] changed;

        lock (_changeLock)
        {
            var quietFor =
                DateTimeOffset.UtcNow - _lastObservedChangeUtc;

            if (quietFor.TotalMilliseconds
                < _configuration.DebounceMilliseconds - 100)
            {
                _debounceTimer?.Change(
                    TimeSpan.FromMilliseconds(
                        _configuration.DebounceMilliseconds),
                    Timeout.InfiniteTimeSpan);
                return;
            }

            changed = _pendingChanges
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _pendingChanges.Clear();
        }

        ScheduleBuild("filesystem-change", changed);
    }

    private void ScheduleBuild(
        string trigger,
        IReadOnlyCollection<string> changedFiles)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecutePipelineAsync(
                    trigger,
                    changedFiles,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                AppendAgentLog(
                    $"Unhandled pipeline exception: {exception}");
            }
        });
    }

    private async Task<BuildPipelineResult> ExecutePipelineAsync(
        string trigger,
        IReadOnlyCollection<string> changedFiles,
        CancellationToken cancellationToken)
    {
        await _executionGate.WaitAsync(cancellationToken);

        try
        {
            var sequence = Interlocked.Increment(ref _buildSequence);
            var startedUtc = DateTimeOffset.UtcNow;
            var stamp = startedUtc.ToString("yyyyMMdd-HHmmss");
            var logPath = Path.Combine(
                _configuration.LogDirectory,
                $"build-{stamp}-{sequence:D4}.log");

            WriteStatus(
                AgentState.Building,
                $"Build {sequence} started.",
                trigger,
                changedFiles,
                sequence);

            var pipeline = new BuildPipelineResult
            {
                Sequence = sequence,
                Trigger = trigger,
                StartedUtc = startedUtc,
                ChangedFiles = changedFiles
                    .Select(MakeRepositoryRelative)
                    .ToList(),
                LogPath = logPath
            };

            await using var log = new StreamWriter(
                logPath,
                append: false,
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            await log.WriteLineAsync(
                $"UnifiedToolkit Build Agent pipeline {sequence}");
            await log.WriteLineAsync(
                new string('=', 64));
            await log.WriteLineAsync(
                $"Started UTC: {startedUtc:O}");
            await log.WriteLineAsync(
                $"Trigger: {trigger}");
            await log.WriteLineAsync(
                $"Configuration: {_configurationPath}");
            await log.WriteLineAsync(
                $"Project: {_configuration.ToolkitProjectFile}");
            await log.WriteLineAsync();

            if (changedFiles.Count > 0)
            {
                await log.WriteLineAsync("Changed files:");
                foreach (var file in changedFiles)
                {
                    await log.WriteLineAsync(
                        $"  - {MakeRepositoryRelative(file)}");
                }
                await log.WriteLineAsync();
            }

            if (_configuration.RunCleanBeforeBuild)
            {
                var clean = await RunDotNetAsync(
                    new[] { "clean", _configuration.ToolkitProjectFile },
                    "dotnet clean",
                    log,
                    cancellationToken);
                pipeline.Steps.Add(clean);

                if (!clean.Success)
                    return await CompletePipelineAsync(pipeline, log);
            }

            var buildArguments = new List<string>
            {
                "build",
                _configuration.ToolkitProjectFile,
                "--nologo",
                "--verbosity",
                _configuration.BuildVerbosity
            };

            if (_configuration.TreatWarningsAsErrors)
                buildArguments.Add("-warnaserror");

            var build = await RunDotNetAsync(
                buildArguments,
                "dotnet build",
                log,
                cancellationToken);
            pipeline.Steps.Add(build);

            if (build.Success)
            {
                foreach (var configuredCommand
                         in _configuration.CommandsAfterSuccessfulBuild
                             .Where(command => command.Enabled))
                {
                    var commandResult = await RunToolkitCommandAsync(
                        configuredCommand,
                        log,
                        cancellationToken);
                    pipeline.Steps.Add(commandResult);

                    if (!commandResult.Success
                        && configuredCommand.StopPipelineOnFailure)
                    {
                        break;
                    }
                }
            }

            return await CompletePipelineAsync(pipeline, log);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task<BuildPipelineResult> CompletePipelineAsync(
        BuildPipelineResult pipeline,
        StreamWriter log)
    {
        pipeline.CompletedUtc = DateTimeOffset.UtcNow;
        pipeline.Success = pipeline.Steps.All(step => step.Success);
        pipeline.ErrorCount = pipeline.Steps.Sum(step => step.ErrorCount);
        pipeline.WarningCount = pipeline.Steps.Sum(step => step.WarningCount);

        await log.WriteLineAsync();
        await log.WriteLineAsync(new string('=', 64));
        await log.WriteLineAsync(
            $"Completed UTC: {pipeline.CompletedUtc:O}");
        await log.WriteLineAsync(
            $"Success: {pipeline.Success}");
        await log.WriteLineAsync(
            $"Errors: {pipeline.ErrorCount}");
        await log.WriteLineAsync(
            $"Warnings: {pipeline.WarningCount}");

        await WritePipelineResultAsync(pipeline);

        WriteStatus(
            pipeline.Success
                ? AgentState.BuildSucceeded
                : AgentState.BuildFailed,
            pipeline.Success
                ? $"Build {pipeline.Sequence} succeeded."
                : $"Build {pipeline.Sequence} failed.",
            pipeline.Trigger,
            pipeline.ChangedFiles,
            pipeline.Sequence,
            pipeline.ErrorCount,
            pipeline.WarningCount,
            pipeline.LogPath);

        CleanupOldLogs();
        return pipeline;
    }

    private async Task<ProcessStepResult> RunToolkitCommandAsync(
        ConfiguredToolkitCommand configuredCommand,
        StreamWriter log,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "run",
            "--project",
            _configuration.ToolkitProjectFile,
            "--"
        };

        arguments.AddRange(
            ExpandArguments(configuredCommand.Arguments));

        return await RunDotNetAsync(
            arguments,
            configuredCommand.Name,
            log,
            cancellationToken,
            configuredCommand.TimeoutMinutes);
    }

    private IEnumerable<string> ExpandArguments(
        IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            yield return argument
                .Replace(
                    "{RepositoryRoot}",
                    _configuration.RepositoryRoot,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "{ToolkitProjectDirectory}",
                    _configuration.ToolkitProjectDirectory,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "{ToolkitProjectFile}",
                    _configuration.ToolkitProjectFile,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<ProcessStepResult> RunDotNetAsync(
        IEnumerable<string> arguments,
        string stepName,
        StreamWriter log,
        CancellationToken cancellationToken,
        int? timeoutMinutes = null)
    {
        var argumentList = arguments.ToList();
        var result = new ProcessStepResult
        {
            Name = stepName,
            StartedUtc = DateTimeOffset.UtcNow,
            CommandLine = "dotnet "
                + string.Join(
                    " ",
                    argumentList.Select(QuoteForDisplay))
        };

        await log.WriteLineAsync();
        await log.WriteLineAsync(
            $"[{result.StartedUtc:O}] {result.Name}");
        await log.WriteLineAsync(result.CommandLine);
        await log.WriteLineAsync(new string('-', 64));

        var startInfo = new ProcessStartInfo
        {
            FileName = _configuration.DotNetExecutable,
            WorkingDirectory =
                _configuration.ToolkitProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in argumentList)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var outputLines = new List<string>();
        var outputLock = new object();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
                return;

            lock (outputLock)
                outputLines.Add(eventArgs.Data);

            lock (log)
            {
                log.WriteLine(eventArgs.Data);
                log.Flush();
            }

            Console.WriteLine(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
                return;

            lock (outputLock)
                outputLines.Add(eventArgs.Data);

            lock (log)
            {
                log.WriteLine(eventArgs.Data);
                log.Flush();
            }

            Console.Error.WriteLine(eventArgs.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException(
                $"Could not start step '{stepName}'.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(
            TimeSpan.FromMinutes(
                timeoutMinutes
                ?? _configuration.DefaultCommandTimeoutMinutes));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }

            result.TimedOut = !cancellationToken.IsCancellationRequested;
            result.Cancelled = cancellationToken.IsCancellationRequested;
        }

        result.CompletedUtc = DateTimeOffset.UtcNow;
        result.ExitCode = process.HasExited
            ? process.ExitCode
            : -1;

        string[] captured;
        lock (outputLock)
            captured = outputLines.ToArray();

        ParseDiagnostics(captured, result);
        result.Success =
            result.ExitCode == 0
            && !result.TimedOut
            && !result.Cancelled;

        await log.WriteLineAsync(new string('-', 64));
        await log.WriteLineAsync(
            $"Exit code: {result.ExitCode}");
        await log.WriteLineAsync(
            $"Errors: {result.ErrorCount}");
        await log.WriteLineAsync(
            $"Warnings: {result.WarningCount}");
        await log.WriteLineAsync(
            $"Success: {result.Success}");

        return result;
    }

    private static void ParseDiagnostics(
        IEnumerable<string> lines,
        ProcessStepResult result)
    {
        foreach (var line in lines)
        {
            if (line.Contains(
                    ": error ",
                    StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(
                    "error ",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorCount++;
                result.Diagnostics.Add(
                    BuildDiagnostic("error", line));
            }
            else if (line.Contains(
                         ": warning ",
                         StringComparison.OrdinalIgnoreCase)
                     || line.StartsWith(
                         "warning ",
                         StringComparison.OrdinalIgnoreCase))
            {
                result.WarningCount++;
                result.Diagnostics.Add(
                    BuildDiagnostic("warning", line));
            }
        }
    }

    private static BuildDiagnostic BuildDiagnostic(
        string severity,
        string line)
    {
        var diagnostic = new BuildDiagnostic
        {
            Severity = severity,
            Message = line.Trim()
        };

        var openParenthesis = line.IndexOf('(');
        var closeParenthesis = line.IndexOf(')');

        if (openParenthesis > 0
            && closeParenthesis > openParenthesis)
        {
            diagnostic.File = line[..openParenthesis].Trim();
            var location = line[
                (openParenthesis + 1)..closeParenthesis];
            var parts = location.Split(',');

            if (parts.Length > 0
                && int.TryParse(parts[0], out var lineNumber))
            {
                diagnostic.Line = lineNumber;
            }

            if (parts.Length > 1
                && int.TryParse(parts[1], out var column))
            {
                diagnostic.Column = column;
            }
        }

        return diagnostic;
    }

    private async Task ProcessQueueAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_configuration.QueueFilePath))
            return;

        AgentCommandQueue? queue;

        try
        {
            queue = JsonSerializer.Deserialize<AgentCommandQueue>(
                await File.ReadAllTextAsync(
                    _configuration.QueueFilePath,
                    cancellationToken),
                ProgramJson.Options);
        }
        catch (Exception exception)
        {
            AppendAgentLog(
                $"Could not parse queue file: {exception.Message}");
            return;
        }

        if (queue is null || queue.Commands.Count == 0)
            return;

        var pending = queue.Commands
            .Where(command => !command.Completed)
            .OrderBy(command => command.CreatedUtc)
            .ToList();

        foreach (var command in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            command.StartedUtc = DateTimeOffset.UtcNow;

            var result = await ExecuteQueuedCommandAsync(
                command,
                cancellationToken);

            command.CompletedUtc = DateTimeOffset.UtcNow;
            command.Completed = true;
            command.Success = result.Success;
            command.ExitCode = result.ExitCode;
            command.LogPath = result.LogPath;
            command.Error = result.Error;

            await WriteQueueAsync(queue, cancellationToken);
        }
    }

    private async Task<QueuedCommandResult> ExecuteQueuedCommandAsync(
        AgentQueuedCommand command,
        CancellationToken cancellationToken)
    {
        await _executionGate.WaitAsync(cancellationToken);

        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd-HHmmss");
            var logPath = Path.Combine(
                _configuration.LogDirectory,
                $"queue-{stamp}-{SanitizeFileName(command.Id)}.log");

            await using var log = new StreamWriter(
                logPath,
                append: false,
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            try
            {
                var configured = new ConfiguredToolkitCommand
                {
                    Name = command.Name,
                    Arguments = command.Arguments,
                    TimeoutMinutes = command.TimeoutMinutes
                        ?? _configuration.DefaultCommandTimeoutMinutes
                };

                var step = await RunToolkitCommandAsync(
                    configured,
                    log,
                    cancellationToken);

                return new QueuedCommandResult
                {
                    Success = step.Success,
                    ExitCode = step.ExitCode,
                    LogPath = logPath
                };
            }
            catch (Exception exception)
            {
                await log.WriteLineAsync(exception.ToString());

                return new QueuedCommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    LogPath = logPath,
                    Error = exception.Message
                };
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task WriteQueueAsync(
        AgentCommandQueue queue,
        CancellationToken cancellationToken)
    {
        var temporaryPath =
            _configuration.QueueFilePath + ".tmp";

        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(
                queue,
                ProgramJson.Options),
            new UTF8Encoding(false),
            cancellationToken);

        File.Move(
            temporaryPath,
            _configuration.QueueFilePath,
            overwrite: true);
    }

    private async Task WritePipelineResultAsync(
        BuildPipelineResult result)
    {
        var historyPath = Path.Combine(
            _configuration.ReportDirectory,
            $"build-result-{result.Sequence:D4}.json");

        var latestPath = Path.Combine(
            _configuration.ReportDirectory,
            "build-agent-latest.json");

        var json = JsonSerializer.Serialize(
            result,
            ProgramJson.Options);

        await File.WriteAllTextAsync(
            historyPath,
            json,
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            latestPath,
            json,
            new UTF8Encoding(false));
    }

    private void WriteStatus(
        AgentState state,
        string message,
        string? trigger = null,
        IEnumerable<string>? changedFiles = null,
        int? sequence = null,
        int errorCount = 0,
        int warningCount = 0,
        string? logPath = null)
    {
        var status = new AgentStatus
        {
            State = state,
            Message = message,
            UpdatedUtc = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Trigger = trigger,
            BuildSequence = sequence,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            LogPath = logPath,
            ChangedFiles = changedFiles?.ToList() ?? new List<string>()
        };

        var path = Path.Combine(
            _configuration.ReportDirectory,
            "build-agent-status.json");

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                status,
                ProgramJson.Options),
            new UTF8Encoding(false));

        Console.WriteLine(
            $"[{status.UpdatedUtc:HH:mm:ss}] {message}");
    }

    private void CleanupOldLogs()
    {
        var files = new DirectoryInfo(
            _configuration.LogDirectory)
            .GetFiles("*.log")
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(_configuration.MaximumRetainedLogs)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private void AppendAgentLog(string message)
    {
        Directory.CreateDirectory(_configuration.LogDirectory);
        File.AppendAllText(
            Path.Combine(
                _configuration.LogDirectory,
                "agent.log"),
            $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}",
            new UTF8Encoding(false));
    }

    private string MakeRepositoryRelative(string path)
    {
        try
        {
            return Path.GetRelativePath(
                    _configuration.RepositoryRoot,
                    path)
                .Replace('\\', '/');
        }
        catch
        {
            return path;
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var character
                 in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(character, '_');
        }

        return value;
    }

    private static string QuoteForDisplay(string argument) =>
        argument.Contains(' ')
            ? $"\"{argument}\""
            : argument;

    private void WriteConsoleHeader()
    {
        Console.WriteLine(
            "UnifiedToolkit Build Agent");
        Console.WriteLine(
            "==========================");
        Console.WriteLine(
            $"Repository:  {_configuration.RepositoryRoot}");
        Console.WriteLine(
            $"Project:     {_configuration.ToolkitProjectFile}");
        Console.WriteLine(
            $"Reports:     {_configuration.ReportDirectory}");
        Console.WriteLine(
            $"Queue:       {_configuration.QueueFilePath}");
        Console.WriteLine();
    }
}

internal sealed class AgentConfiguration
{
    public string RepositoryRoot { get; set; } = string.Empty;
    public string ToolkitProjectDirectory { get; set; } = string.Empty;
    public string ToolkitProjectFile { get; set; } = string.Empty;
    public string DotNetExecutable { get; set; } = "dotnet";
    public string ReportDirectory { get; set; } = string.Empty;
    public string LogDirectory { get; set; } = string.Empty;
    public string QueueFilePath { get; set; } = string.Empty;
    public string SingleInstanceMutexName { get; set; } =
        @"Global\UnifiedToolkitBuildAgent";
    public bool BuildOnStartup { get; set; } = true;
    public bool RunCleanBeforeBuild { get; set; }
    public bool TreatWarningsAsErrors { get; set; }
    public int DebounceMilliseconds { get; set; } = 2500;
    public int QueuePollSeconds { get; set; } = 5;
    public int DefaultCommandTimeoutMinutes { get; set; } = 30;
    public int MaximumRetainedLogs { get; set; } = 50;
    public string BuildVerbosity { get; set; } = "minimal";

    public List<string> WatchDirectories { get; set; } =
        new()
        {
            "."
        };

    public List<string> WatchedExtensions { get; set; } =
        new()
        {
            ".cs",
            ".csproj",
            ".json",
            ".props",
            ".targets",
            ".resx"
        };

    public List<ConfiguredToolkitCommand>
        CommandsAfterSuccessfulBuild { get; set; } = new();

    public void ResolvePaths(string configPath)
    {
        var configDirectory =
            Path.GetDirectoryName(configPath)
            ?? AppContext.BaseDirectory;

        RepositoryRoot = Resolve(
            RepositoryRoot,
            configDirectory);

        ToolkitProjectDirectory = Resolve(
            ToolkitProjectDirectory,
            RepositoryRoot);

        ToolkitProjectFile = Resolve(
            ToolkitProjectFile,
            ToolkitProjectDirectory);

        ReportDirectory = Resolve(
            ReportDirectory,
            RepositoryRoot);

        LogDirectory = Resolve(
            LogDirectory,
            RepositoryRoot);

        QueueFilePath = Resolve(
            QueueFilePath,
            RepositoryRoot);
    }

    public void Validate()
    {
        if (!Directory.Exists(RepositoryRoot))
            throw new DirectoryNotFoundException(
                $"Repository root not found: {RepositoryRoot}");

        if (!Directory.Exists(ToolkitProjectDirectory))
            throw new DirectoryNotFoundException(
                $"Toolkit project directory not found: {ToolkitProjectDirectory}");

        if (!File.Exists(ToolkitProjectFile))
            throw new FileNotFoundException(
                "Toolkit project file not found.",
                ToolkitProjectFile);

        if (DebounceMilliseconds < 250)
            throw new InvalidDataException(
                "DebounceMilliseconds must be at least 250.");

        if (QueuePollSeconds < 1)
            throw new InvalidDataException(
                "QueuePollSeconds must be at least 1.");
    }

    private static string Resolve(
        string value,
        string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Path.GetFullPath(baseDirectory);

        return Path.GetFullPath(
            Path.IsPathRooted(value)
                ? value
                : Path.Combine(baseDirectory, value));
    }
}

internal sealed class ConfiguredToolkitCommand
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool StopPipelineOnFailure { get; set; } = true;
    public int? TimeoutMinutes { get; set; }
    public List<string> Arguments { get; set; } = new();
}

internal sealed class BuildPipelineResult
{
    public int Sequence { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public bool Success { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public string LogPath { get; set; } = string.Empty;
    public List<string> ChangedFiles { get; set; } = new();
    public List<ProcessStepResult> Steps { get; set; } = new();
}

internal sealed class ProcessStepResult
{
    public string Name { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public bool Success { get; set; }
    public bool TimedOut { get; set; }
    public bool Cancelled { get; set; }
    public int ExitCode { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<BuildDiagnostic> Diagnostics { get; set; } = new();
}

internal sealed class BuildDiagnostic
{
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

internal sealed class AgentStatus
{
    public AgentState State { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
    public int ProcessId { get; set; }
    public int? BuildSequence { get; set; }
    public string? Trigger { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public string? LogPath { get; set; }
    public List<string> ChangedFiles { get; set; } = new();
}

internal enum AgentState
{
    Starting,
    Watching,
    Building,
    BuildSucceeded,
    BuildFailed,
    Stopped
}

internal sealed class AgentCommandQueue
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public List<AgentQueuedCommand> Commands { get; set; } = new();
}

internal sealed class AgentQueuedCommand
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public int? TimeoutMinutes { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } =
        DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public bool Completed { get; set; }
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string? LogPath { get; set; }
    public string? Error { get; set; }
}

internal sealed class QueuedCommandResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string LogPath { get; set; } = string.Empty;
    public string? Error { get; set; }
}

internal static class ProgramJson
{
    public static JsonSerializerOptions Options { get; } =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
}

internal sealed class CommandLineOptions
{
    public string? ConfigPath { get; private set; }
    public bool RunOnce { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--config":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException(
                            "--config requires a path.");

                    options.ConfigPath = args[++index];
                    break;

                case "--run-once":
                    options.RunOnce = true;
                    break;

                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown argument: {args[index]}");
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            "UnifiedToolkitBuildAgent");
        Console.WriteLine();
        Console.WriteLine(
            "  --config <path>   Configuration JSON path.");
        Console.WriteLine(
            "  --run-once        Run one build and exit.");
        Console.WriteLine(
            "  --help            Show this help.");
    }
}
