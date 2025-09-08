using System;

namespace ComfyUIServerManagerModern.Services;

public enum ServerState { Stopped, Starting, Running }

public interface IProcessService
{
    ServerState CurrentState { get; }
    int? ProcessId { get; }
    event Action<ServerState> StateChanged;
    event Action<string> LogReceived;

    void Start();
    void Stop();
    void Restart();
    bool TryAttachToExistingProcess();
}