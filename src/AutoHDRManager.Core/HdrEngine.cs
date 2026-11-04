namespace AutoHDRManager.Core;

// All reads, writes and state transitions share one gate. No delayed callback can outlive its session.
public sealed class HdrEngine(IHdrController hdr, ISessionRepository journal, INotificationService notifications, IEventLog log, Settings settings)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Session? session;
    private DateTimeOffset due;
    private readonly Dictionary<Guid, DateTimeOffset> launches = [];
    private int attempts;
    private bool restoring;
    private bool recoveryChecked;
    private HashSet<ProcessIdentity> active = [];
    public HdrState State { get; private set; } = HdrState.Idle;
    public string? LastError { get; private set; }
    public Display? CurrentDisplay { get; private set; }
    public IReadOnlySet<ProcessIdentity> ActiveProcesses => active;
    public bool ManualOverride => session?.ManualOverride == true;
    public bool HasSession => session is not null;
    public async Task<bool> IsLaunchPendingAsync(Guid gameId, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return launches.ContainsKey(gameId); }
        finally { gate.Release(); }
    }

    public async Task TickAsync(IReadOnlyList<GameProcess> snapshot, IReadOnlyList<Game> games, DateTimeOffset now, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var previous = active;
            active = snapshot.Where(p => (p.Path != null && games.Any(g => GamePaths.Equal(g.ExecutablePath, p.Path))) || (p.Path == null && previous.Contains(p.Identity))).Select(p => p.Identity).ToHashSet();
            foreach (var p in active.Except(previous)) log.Write("GameDetected", new { p.Pid, p.CreationTime });
            foreach (var p in previous.Except(active)) log.Write("GameExited", new { p.Pid, p.CreationTime });
            if (!recoveryChecked)
            {
                recoveryChecked = true;
                session = journal.Load();
                if (session != null)
                {
                    // A process restart leaves an unobserved interval. Matching HDR alone does not prove ownership.
                    session.ManualOverride = true;
                    journal.Save(session);
                    LastError = "Найдена незавершённая сессия. Состояние HDR сохранено: изменения во время перерыва неизвестны.";
                    log.Write("CrashRecovery", new { session.Id, LiveProcesses = active.Count, Decision = "PreserveUserState" });
                }
            }
            if (session != null)
            {
                if (!session.Processes.ToHashSet().SetEquals(active)) { session.Processes = active.ToList(); journal.Save(session); }
                await ObserveAsync(token);
            }
            else
            {
                var displays = await hdr.GetDisplaysAsync(token);
                CurrentDisplay = settings.DisplayId is { Length: > 0 } id ? displays.FirstOrDefault(d => d.Id == id) : displays.FirstOrDefault(d => d.Supported) ?? displays.FirstOrDefault();
            }
            foreach (var launch in launches.ToArray())
            {
                if (snapshot.Any(p => p.Path != null && games.Any(g => g.Id == launch.Key && GamePaths.Equal(g.ExecutablePath, p.Path)))) launches.Remove(launch.Key);
                else if (launch.Value <= now) { launches.Remove(launch.Key); log.Write("LaunchTimeout", new { GameId = launch.Key }); }
            }
            bool needed = active.Count > 0 || launches.Count > 0;
            if (!settings.Monitoring)
            {
                launches.Clear();
                if (session != null)
                {
                    if (!restoring) { restoring = true; attempts = 0; due = now; }
                    if (now >= due && attempts < 3) await RestoreAsync(now, token);
                }
                if (session == null) State = HdrState.Suspended;
                return;
            }
            if (session == null && needed) await BeginAsync(now, token);
            if (session == null) { State = HdrState.Idle; return; }
            if (session.ManualOverride)
            {
                State = HdrState.Suspended;
                if (!needed) Clear();
                return;
            }
            if (!needed)
            {
                if (State is not (HdrState.WaitingToRestore or HdrState.RestoringHDR) && !(State == HdrState.Error && restoring))
                {
                    due = now.AddSeconds(settings.RestoreDelaySeconds); attempts = 0; restoring = true; State = HdrState.WaitingToRestore;
                }
                if (now >= due && attempts < 3) await RestoreAsync(now, token);
                return;
            }
            if (restoring) { restoring = false; attempts = 0; State = session.LastConfirmed ? HdrState.HDRActive : HdrState.WaitingToEnable; due = now.AddSeconds(settings.EnableDelaySeconds); }
            if (State is HdrState.WaitingToEnable or HdrState.Error && !restoring && now >= due && attempts < 3) await EnableAsync(now, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { Fail(ex); }
        finally { gate.Release(); }
    }

    private async Task BeginAsync(DateTimeOffset now, CancellationToken token)
    {
        State = HdrState.GameDetected;
        var display = CurrentDisplay ?? throw new InvalidOperationException("Активный дисплей не найден.");
        if (!display.Supported) throw new InvalidOperationException("Выбранный дисплей не поддерживает управление HDR.");
        session = new Session { Started = now, DisplayId = display.Id, Original = display.Enabled, LastConfirmed = display.Enabled, Processes = active.ToList() };
        journal.Save(session);
        log.Write("SessionStarted", new { session.Id, Original = display.Enabled });
        attempts = 0; restoring = false; LastError = null;
        due = now.AddSeconds(settings.EnableDelaySeconds);
        State = display.Enabled ? HdrState.HDRActive : HdrState.WaitingToEnable;
    }
    private async Task ObserveAsync(CancellationToken token)
    {
        if (session == null) return;
        try
        {
            CurrentDisplay = await hdr.ReadAsync(session.DisplayId, token);
            if (!session.ManualOverride && (CurrentDisplay.Enabled != session.LastConfirmed || !CurrentDisplay.Supported))
            {
                session.ManualOverride = true; journal.Save(session);
                log.Write("ExternalHdrChange", new { session.Id, CurrentDisplay.Enabled });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            // A missing display or failed read destroys continuity; never restore using stale state.
            session.ManualOverride = true; journal.Save(session); throw;
        }
    }
    private async Task EnableAsync(DateTimeOffset now, CancellationToken token)
    {
        State = HdrState.EnablingHDR;
        try
        {
            await ChangeAsync(true, token);
            State = HdrState.HDRActive; attempts = 0;
            if (settings.Notifications) notifications.Show("HDR включён", "Игровая сессия активна.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { attempts++; due = now.AddSeconds(Math.Pow(2, attempts)); Fail(ex); }
    }
    private async Task ChangeAsync(bool enabled, CancellationToken token)
    {
        if (session == null) return;
        await ObserveAsync(token);
        if (session.ManualOverride) throw new InvalidOperationException("Обнаружено внешнее изменение HDR. Автоматическое управление остановлено до завершения сессии.");
        if (CurrentDisplay!.Enabled == enabled) return;
        session.WritePending = true; journal.Save(session);
        log.Write("HdrWriteAttempt", new { session.Id, Enabled = enabled });
        try
        {
            await hdr.SetAsync(session.DisplayId, enabled, token);
            CurrentDisplay = await hdr.ReadAsync(session.DisplayId, token);
            if (CurrentDisplay.Enabled != enabled || (enabled && !CurrentDisplay.Active)) throw new InvalidOperationException("Windows не подтвердила запрошенный режим HDR.");
            session.LastConfirmed = enabled; session.ChangedByUs = true; session.WritePending = false;
            journal.Save(session);
            log.Write("HdrWriteConfirmed", new { session.Id, Enabled = enabled });
        }
        catch
        {
            // If a failed API call nevertheless changed state, ownership is ambiguous. Preserve it.
            try { var actual = await hdr.ReadAsync(session.DisplayId, CancellationToken.None); if (actual.Enabled != session.LastConfirmed) session.ManualOverride = true; }
            catch { session.ManualOverride = true; }
            session.WritePending = false; journal.Save(session); throw;
        }
    }
    private async Task RestoreAsync(DateTimeOffset now, CancellationToken token)
    {
        State = HdrState.RestoringHDR;
        try { await EndAsync(token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { attempts++; due = now.AddSeconds(Math.Pow(2, attempts)); Fail(ex); }
    }
    private async Task EndAsync(CancellationToken token)
    {
        if (session == null) return;
        await ObserveAsync(token);
        if (session.ChangedByUs && !session.ManualOverride && session.LastConfirmed != session.Original)
        {
            await ChangeAsync(session.Original, token);
            if (settings.Notifications) notifications.Show("HDR восстановлен", session.Original ? "Исходный режим HDR." : "Исходный режим SDR.");
        }
        Clear();
    }
    private void Clear() { journal.Clear(); session = null; launches.Clear(); restoring = false; attempts = 0; State = HdrState.Idle; }
    private void Fail(Exception ex)
    {
        State = HdrState.Error;
        if (LastError != ex.Message) { log.Write("HdrError", new { Error = ex.Message }); if (settings.Notifications) notifications.Show("Ошибка HDR", ex.Message); }
        LastError = ex.Message;
    }
    public async Task PrepareLaunchAsync(Game game, DateTimeOffset now, CancellationToken token = default)
    {
        GamePaths.Validate(game);
        await gate.WaitAsync(token);
        try
        {
            if (!settings.Monitoring) throw new InvalidOperationException("Сначала возобновите автоматическое управление HDR.");
            if (!recoveryChecked) throw new InvalidOperationException("Дождитесь первой проверки состояния HDR.");
            if (session == null)
            {
                var displays = await hdr.GetDisplaysAsync(token);
                CurrentDisplay = settings.DisplayId is { Length: > 0 } id ? displays.FirstOrDefault(d => d.Id == id) : displays.FirstOrDefault(d => d.Supported);
                await BeginAsync(now, token);
            }
            if (session!.ManualOverride) throw new InvalidOperationException("HDR переопределён вручную. Дождитесь завершения текущей сессии.");
            await ChangeAsync(true, token);
            if (CurrentDisplay is not { Enabled: true, Active: true }) throw new InvalidOperationException("Windows не подтвердила активный HDR. Игра не запущена.");
            restoring = false; State = HdrState.HDRActive;
            launches[game.Id] = now.AddSeconds(settings.LaunchTimeoutSeconds);
        }
        finally { gate.Release(); }
    }
    public async Task DispatchLaunchAsync(Game game, Action launch, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!settings.Monitoring || session == null || !launches.ContainsKey(game.Id)) throw new InvalidOperationException("HDR-запуск отменён. Возобновите мониторинг и повторите запуск.");
            await ObserveAsync(token);
            if (session.ManualOverride || CurrentDisplay is not { Enabled: true, Active: true }) throw new InvalidOperationException("HDR изменился перед запуском. Игра не запущена.");
            token.ThrowIfCancellationRequested();
            launch();
            launches[game.Id] = DateTimeOffset.UtcNow.AddSeconds(settings.LaunchTimeoutSeconds);
        }
        finally { gate.Release(); }
    }
    public async Task CancelLaunchAsync(CancellationToken token = default, Guid? gameId = null)
    {
        await gate.WaitAsync(token);
        try { if (gameId is Guid id) launches.Remove(id); else launches.Clear(); if (active.Count == 0 && launches.Count == 0) await EndAsync(token); }
        finally { gate.Release(); }
    }
    public async Task ShutdownAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { await EndAsync(token); log.Write("Shutdown"); }
        finally { gate.Release(); }
    }
}
