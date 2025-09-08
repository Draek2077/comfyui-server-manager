using System;

namespace ComfyUIServerManagerModern.Models;

/// <summary>
/// Represents a single, raw log entry with a timestamp.
/// </summary>
public record LogEntry(DateTime Timestamp, string RawText);