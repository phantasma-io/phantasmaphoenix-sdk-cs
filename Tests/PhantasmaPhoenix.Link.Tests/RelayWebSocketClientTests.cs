using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Link.Tests;

/// <summary>
/// The outbound relay socket of the wallet against a local WebSocket server that plays the relay.
/// </summary>
public class RelayWebSocketClientTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private static int FreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	/// <summary>Starts a relay that accepts one connection; the task gives its server side.</summary>
	private static (HttpListener listener, Task<System.Net.WebSockets.WebSocket> accepted, string url) StartRelay()
	{
		var port = FreePort();
		var listener = new HttpListener();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		listener.Start();
		var accepted = Task.Run(async () =>
		{
			var context = await listener.GetContextAsync();
			return (System.Net.WebSockets.WebSocket)(await context.AcceptWebSocketAsync(null)).WebSocket;
		});
		return (listener, accepted, $"ws://127.0.0.1:{port}/relay");
	}

	// The wallet sends from its UI thread. A relay that reads slowly, or not at all, must not stop
	// that thread: the frames wait for the writer thread instead.
	[Fact]
	public async Task SendText_returns_at_once_while_the_relay_does_not_read()
	{
		var (listener, accepted, url) = StartRelay();
		var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var socket = new RelayWebSocketClient().Connect(url, () => opened.TrySetResult(true), _ => { }, () => { });
		try
		{
			(await Task.WhenAny(opened.Task, Task.Delay(Wait))).ShouldBe(opened.Task);
			using var server = await accepted;
			var large = new string('x', 32 * 1024 * 1024);

			var sending = Task.Run(() =>
			{
				socket.SendText(large);
				socket.SendText(large);
			});

			(await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(2)))).ShouldBe(sending);
		}
		finally
		{
			socket.Close();
			listener.Stop();
		}
	}

	[Fact]
	public async Task Frames_reach_the_relay_in_the_order_they_were_sent()
	{
		var (listener, accepted, url) = StartRelay();
		var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var socket = new RelayWebSocketClient().Connect(url, () => opened.TrySetResult(true), _ => { }, () => { });
		try
		{
			(await Task.WhenAny(opened.Task, Task.Delay(Wait))).ShouldBe(opened.Task);
			using var server = await accepted;
			for (var i = 0; i < 5; i++)
			{
				socket.SendText("frame " + i);
			}

			var received = new List<string>();
			var buffer = new byte[1024];
			using var timeout = new CancellationTokenSource(Wait);
			while (received.Count < 5)
			{
				var result = await server.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
				received.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
			}
			received.ShouldBe(Enumerable.Range(0, 5).Select(i => "frame " + i));
		}
		finally
		{
			socket.Close();
			listener.Stop();
		}
	}
}
