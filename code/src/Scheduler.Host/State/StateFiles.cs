using Scheduler.Core.Contracts;

namespace Scheduler.Host.State;

/// <summary>
/// The log folder's state files as Host keeps them (<see cref="IStateFiles"/>): the text Core
/// builds, one file each beside the logs. An unreadable file counts as none, and a file that
/// cannot be written is left alone, as the script's try/catch around each of them did.
/// </summary>
public sealed class StateFiles(string logDir) : IStateFiles
{
    /// <summary>The files live in this folder.</summary>
    public string LogDir { get; } = logDir;

    /// <summary>The file's path in the log folder.</summary>
    public string PathOf(StateFile file) => System.IO.Path.Combine(LogDir, Name(file));

    /// <inheritdoc />
    public string? Read(StateFile file)
    {
        try
        {
            var path = PathOf(file);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Write(StateFile file, string text)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(PathOf(file), text);
        }
        catch (IOException)
        {
            // A file another process holds is not worth losing a tick over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Name(StateFile file) => file switch
    {
        StateFile.Retries => "retries.json",
        StateFile.CategoriesAccepted => "lanes-accepted.json",
        StateFile.WorkersState => "workers-state.json",
        StateFile.LegacyWorkers => "workers.json",
        StateFile.Running => "running.json",
        StateFile.Status => "status.txt",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "not a state file"),
    };
}
