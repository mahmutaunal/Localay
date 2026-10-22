namespace AlpWareStudio.Localay;

internal readonly record struct GameWindowIdentity(nint Handle, uint ProcessId)
{
    internal bool IsValid => Handle != 0 && ProcessId != 0;
}

internal readonly record struct GameWindowObservation(GameWindowIdentity Identity, bool Exists,
    bool Visible, bool Minimized, bool Foreground)
{
    internal bool IsActive => Exists && Visible && !Minimized && Foreground;
}

internal enum GameSessionState { Stopped, Waiting, Running, Paused }
internal enum GameSessionAction { None, Activate, Suspend, Stop }

// UI-owner serialized. OS observation and side effects live outside this portable policy.
internal sealed class GameSessionPolicy
{
    internal GameWindowIdentity Selected { get; private set; }
    internal GameSessionState State { get; private set; }
    internal bool IsArmed => State is GameSessionState.Waiting or GameSessionState.Running;
    internal bool IsTracking => State != GameSessionState.Stopped;

    internal void Select(GameWindowIdentity window) { Selected = window; Stop(); }
    internal void Start() { if (Selected.IsValid) State = GameSessionState.Waiting; }
    internal void Pause() { if (IsArmed) State = GameSessionState.Paused; }
    internal void Stop() => State = GameSessionState.Stopped;

    internal GameSessionAction Observe(GameWindowObservation window)
    {
        if (!IsTracking) return GameSessionAction.None;
        if (!window.Exists || window.Identity != Selected)
        {
            Selected = default;
            Stop();
            return GameSessionAction.Stop;
        }
        if (State == GameSessionState.Paused) return GameSessionAction.None;
        if (window.IsActive && State == GameSessionState.Waiting)
        {
            State = GameSessionState.Running;
            return GameSessionAction.Activate;
        }
        if (!window.IsActive && State == GameSessionState.Running)
        {
            State = GameSessionState.Waiting;
            return GameSessionAction.Suspend;
        }
        return GameSessionAction.None;
    }
}
