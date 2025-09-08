using System.Collections.Generic;
using ComfyUIServerManagerModern.Models;

namespace ComfyUIServerManagerModern.Services;

public interface ILoggingService
{
    void AddLog(string message);
    IEnumerable<LogEntry> GetLogs();
}