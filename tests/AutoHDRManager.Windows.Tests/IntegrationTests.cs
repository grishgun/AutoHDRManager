using AutoHDRManager.Windows;
using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class IntegrationTests
{
    private sealed class Sink : IEventLog { public void Write(string name, object? details = null) { } }
    [Fact] public async Task ActiveDisplayCanBeReadWithoutWrites()
    {
        var hdr = new WindowsHdrController(new Sink()); var displays = await hdr.GetDisplaysAsync(); Assert.NotEmpty(displays);
        foreach (var display in displays) { var read = await hdr.ReadAsync(display.Id); Assert.Equal(display.Id, read.Id); }
    }
    [Fact] public void InteropLayoutsMatchMicrosoftHeader()
    {
        Assert.Equal(new[] { 8, 20, 20, 48, 72, 64, 32, 36, 24, 420 }, WindowsHdrController.StructureSizes.Values);
    }
    [Fact] public async Task ProcessSnapshotIncludesExactOwnPathAndCreationTime()
    {
        var processes = await new ProcessMonitor().SnapshotAsync(); var own = Assert.Single(processes, p => p.Identity.Pid == Environment.ProcessId);
        Assert.True(GamePaths.Equal(Environment.ProcessPath, own.Path)); Assert.True(own.Identity.CreationTime > 0);
    }
    [Fact] public async Task MonitorHonorsCancellation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessMonitor().SnapshotAsync(cancel.Token));
    }
    [Fact] public async Task SingleInstanceRoutesActivationAndStopsServer()
    {
        string suffix = "-test-" + Guid.NewGuid().ToString("N");
        await using var first = new SingleInstance(suffix); Assert.True(first.IsPrimary);
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += () => signal.TrySetResult(); first.StartListening();
        await using var second = new SingleInstance(suffix); Assert.False(second.IsPrimary);
        await second.NotifyAsync(false); await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact] public async Task MutexIsReleasedAfterShutdown()
    {
        string suffix = "-test-" + Guid.NewGuid().ToString("N");
        var first = new SingleInstance(suffix); first.StartListening(); await first.DisposeAsync();
        await using var next = new SingleInstance(suffix); Assert.True(next.IsPrimary);
    }
    [Fact] public void StartupCommandQuotesPortableExecutableAndUsesBackground()
    {
        Assert.Equal($"\"{Environment.ProcessPath}\" --background", StartupManager.ExpectedCommand);
        // Only reads existing registry state. Opt-in mutation tests are deliberately separate.
        _ = new StartupManager().IsEnabled;
    }
}
