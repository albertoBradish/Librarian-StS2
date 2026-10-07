using System.IO;
using System.Runtime.InteropServices;
using Godot;

namespace Librarian.Mechanics;

/// <summary>Player-facing exports use the operating system's Downloads folder, independently of game data.</summary>
internal static class LibrarianExportDestination
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");
    internal static string LastOpenedDirectory { get; private set; } = "";
    internal static string LastOpenError { get; private set; } = "";
    internal static Error? LastShellOpenResult { get; private set; }
    internal static int OpenAttemptCount { get; private set; }

    internal static string DownloadsDirectory
    {
        get
        {
            // A redirected Windows known folder may be on another drive or a network share.
            if (OperatingSystem.IsWindows() && WindowsDownloadsDirectory() is { } windowsPath)
                return windowsPath;

            string systemPath = OS.GetSystemDir(OS.SystemDir.Downloads);
            if (!string.IsNullOrWhiteSpace(systemPath) && Path.IsPathFullyQualified(systemPath))
                return Path.GetFullPath(systemPath);

            string profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
                throw new IOException("The user's Downloads folder could not be located.");
            return Path.Combine(profile, "Downloads");
        }
    }

    internal static string CreateUniqueDirectory(string prefix, string? destinationDirectory = null, bool partial = false)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            prefix.Contains(Path.DirectorySeparatorChar) || prefix.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("The export folder name must be a single file name.", nameof(prefix));

        // The explicit destination is used by isolated audits; player actions always use Downloads.
        string downloads = destinationDirectory is null ? DownloadsDirectory : Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(downloads);
        string directory = Path.Combine(downloads, prefix + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N") + (partial ? ".partial" : ""));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static bool TryOpenDirectory(string directory, out string reason)
    {
        LastOpenedDirectory = "";
        LastOpenError = "";
        LastShellOpenResult = null;
        try
        {
            string path = Path.GetFullPath(directory);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            OpenAttemptCount++;
            Error result = OS.ShellOpen(path);
            reason = result == Error.Ok ? "" : result.ToString();
            LastShellOpenResult = result;
            LastOpenError = reason;
            if (result == Error.Ok) LastOpenedDirectory = path;
            return result == Error.Ok;
        }
        catch (Exception e)
        {
            // Opening a file manager is separate from completing an export.
            reason = e.Message;
            LastOpenError = reason;
            return false;
        }
    }

    private static string? WindowsDownloadsDirectory()
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            Guid folderId = DownloadsFolderId;
            int result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out pointer);
            if (result != 0 || pointer == IntPtr.Zero) return null;
            string? directory = Marshal.PtrToStringUni(pointer);
            return !string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory) ? Path.GetFullPath(directory) : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
