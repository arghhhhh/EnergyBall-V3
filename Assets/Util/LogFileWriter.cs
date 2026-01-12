using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Captures Unity console logs and writes them to a text file for easy access.
/// This allows you to copy/paste logs without dealing with Unity's console limitations.
/// </summary>
public class LogFileWriter : MonoBehaviour
{
    [Header("Log File Settings")]
    [Tooltip("File name for the log file (will be saved to persistent data path)")]
    public string logFileName = "unity_console_logs.txt";
    
    [Tooltip("Maximum number of log entries to keep in the file")]
    public int maxLogEntries = 1000;
    
    [Tooltip("Include timestamps with each log entry")]
    public bool includeTimestamp = true;
    
    [Tooltip("Include log type (Log, Warning, Error) with each entry")]
    public bool includeLogType = true;
    
    [Tooltip("Clear the log file when the application starts")]
    public bool clearOnStart = true;
    
    private string logFilePath;
    private int logCount = 0;
    
    void Awake()
    {
        // Create the full path to the log file in the project folder
        logFilePath = System.IO.Path.Combine(Application.dataPath, "..", logFileName);
        
        // Clear the log file if requested
        if (clearOnStart)
        {
            ClearLogFile();
        }
        
        // Subscribe to Unity's log message callback
        Application.logMessageReceived += OnLogMessageReceived;
        
        // Log the file location so user knows where to find it
        Debug.Log($"Log file writer initialized. Logs will be saved to: {logFilePath}");
    }
    
    void OnDestroy()
    {
        // Unsubscribe from the callback to prevent memory leaks
        Application.logMessageReceived -= OnLogMessageReceived;
    }
    
    private void OnLogMessageReceived(string logString, string stackTrace, LogType type)
    {
        try
        {
            // Build the log entry
            string logEntry = BuildLogEntry(logString, stackTrace, type);
            
            // Write to file
            WriteToFile(logEntry);
            
            // Increment counter
            logCount++;
            
            // Check if we need to trim the file
            if (logCount > maxLogEntries)
            {
                TrimLogFile();
            }
        }
        catch (Exception e)
        {
            // Don't use Debug.Log here to avoid infinite recursion
            Console.WriteLine($"LogFileWriter error: {e.Message}");
        }
    }
    
    private string BuildLogEntry(string logString, string stackTrace, LogType type)
    {
        string entry = "";
        
        // Add timestamp if requested
        if (includeTimestamp)
        {
            entry += $"[{DateTime.Now:HH:mm:ss.fff}] ";
        }
        
        // Add log type if requested
        if (includeLogType)
        {
            entry += $"[{type}] ";
        }
        
        // Add the actual log message
        entry += logString;
        
        return entry;
    }
    
    private void WriteToFile(string logEntry)
    {
        // Use StreamWriter to append to the file
        using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
        {
            writer.WriteLine(logEntry);
        }
    }
    
    private void ClearLogFile()
    {
        try
        {
            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }
            logCount = 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error clearing log file: {e.Message}");
        }
    }
    
    private void TrimLogFile()
    {
        try
        {
            if (!File.Exists(logFilePath)) return;
            
            // Read all lines
            string[] allLines = File.ReadAllLines(logFilePath);
            
            // Keep only the last maxLogEntries
            if (allLines.Length > maxLogEntries)
            {
                string[] trimmedLines = new string[maxLogEntries];
                Array.Copy(allLines, allLines.Length - maxLogEntries, trimmedLines, 0, maxLogEntries);
                
                // Write back to file
                File.WriteAllLines(logFilePath, trimmedLines);
                logCount = maxLogEntries;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error trimming log file: {e.Message}");
        }
    }
    
    /// <summary>
    /// Manually clear the log file (can be called from inspector or other scripts)
    /// </summary>
    [ContextMenu("Clear Log File")]
    public void ClearLogFileManually()
    {
        ClearLogFile();
        Debug.Log("Log file cleared manually");
    }
    
    /// <summary>
    /// Open the folder containing the log file in the file explorer
    /// </summary>
    [ContextMenu("Open Log Folder")]
    public void OpenLogFolder()
    {
        string folderPath = System.IO.Path.GetDirectoryName(logFilePath);
        
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        System.Diagnostics.Process.Start("explorer.exe", folderPath);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        System.Diagnostics.Process.Start("open", folderPath);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        System.Diagnostics.Process.Start("xdg-open", folderPath);
#endif
        Debug.Log($"Opening log folder: {folderPath}");
    }
    
    /// <summary>
    /// Get the current log file path
    /// </summary>
    public string GetLogFilePath()
    {
        return logFilePath;
    }
}
