using System.IO;

namespace dlp_agent;

public class TransmissionMonitor
{
    private List<FileSystemWatcher> _watchers = new();
    private List<string> _restrictedFolders = new();
    private string _mode = "WARN";
    private readonly DatabaseManager _dbManager;

    public TransmissionMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    public void UpdateRules(List<string> folders, string mode)
    {
        _restrictedFolders = folders;
        _mode = mode;
        RestartWatchers();
    }

    private void RestartWatchers()
    {
        foreach (var w in _watchers) { w.Dispose(); }
        _watchers.Clear();

        foreach (var folder in _restrictedFolders)
        {
            if (Directory.Exists(folder))
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                // Catch Chrome/Edge when they finish downloading and rename the .tmp file
                watcher.Renamed += (s, e) => ProcessFile(e.FullPath, e.Name);
                
                // Catch normal file creations (e.g., Save As from Word)
                watcher.Created += (s, e) => ProcessFile(e.FullPath, e.Name);
                
                _watchers.Add(watcher);
            }
        }
    }

    private void ProcessFile(string fullPath, string? fileName)
    {
        if (fileName == null) return;
        
        // IGNORE browser temporary files!
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || 
            fileName.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) 
            return;

        bool isBlock = _mode == "BLOCK";
        string message = isBlock ? "File creation blocked in restricted folder." : "Warning: File activity detected in restricted folder.";
        
        NotificationManager.ShowWarning($"Security Alert: '{fileName}'. {message}", isBlock);
        
        // If strictly blocked, delete the file instantly!
        if (isBlock)
        {
            try { File.Delete(fullPath); } catch { /* Might be locked by another app */ }
        }

        _dbManager.LogEvent(isBlock ? "RESTRICTED_FILE_BLOCKED" : "RESTRICTED_FILE_WARNING", fullPath);
    }
}