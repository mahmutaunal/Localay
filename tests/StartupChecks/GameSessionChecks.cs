using AlpWareStudio.Localay;

internal static class GameSessionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var game = new GameWindowIdentity(100, 7);
        var active = new GameWindowObservation(game, true, true, false, true);
        var away = active with { Foreground = false };
        var session = new GameSessionPolicy();
        session.Start();
        check(!session.IsTracking, "Start cannot arm an unidentified window");
        session.Select(game);
        session.Start();
        check(session.State == GameSessionState.Waiting && session.IsArmed,
            "Start arms the selected game without starting desktop capture");
        check(session.Observe(away) == GameSessionAction.None && session.State == GameSessionState.Waiting,
            "waiting for foreground is stable and does not initiate processing");
        check(session.Observe(active) == GameSessionAction.Activate && session.State == GameSessionState.Running,
            "the selected foreground game activates the session");
        check(session.Observe(active) == GameSessionAction.None, "steady foreground does not restart OCR every tick");

        var lifetime = new PipelineLifetime();
        using var beforeAltTab = lifetime.Capture();
        check(session.Observe(away) == GameSessionAction.Suspend && !session.State.Equals(GameSessionState.Paused),
            "Alt+Tab suspends automatically without becoming a manual pause");
        lifetime.Invalidate();
        check(!beforeAltTab.TryApply(() => throw new Exception("stale work applied")) && beforeAltTab.Token.IsCancellationRequested,
            "pre-Alt+Tab provider and OCR leases cannot publish after invalidation");
        check(session.Observe(away) == GameSessionAction.None, "background polling does not repeat cancellation");
        check(session.Observe(active) == GameSessionAction.Activate, "returning to the game resumes an armed session");
        using var afterReturn = lifetime.Capture();
        bool freshApplied = false;
        check(afterReturn.TryApply(() => freshApplied = true) && freshApplied && !beforeAltTab.IsCurrent,
            "only the new pipeline generation may apply a result after returning");

        check(session.Observe(active with { Minimized = true }) == GameSessionAction.Suspend,
            "minimized windows suspend even if a foreground flag is stale");
        check(session.Observe(active with { Minimized = true }) == GameSessionAction.None,
            "minimization does not report the game as closed");
        check(session.Observe(active) == GameSessionAction.Activate, "restoring the foreground game resumes");
        check(session.Observe(active with { Visible = false }) == GameSessionAction.Suspend,
            "hidden game windows do not process or present translations");
        session.Observe(active);
        session.Pause();
        check(session.State == GameSessionState.Paused && !session.IsArmed,
            "manual pause disarms automatic resumption");
        check(session.Observe(away) == GameSessionAction.None && session.Observe(active) == GameSessionAction.None,
            "a manually paused session stays paused through focus changes");
        session.Start();
        check(session.Observe(active) == GameSessionAction.Activate, "explicit Resume re-arms a manual pause");
        session.Stop();
        check(session.Observe(active) == GameSessionAction.None && !session.IsTracking,
            "Stop cannot be undone by a later foreground event");

        foreach (var lost in new[] { active with { Exists = false },
            active with { Identity = new(100, 8) }, active with { Identity = new(101, 7) } })
        {
            session.Select(game); session.Start(); session.Observe(active);
            check(session.Observe(lost) == GameSessionAction.Stop && !session.Selected.IsValid && !session.IsTracking,
                "closed or replaced HWND/process identity stops and clears the selected game");
            session.Start();
            check(!session.IsTracking, "a lost game requires explicit reselection, not automatic rebinding");
        }
        session.Select(game); session.Start(); session.Pause();
        check(session.Observe(active with { Exists = false }) == GameSessionAction.Stop,
            "game closure is still detected while manually paused");
        session.Select(game); session.Start();
        check(session.Observe(active with { Exists = false }) == GameSessionAction.Stop,
            "game closure is detected before it ever reaches foreground");
        session.Select(game); session.Start(); session.Observe(active);
        session.Select(new(200, 9));
        check(session.State == GameSessionState.Stopped && session.Selected == new GameWindowIdentity(200, 9),
            "choosing another game never inherits a running session");

        // Exercise the reader's pure focus matcher, not user32 or a real Windows window.
        check(GameWindowObserver.IsForeground(game, game, game.Handle), "the exact game window matches foreground");
        check(GameWindowObserver.IsForeground(game, new(102, 7), game.Handle), "an owned in-process game dialog matches foreground");
        check(!GameWindowObserver.IsForeground(game, new(102, 7), 102), "an unrelated window in the same process is not the game");
        check(!GameWindowObserver.IsForeground(game, new(102, 8), game.Handle), "a foreign process cannot match via an owner handle");
        check(!GameWindowObserver.IsForeground(game, default, 0), "temporary null foreground waits without selecting another application");
    }
}
