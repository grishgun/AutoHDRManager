using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public sealed class SingleInstance : IAsyncDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private Task? server;
    public bool IsPrimary { get; }
    public event Action? Activated;
    public Func<Guid, bool>? LaunchRequested { get; set; }
    public Func<string, bool>? VideoRequested { get; set; }
    public SingleInstance(string suffix = "")
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        pipeName = $"AutoHDRManager-{sid}-{Process.GetCurrentProcess().SessionId}{suffix}";
        // Object existence, not thread-affine mutex ownership, defines the lifetime of the primary.
        mutex = new Mutex(false, @"Local\" + pipeName, out bool created);
        IsPrimary = created;
    }
    public void StartListening() { if (IsPrimary && server == null) server = Task.Run(ListenAsync); }
    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var bytes = new byte[4]; int count = 0;
                while (count < bytes.Length) { int read = await pipe.ReadAsync(bytes.AsMemory(count), timeout.Token); if (read == 0) break; count += read; }
                if (count == 4 && Encoding.ASCII.GetString(bytes) == "OPEN") Activated?.Invoke();
                else if (count == 4 && Encoding.ASCII.GetString(bytes) == "LAUN")
                {
                    var payload = new byte[36];
                    await pipe.ReadExactlyAsync(payload, timeout.Token);
                    bool accepted = Guid.TryParseExact(Encoding.ASCII.GetString(payload), "D", out var id) && id != Guid.Empty && LaunchRequested?.Invoke(id) == true;
                    await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token);
                }
                else if (count == 4 && Encoding.ASCII.GetString(bytes) == "VIDO")
                {
                    await pipe.ReadExactlyAsync(bytes, timeout.Token);
                    int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
                    if (length is <= 0 or > 131040) continue;
                    byte[] payload = new byte[length]; await pipe.ReadExactlyAsync(payload, timeout.Token);
                    bool accepted = false;
                    try { accepted = VideoRequested?.Invoke(VideoPath.Validate(new UTF8Encoding(false, true).GetString(payload))) == true; }
                    catch (ArgumentException) { }
                    await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(250, stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        }
    }
    public async Task NotifyAsync(bool background)
        => await NotifyAsync(new LaunchRequest(null, background));
    public async Task NotifyAsync(LaunchRequest request)
    {
        if (request.Background && request.GameId == null && request.VideoPath == null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token);
        if (request.VideoPath is string file)
        {
            byte[] payload = Encoding.UTF8.GetBytes(VideoPath.Validate(file));
            byte[] header = new byte[8]; Encoding.ASCII.GetBytes("VIDO").CopyTo(header, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), payload.Length);
            await client.WriteAsync(header, timeout.Token); await client.WriteAsync(payload, timeout.Token);
        }
        else await client.WriteAsync(Encoding.ASCII.GetBytes(request.GameId is Guid id ? "LAUN" + id.ToString("D") : "OPEN"), timeout.Token);
        if (request.GameId != null || request.VideoPath != null)
        {
            var ack = new byte[1]; await client.ReadExactlyAsync(ack, timeout.Token);
            if (ack[0] != 1) throw new IOException("Auto HDR Manager не принял запрос запуска. Закройте старую версию и откройте обновлённую.");
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); if (server != null) await server.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        mutex.Dispose(); stop.Dispose();
    }
}
