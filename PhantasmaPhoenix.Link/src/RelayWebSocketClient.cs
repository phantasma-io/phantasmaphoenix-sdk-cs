using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PhantasmaPhoenix.Link.WebSockets;
using PhantasmaPhoenix.Protocol;

namespace PhantasmaPhoenix.Link;

/// <summary>
/// Outbound WebSocket implementation of <see cref="ILinkRelaySocketFactory"/> for the
/// Phantasma Link relay, built on the SAME frame code as <see cref="LinkServer"/> (the
/// WebSocket class masks outgoing frames in client mode), so the wallet ships no new
/// networking dependency and Unity keeps one battle-tested socket path. Supports plain
/// ws:// (local testing) and wss:// via SslStream (production behind the reverse proxy).
/// </summary>
public sealed class RelayWebSocketClient : ILinkRelaySocketFactory
{
	private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	public ILinkRelaySocket Connect(string url, Action onOpen, Action<string> onText, Action onClosed)
	{
		var socket = new ClientSocket(url, onOpen, onText, onClosed);
		socket.Start();
		return socket;
	}

	private sealed class ClientSocket : ILinkRelaySocket
	{
		private readonly string _url;
		private readonly Action _onOpen;
		private readonly Action<string> _onText;
		private readonly Action _onClosed;
		private readonly object _sendGate = new object();
		private TcpClient? _tcp;
		private WebSocket? _webSocket;
		private volatile bool _closed;
		private int _closedSignalled; // 0/1; the closed callback must fire exactly once

		public ClientSocket(string url, Action onOpen, Action<string> onText, Action onClosed)
		{
			_url = url;
			_onOpen = onOpen;
			_onText = onText;
			_onClosed = onClosed;
		}

		/// <summary>Dial + handshake + read loop on a background thread; the caller's
		/// thread (the wallet UI) never blocks on the network.</summary>
		public void Start()
		{
			var thread = new Thread(Run) { IsBackground = true, Name = "phantasma-link-relay-ws" };
			thread.Start();
		}

		public void SendText(string text)
		{
			// The frame writer is not re-entrant; serialize writers. Failures surface in
			// the read loop as a dead stream, which signals the close exactly once.
			lock (_sendGate)
			{
				try
				{
					_webSocket?.Send(text);
				}
				catch
				{
					Close();
				}
			}
		}

		public void Close()
		{
			_closed = true;
			try
			{
				_tcp?.Close();
			}
			catch
			{
				// Tearing down an already-dead socket is harmless.
			}
			SignalClosed();
		}

		private void SignalClosed()
		{
			if (Interlocked.Exchange(ref _closedSignalled, 1) == 0)
			{
				_onClosed();
			}
		}

		private void Run()
		{
			try
			{
				var uri = new Uri(_url);
				var secure = uri.Scheme == "wss";
				if (uri.Scheme != "ws" && !secure)
				{
					throw new ArgumentException($"Unsupported relay scheme: {uri.Scheme}");
				}
				var port = uri.IsDefaultPort ? (secure ? 443 : 80) : uri.Port;

				_tcp = Dial(uri.Host, port);
				Stream stream = _tcp.GetStream();
				if (secure)
				{
					// Standard certificate validation; the production relay sits behind the
					// reverse proxy with an ordinary publicly-trusted certificate.
					var ssl = new SslStream(stream, false);
					ssl.AuthenticateAsClient(uri.Host);
					stream = ssl;
				}

				PerformHandshake(stream, uri);
				if (_closed)
				{
					return;
				}

				// Same frame engine as the wallet's own server; isClient=true makes the
				// writer mask outgoing frames as RFC 6455 requires of clients.
				_webSocket = new WebSocket(() => new MemoryStream(), stream, 30000, null, false, true, null);
				_onOpen();

				// Read loop: accumulate fragmented text frames into complete messages.
				// Pings are answered inside Receive(); binary frames are not part of the
				// relay protocol and are skipped.
				var pending = new StringBuilder();
				while (!_closed)
				{
					var result = _webSocket.Receive();
					if (result.MessageType == WebSocketMessageType.Close)
					{
						break;
					}
					if (result.MessageType != WebSocketMessageType.Text || result.Bytes == null)
					{
						continue;
					}
					pending.Append(Encoding.UTF8.GetString(result.Bytes, 0, result.Count));
					if (result.EndOfMessage)
					{
						var message = pending.ToString();
						pending.Clear();
						_onText(message);
					}
				}
			}
			catch
			{
				// Any transport failure (dial, TLS, handshake, mid-stream) ends the same
				// way: the connection is dead and the owner decides about reconnecting.
			}
			finally
			{
				try
				{
					_tcp?.Close();
				}
				catch
				{
					// Already torn down.
				}
				SignalClosed();
			}
		}

		/// <summary>Connect trying EVERY resolved address. `TcpClient.Connect(host, ...)`
		/// is not reliable across runtimes when a name maps to several stacks: a host
		/// listening only on [::1] (e.g. "localhost" bound by the relay) is unreachable
		/// from a runtime that stops after trying 127.0.0.1 - Unity's Mono did exactly
		/// that, leaving the wallet in an endless reconnect loop. Explicit iteration
		/// makes the dial deterministic everywhere.</summary>
		private static TcpClient Dial(string host, int port)
		{
			var addresses = System.Net.Dns.GetHostAddresses(host);
			Exception? last = null;
			foreach (var address in addresses)
			{
				TcpClient? candidate = null;
				try
				{
					candidate = new TcpClient(address.AddressFamily);
					candidate.Connect(address, port);
					return candidate;
				}
				catch (Exception ex)
				{
					candidate?.Close();
					last = ex;
				}
			}
			throw last ?? new SocketException((int)SocketError.HostNotFound);
		}

		/// <summary>Client side of the RFC 6455 opening handshake, verified strictly:
		/// a wrong status or accept hash is a hard failure, never a half-open socket.</summary>
		private static void PerformHandshake(Stream stream, Uri uri)
		{
			var keyBytes = new byte[16];
			using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(keyBytes);
			}
			var key = Convert.ToBase64String(keyBytes);
			var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

			var request =
				$"GET {path} HTTP/1.1\r\n" +
				$"Host: {uri.Host}:{uri.Port}\r\n" +
				"Upgrade: websocket\r\n" +
				"Connection: Upgrade\r\n" +
				$"Sec-WebSocket-Key: {key}\r\n" +
				"Sec-WebSocket-Version: 13\r\n" +
				"\r\n";
			var requestBytes = Encoding.ASCII.GetBytes(request);
			stream.Write(requestBytes, 0, requestBytes.Length);
			stream.Flush();

			// Read headers byte-by-byte up to the blank line; the response has no body, so
			// nothing beyond the handshake is consumed from the stream.
			var header = new StringBuilder();
			var tail = new char[4];
			while (true)
			{
				var b = stream.ReadByte();
				if (b < 0)
				{
					throw new EndOfStreamException("Relay closed during the WebSocket handshake");
				}
				header.Append((char)b);
				tail[0] = tail[1];
				tail[1] = tail[2];
				tail[2] = tail[3];
				tail[3] = (char)b;
				if (tail[0] == '\r' && tail[1] == '\n' && tail[2] == '\r' && tail[3] == '\n')
				{
					break;
				}
				if (header.Length > 16 * 1024)
				{
					throw new InvalidDataException("Relay handshake response too large");
				}
			}

			var response = header.ToString();
			var lines = response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0 || !lines[0].Contains(" 101 "))
			{
				throw new InvalidDataException($"Relay refused the WebSocket upgrade: {(lines.Length > 0 ? lines[0] : "<empty>")}");
			}

			string? accept = null;
			foreach (var line in lines)
			{
				var colon = line.IndexOf(':');
				if (colon > 0 && line.Substring(0, colon).Trim().Equals("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase))
				{
					accept = line.Substring(colon + 1).Trim();
				}
			}
			string expected;
			using (var sha1 = SHA1.Create())
			{
				expected = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + HandshakeGuid)));
			}
			if (accept != expected)
			{
				throw new InvalidDataException("Relay handshake Sec-WebSocket-Accept mismatch");
			}
		}
	}
}
