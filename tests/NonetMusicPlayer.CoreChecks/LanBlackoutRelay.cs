using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

/// <summary>仅转发环回 TLS 字节的隔离夹具；黑屏式断网不向客户端伪造已认证心跳。</summary>
internal sealed class LanBlackoutRelay : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly int _target;
    private volatile bool _blocked;
    public int Port { get; }
    public bool Blocked { get => _blocked; set { _blocked = value; if (value) foreach (var client in _clients.Keys) client.Dispose(); } }
    public LanBlackoutRelay(int target) { _target = target; _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _ = AcceptAsync(); }
    private async Task AcceptAsync()
    {
        try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = ForwardAsync(client); } }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task ForwardAsync(TcpClient client)
    {
        using (client) using (var upstream = new TcpClient())
        {
            _clients[client] = 0;
            try
            {
                if (_blocked) return;
                await upstream.ConnectAsync(IPAddress.Loopback, _target, _stop.Token);
                var forward = client.GetStream().CopyToAsync(upstream.GetStream(), _stop.Token);
                var reverse = upstream.GetStream().CopyToAsync(client.GetStream(), _stop.Token);
                await Task.WhenAny(forward, reverse);
                client.Dispose(); upstream.Dispose();
                try { await Task.WhenAll(forward, reverse); } catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
            finally { _clients.TryRemove(client, out _); }
        }
    }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); foreach (var client in _clients.Keys) client.Dispose(); }
}
