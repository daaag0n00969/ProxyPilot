using System.Net;
using System.Net.Sockets;
using ProxyPilot.Core.Config;
using ProxyPilot.Core.Engine;
using Xunit;

namespace ProxyPilot.Tests;

public class Socks5Tests
{
    [Fact]
    public async Task Socks5_ConnectsThroughLocalMock()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () =>
        {
            using var incoming = await listener.AcceptTcpClientAsync(serverCts.Token);
            var stream = incoming.GetStream();
            var hello = new byte[3];
            await ProxyClient.ReadExactAsync(stream, hello, serverCts.Token);
            Assert.Equal(5, hello[0]);
            await stream.WriteAsync(new byte[] { 5, 0 }, serverCts.Token);
            var req = new byte[10];
            await ProxyClient.ReadExactAsync(stream, req, serverCts.Token);
            Assert.Equal(new byte[] { 5, 1, 0, 1 }, req[..4]);
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, serverCts.Token);
            var payload = new byte[4];
            await ProxyClient.ReadExactAsync(stream, payload, serverCts.Token);
            Assert.Equal("ping"u8.ToArray(), payload);
            await stream.WriteAsync("pong"u8.ToArray(), serverCts.Token);
        });

        var hops = new List<ProxyServer>
        {
            new() { Type = ProxyType.Socks5, Host = "127.0.0.1", Port = port }
        };
        using var client = await ProxyClient.ConnectAsync(hops, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 443), serverCts.Token);
        var stream = client.GetStream();
        await stream.WriteAsync("ping"u8.ToArray(), serverCts.Token);
        var reply = new byte[4];
        await ProxyClient.ReadExactAsync(stream, reply, serverCts.Token);
        Assert.Equal("pong"u8.ToArray(), reply);
        await server;
        listener.Stop();
    }
}
