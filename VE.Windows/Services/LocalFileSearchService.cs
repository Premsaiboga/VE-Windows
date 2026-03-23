using System.Collections.Concurrent;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using VE.Windows.Helpers;

namespace VE.Windows.Services;

/// <summary>
/// Hybrid local file search: Windows Search Index (instant) + filesystem fallback (parallel).
/// Matches the speed of Windows Explorer's search bar.
/// </summary>
public sealed class LocalFileSearchService
{
    public static LocalFileSearchService Instance { get; } = new();
    private LocalFileSearchService() { }

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Windows", "ProgramData",
        "Recovery", "PerfLogs", "$WinREAgent", "Config.Msi",
        "node_modules", ".git", ".svn", ".hg", "__pycache__",
        "AppData", "MSOCache", "Intel", "AMD"
    };

    /// <summary>
    /// Search for files by name. Returns results progressively via callback.
    /// Uses Windows Search Index first, then falls back to filesystem scan.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(
        string query,
        CancellationToken ct,
        Action<SearchResult>? onResultFound = null,
        int maxResults = 50)
    {
        var results = new ConcurrentBag<SearchResult>();
        var seenPaths = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Step 1: Try Windows Search Index (instant, < 1 second)
        try
        {
            var indexResults = await Task.Run(() => SearchWindowsIndex(query, maxResults, ct), ct);
            foreach (var r in indexResults)
            {
                if (seenPaths.TryAdd(r.FullPath, true))
                {
                    results.Add(r);
                    onResultFound?.Invoke(r);
                }
            }

            if (results.Count >= maxResults)
            {
                FileLogger.Instance.Info("FileSearch", $"Index returned {results.Count} results for '{query}'");
                return results.ToList();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Debug("FileSearch", $"Windows Search Index unavailable: {ex.Message}");
        }

        ct.ThrowIfCancellationRequested();

        // Step 2: Filesystem scan — priority folders first, then full drives
        var remaining = maxResults - results.Count;
        if (remaining > 0)
        {
            await SearchFileSystemAsync(query, remaining, ct, r =>
            {
                if (seenPaths.TryAdd(r.FullPath, true))
                {
                    results.Add(r);
                    onResultFound?.Invoke(r);
                }
            });
        }

        FileLogger.Instance.Info("FileSearch", $"Total {results.Count} results for '{query}'");
        return results.ToList();
    }

    /// <summary>
    /// Query the Windows Search Index via OLE DB. Returns results in milliseconds.
    /// </summary>
    private List<SearchResult> SearchWindowsIndex(string query, int maxResults, CancellationToken ct)
    {
        var results = new List<SearchResult>();

        // Sanitize query to prevent SQL injection in the OLE DB query
        var sanitized = query.Replace("'", "''").Replace("%", "[%]").Replace("_", "[_]");

        using var connection = new OleDbConnection(
            "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'");
        connection.Open();

        var sql = $"SELECT TOP {maxResults} System.ItemPathDisplay, System.ItemType, System.Size, System.DateModified " +
                  $"FROM SystemIndex WHERE System.FileName LIKE '%{sanitized}%' " +
                  $"ORDER BY System.DateModified DESC";

        using var command = new OleDbCommand(sql, connection);
        command.CommandTimeout = 10;

        using var reader = command.ExecuteReader();
        while (reader.Read() && results.Count < maxResults)
        {
            ct.ThrowIfCancellationRequested();

            var path = reader[0]?.ToString();
            if (string.IsNullOrEmpty(path)) continue;

            // Verify file still exists (index can be stale)
            if (!File.Exists(path)) continue;

            results.Add(new SearchResult
            {
                FullPath = path,
                FileName = Path.GetFileName(path),
                FileSize = reader[2] as long? ?? 0,
                LastModified = reader[3] as DateTime? ?? DateTime.MinValue
            });
        }

        return results;
    }

    /// <summary>
    /// Parallel filesystem scan. Searches priority folders first, then all drives.
    /// </summary>
    private async Task SearchFileSystemAsync(
        string query, int maxResults, CancellationToken ct, Action<SearchResult> onFound)
    {
        var found = 0;

        // Priority: user folders first (most likely location)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var priorityFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(userProfile, "Downloads"),
            Path.Combine(userProfile, "OneDrive"),
            Path.Combine(userProfile, "Pictures"),
            Path.Combine(userProfile, "Videos"),
            Path.Combine(userProfile, "Music"),
        }.Where(Directory.Exists).ToArray();

        // Search priority folders in parallel
        await Task.Run(() =>
        {
            Parallel.ForEach(priorityFolders, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 4
            }, folder =>
            {
                if (Interlocked.CompareExchange(ref found, 0, 0) >= maxResults) return;
                SearchDirectory(folder, query, ref found, maxResults, ct, onFound, maxDepth: 10);
            });
        }, ct);

        if (found >= maxResults) return;

        // Then search all drive roots (skip priority folders already searched)
        var prioritySet = new HashSet<string>(priorityFolders, StringComparer.OrdinalIgnoreCase);
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();

        await Task.Run(() =>
        {
            Parallel.ForEach(drives, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 2
            }, drive =>
            {
                if (Interlocked.CompareExchange(ref found, 0, 0) >= maxResults) return;
                SearchDirectoryTopLevel(drive, query, ref found, maxResults, ct, onFound, prioritySet);
            });
        }, ct);
    }

    /// <summary>
    /// Search top-level directories of a drive, skipping already-searched and system folders.
    /// </summary>
    private void SearchDirectoryTopLevel(
        string drive, string query, ref int found, int maxResults,
        CancellationToken ct, Action<SearchResult> onFound, HashSet<string> skipPaths)
    {
        try
        {
            // Search files in the drive root
            SearchFilesInFolder(drive, query, ref found, maxResults, ct, onFound);

            // Then recurse into subdirectories (skip system + already-searched)
            foreach (var dir in Directory.EnumerateDirectories(drive))
            {
                ct.ThrowIfCancellationRequested();
                if (Interlocked.CompareExchange(ref found, 0, 0) >= maxResults) return;

                var dirName = Path.GetFileName(dir);
                if (SkipFolders.Contains(dirName)) continue;
                if (skipPaths.Contains(dir)) continue;

                SearchDirectory(dir, query, ref found, maxResults, ct, onFound, maxDepth: 8);
            }
        }
        catch { }
    }

    private void SearchDirectory(
        string path, string query, ref int found, int maxResults,
        CancellationToken ct, Action<SearchResult> onFound, int maxDepth, int depth = 0)
    {
        if (depth > maxDepth) return;
        if (Interlocked.CompareExchange(ref found, 0, 0) >= maxResults) return;

        ct.ThrowIfCancellationRequested();

        // Search files in this folder
        SearchFilesInFolder(path, query, ref found, maxResults, ct, onFound);

        // Recurse into subdirectories
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                if (Interlocked.CompareExchange(ref found, 0, 0) >= maxResults) return;

                var dirName = Path.GetFileName(dir);
                if (SkipFolders.Contains(dirName)) continue;
                if (dirName.StartsWith('.')) continue; // Skip hidden folders

                SearchDirectory(dir, query, ref found, maxResults, ct, onFound, maxDepth, depth + 1);
            }
        }
        catch { } // Permission denied — skip silently
    }

    private void SearchFilesInFolder(
        string path, string query, ref int found, int maxResults,
        CancellationToken ct, Action<SearchResult> onFound)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                if (Interlocked.CompareExchange(ref found, 0, 0) >= maxResults) return;

                var fileName = Path.GetFileName(file);
                if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref found);
                    try
                    {
                        var info = new FileInfo(file);
                        onFound(new SearchResult
                        {
                            FullPath = file,
                            FileName = fileName,
                            FileSize = info.Length,
                            LastModified = info.LastWriteTime
                        });
                    }
                    catch
                    {
                        onFound(new SearchResult
                        {
                            FullPath = file,
                            FileName = fileName
                        });
                    }
                }
            }
        }
        catch { } // Permission denied — skip silently
    }

    /// <summary>
    /// Open a file with its default application.
    /// </summary>
    public static void OpenFile(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("FileSearch", $"Failed to open file: {ex.Message}");
        }
    }
}

public class SearchResult
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }

    public string FormattedSize
    {
        get
        {
            if (FileSize == 0) return "";
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024.0 * 1024):F1} MB";
            return $"{FileSize / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    public string FolderPath => Path.GetDirectoryName(FullPath) ?? "";
}