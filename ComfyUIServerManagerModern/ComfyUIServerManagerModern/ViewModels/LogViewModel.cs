// (In the ViewModels folder)

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ComfyUIServerManagerModern.Models;
using ComfyUIServerManagerModern.Services;
using System.Collections.ObjectModel;

namespace ComfyUIServerManagerModern.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public LogViewModel(IProcessService processService, ILoggingService loggingService)
    {
        // Subscribe to log events
        processService.LogReceived += OnLogReceived;
        _loggingService = loggingService;
    }

    private void OnLogReceived(string rawLog)
    {
        _loggingService.AddLog(rawLog);
    }
}