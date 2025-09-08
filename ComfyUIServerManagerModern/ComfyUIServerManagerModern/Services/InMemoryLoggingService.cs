using System;
using System.Collections.Generic;
using ComfyUIServerManagerModern.Models;

namespace ComfyUIServerManagerModern.Services;

public class InMemoryLoggingService : ILoggingService
{
    private readonly List<LogEntry> _logs = new();

    public InMemoryLoggingService()
    {
        // Add some sample data to see something on startup
        AddLog("Application starting up.");
        AddLog("Initializing services.");
        AddLog("Configuration file not found, using defaults.");
    }

    public void AddLog(string message)
    {
        _logs.Add(new LogEntry(DateTime.Now, message));
    }

    public IEnumerable<LogEntry> GetLogs() => _logs;
}