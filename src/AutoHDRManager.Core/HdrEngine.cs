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
    private DateTimeOffset probeDue;
    private string? reconnectCandidate;
    private DateTimeOffset reconnectSeen;
    private string? topology;
    private string? deferredReason;
    public static readonly TimeSpan PendingRestoreLimit = TimeSpan.FromMinutes(10);
    public const int MaxRestoreProbes = 120;
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
            if (session != null)
            {
                if (needed && settings.Monitoring && session.RestoreDeadline != null)
                {
                    log.Write("PendingRestoreCancelled", new { session.Id, Reason = "GameStillNeedsHdr" });
                    session.RestoreDeadline = null; session.RestoreProbeCount = 0; journal.Save(session);
                }
                if ((!needed || !settings.Monitoring) && session.DisplayUnavailableSince != null) PendingRestore(now);
                if (!session.ManualOverride && !await ObserveAsync(now, token))
                {
                    if (!needed || !settings.Monitoring) PendingRestore(now);
                    if (session.ManualOverride && (!needed || !settings.Monitoring)) Clear();
                    return;
                }
            }
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
        session = new Session { Started = now, DisplayId = display.Id, DisplayFingerprint = display.Fingerprint, DisplayHardwareId = display.HardwareId,
            Original = display.Enabled, LastConfirmed = display.Enabled, Processes = active.ToList() };
        journal.Save(session);
        log.Write("SessionStarted", new { session.Id, Original = display.Enabled });
        attempts = 0; restoring = false; LastError = null;
        due = now.AddSeconds(settings.EnableDelaySeconds);
        State = display.Enabled ? HdrState.HDRActive : HdrState.WaitingToEnable;
    }
    private void LoseOwnership(string reason)
    {
        if (session == null || session.ManualOverride) return;
        session.ManualOverride = true; session.OwnershipLostReason = reason; journal.Save(session);
        State = HdrState.Suspended;
        LastError = "Автоматическое восстановление HDR отменено: " + reason;
        log.Write("HdrOwnershipLost", new { session.Id, Reason = reason, session.Original });
    }

    private void PendingRestore(DateTimeOffset now)
    {
        if (session == null || session.ManualOverride) return;
        restoring = true; State = HdrState.WaitingToRestore;
        if (session.RestoreDeadline == null)
        {
            session.RestoreDeadline = now + PendingRestoreLimit; session.RestoreProbeCount = 0; journal.Save(session);
            log.Write("HdrRestorePending", new { session.Id, session.Original, session.RestoreDeadline, Reason = "DisplayUnavailable" });
        }
    }

    private void DisplayUnavailable(DateTimeOffset now, string reason)
    {
        if (session == null) return;
        CurrentDisplay = null; reconnectCandidate = null;
        if (session.DisplayUnavailableSince == null)
        {
            session.DisplayUnavailableSince = now; journal.Save(session);
            log.Write("DisplayUnavailable", new { session.Id, session.DisplayId, Reason = reason, session.Original, session.LastConfirmed });
        }
        probeDue = now.AddSeconds(5);
        State = restoring ? HdrState.WaitingToRestore : HdrState.Suspended;
        LastError = "Целевой дисплей временно недоступен. Исходное состояние HDR сохранено.";
    }

    private void Deferred(string reason)
    {
        if (reason == deferredReason) return;
        deferredReason = reason;
        log.Write("DisplayReconnectDeferred", new { session!.Id, Reason = reason });
    }

    private bool CheckObservedState(Display display)
    {
        if (display.Enabled != session!.LastConfirmed)
        {
            log.Write("ExternalHdrChange", new { session.Id, display.Enabled, session.LastConfirmed });
            LoseOwnership("ObservedHdrChanged"); return false;
        }
        if (!display.Supported) { LoseOwnership("HdrSupportChanged"); return false; }
        return true;
    }

    private async Task<bool> ObserveAsync(DateTimeOffset now, CancellationToken token)
    {
        if (session == null || session.ManualOverride) return false;
        if (session.DisplayUnavailableSince != null)
        {
            if (session.RestoreDeadline is { } deadline && (now >= deadline || session.RestoreProbeCount >= MaxRestoreProbes))
            {
                log.Write("HdrRestoreAbandoned", new { session.Id, Reason = "PendingRestoreLimitReached", session.RestoreProbeCount });
                LoseOwnership("PendingRestoreLimitReached"); return false;
            }
            if (now < probeDue) return false;
            probeDue = now.AddSeconds(5);
            if (session.RestoreDeadline != null) { session.RestoreProbeCount++; journal.Save(session); }
            IReadOnlyList<Display> displays;
            try { displays = await hdr.GetDisplaysAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { reconnectCandidate = null; Deferred("TopologyReadFailed: " + ex.Message); return false; }
            string observedTopology = string.Join("|", displays.OrderBy(d => d.Id).Select(d => d.Id + ":" + d.Fingerprint));
            if (observedTopology != topology)
            {
                topology = observedTopology;
                log.Write("DisplayTopologyObserved", new { session.Id, Displays = displays.Select(d => new { d.Id, d.Fingerprint, d.HardwareId }).ToArray() });
            }
            var exact = displays.Where(d => d.Id == session.DisplayId).ToArray();
            var hardware = session.DisplayHardwareId == null ? [] : displays.Where(d => d.HardwareId == session.DisplayHardwareId).ToArray();
            if (exact.Length > 1 || hardware.Length > 1) { LoseOwnership("AmbiguousDisplayIdentity"); return false; }
            var candidate = exact.SingleOrDefault() ?? hardware.SingleOrDefault();
            if (candidate == null)
            {
                reconnectCandidate = null; Deferred(session.DisplayHardwareId == null ? "OriginalDevicePathNotPresent_NoUniqueHardwareId" : "OriginalPhysicalDisplayNotPresent"); return false;
            }
            if (session.DisplayFingerprint == null || candidate.Fingerprint == null || candidate.Fingerprint != session.DisplayFingerprint ||
                (session.DisplayHardwareId != null && candidate.HardwareId != session.DisplayHardwareId))
            { LoseOwnership("DisplayIdentityUnverifiableOrChanged"); return false; }
            if (!CheckObservedState(candidate)) return false;
            if (candidate.Enabled && !candidate.Active) { reconnectCandidate = null; Deferred("HdrNotYetActive"); return false; }
            if (reconnectCandidate != candidate.Id)
            {
                reconnectCandidate = candidate.Id; reconnectSeen = now; probeDue = now.AddSeconds(1);
                log.Write("DisplayReconnectCandidate", new { session.Id, PreviousPath = session.DisplayId, CandidatePath = candidate.Id, Match = exact.Length == 1 ? "DevicePathAndEdid" : "UniqueHardwareIdAndEdid" });
                return false;
            }
            if (now - reconnectSeen < TimeSpan.FromSeconds(1)) return false;
            string previousPath = session.DisplayId;
            session.DisplayId = candidate.Id; session.DisplayUnavailableSince = null; journal.Save(session);
            CurrentDisplay = candidate; reconnectCandidate = null; deferredReason = null; LastError = null;
            State = restoring ? HdrState.WaitingToRestore : candidate.Enabled ? HdrState.HDRActive : HdrState.WaitingToEnable;
            log.Write("DisplayReconnected", new { session.Id, PreviousPath = previousPath, session.DisplayId, session.Original, session.LastConfirmed });
            return true;
        }
        try
        {
            CurrentDisplay = await hdr.ReadAsync(session.DisplayId, token);
            if (CurrentDisplay.Id != session.DisplayId || (session.DisplayFingerprint != null && CurrentDisplay.Fingerprint != session.DisplayFingerprint))
            { LoseOwnership("DisplayIdentityChanged"); return false; }
            CheckObservedState(CurrentDisplay);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { DisplayUnavailable(now, ex.Message); return false; }
    }
    private async Task EnableAsync(DateTimeOffset now, CancellationToken token)
    {
        State = HdrState.EnablingHDR;
        try
        {
            await ChangeAsync(true, now, token);
            State = HdrState.HDRActive; attempts = 0;
            if (settings.Notifications) notifications.Show("HDR включён", "Игровая сессия активна.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { attempts++; due = now.AddSeconds(Math.Pow(2, attempts)); Fail(ex); }
    }
    private async Task ChangeAsync(bool enabled, DateTimeOffset now, CancellationToken token)
    {
        if (session == null) return;
        if (!await ObserveAsync(now, token)) throw new InvalidOperationException("Дисплей недоступен или его идентификация не подтверждена.");
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
            try
            {
                var actual = await hdr.ReadAsync(session.DisplayId, CancellationToken.None);
                if (actual.Enabled != session.LastConfirmed || actual.Id != session.DisplayId ||
                    (session.DisplayFingerprint != null && actual.Fingerprint != session.DisplayFingerprint)) LoseOwnership("UnconfirmedWriteOutcome");
            }
            catch { LoseOwnership("UnconfirmedWriteAndDisplayUnavailable"); }
            session.WritePending = false; journal.Save(session); throw;
        }
    }
    private async Task RestoreAsync(DateTimeOffset now, CancellationToken token)
    {
        State = HdrState.RestoringHDR;
        try { await EndAsync(now, token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            attempts++; due = now.AddSeconds(Math.Pow(2, attempts)); Fail(ex);
            if (attempts == 3) log.Write("HdrRestoreRetriesExhausted", new { session?.Id, Attempts = attempts });
        }
    }
    private async Task EndAsync(DateTimeOffset now, CancellationToken token)
    {
        if (session == null) return;
        if (session.ManualOverride) { Clear(); return; }
        if (!await ObserveAsync(now, token))
        {
            if (session.ManualOverride) Clear(); else PendingRestore(now);
            return;
        }
        if (session.ChangedByUs && !session.ManualOverride && session.LastConfirmed != session.Original)
        {
            await ChangeAsync(session.Original, now, token);
            if (settings.Notifications) notifications.Show("HDR восстановлен", session.Original ? "Исходный режим HDR." : "Исходный режим SDR.");
        }
        Clear();
    }
    private void Clear()
    {
        if (session != null) log.Write("SessionEnded", new { session.Id, session.Original, Reason = session.OwnershipLostReason ?? (session.ManualOverride ? "ManualOverrideOrCrashRecovery" : "RestoredOrNoChangeRequired") });
        journal.Clear(); session = null; launches.Clear(); restoring = false; attempts = 0;
        reconnectCandidate = null; topology = null; deferredReason = null; State = HdrState.Idle;
    }
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
            await ChangeAsync(true, now, token);
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
            if (!await ObserveAsync(DateTimeOffset.UtcNow, token)) throw new InvalidOperationException("Дисплей недоступен перед запуском игры.");
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
        try { if (gameId is Guid id) launches.Remove(id); else launches.Clear(); if (active.Count == 0 && launches.Count == 0) await EndAsync(DateTimeOffset.UtcNow, token); }
        finally { gate.Release(); }
    }
    public async Task ShutdownAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { await EndAsync(DateTimeOffset.UtcNow, token); log.Write("Shutdown"); }
        finally { gate.Release(); }
    }
}
