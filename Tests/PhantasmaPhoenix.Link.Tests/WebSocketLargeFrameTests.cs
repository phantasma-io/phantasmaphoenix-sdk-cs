using System.Text;
using PhantasmaPhoenix.Link.WebSockets;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Link.Tests;

public class WebSocketLargeFrameTests
{
	/// <summary>
	/// Builds the raw bytes of a single CLIENT->SERVER (masked) text frame per RFC 6455.
	/// Browsers and Node's `ws` may send a multi-megabyte message as ONE unfragmented frame,
	/// which is exactly the shape that used to kill the connection.
	/// </summary>
	private static byte[] BuildMaskedTextFrame(byte[] payload)
	{
		using var ms = new MemoryStream();
		ms.WriteByte(0x81); // FIN + text opcode

		var mask = new byte[] { 0x11, 0x22, 0x33, 0x44 };
		if (payload.Length <= 125)
		{
			ms.WriteByte((byte)(0x80 | payload.Length)); // mask bit + short length
		}
		else if (payload.Length <= ushort.MaxValue)
		{
			ms.WriteByte(0x80 | 126);
			ms.WriteByte((byte)(payload.Length >> 8));
			ms.WriteByte((byte)(payload.Length & 0xff));
		}
		else
		{
			ms.WriteByte(0x80 | 127);
			ulong len = (ulong)payload.Length;
			for (int i = 7; i >= 0; i--)
			{
				ms.WriteByte((byte)((len >> (8 * i)) & 0xff));
			}
		}

		ms.Write(mask, 0, mask.Length);
		for (int i = 0; i < payload.Length; i++)
		{
			ms.WriteByte((byte)(payload[i] ^ mask[i % 4]));
		}
		return ms.ToArray();
	}

	private static WebSocket BuildServerSocket(Stream stream)
	{
		// Server-side socket (isClient: false) reading straight from the prepared stream.
		return new WebSocket(() => new MemoryStream(), stream, 5000, null, false, false, null);
	}

	[Fact]
	public void Receives_a_2MB_single_frame_without_dropping()
	{
		// Live-test regression: a 2 MB single text frame used to overflow the fixed 64 KB
		// receive buffer and close the connection ("Frame too large to fit in buffer").
		var payload = Encoding.UTF8.GetBytes(new string('x', 2 * 1024 * 1024));
		using var stream = new MemoryStream(BuildMaskedTextFrame(payload));
		var socket = BuildServerSocket(stream);

		var result = socket.Receive();

		result.MessageType.ShouldBe(WebSocketMessageType.Text);
		result.EndOfMessage.ShouldBeTrue();
		result.Count.ShouldBe(payload.Length);
		result.Bytes.ShouldNotBeNull();
		result.Bytes!.Length.ShouldBe(payload.Length);
		result.Bytes[0].ShouldBe((byte)'x');
		result.Bytes[payload.Length - 1].ShouldBe((byte)'x');
	}

	[Fact]
	public void Small_frames_still_work_after_the_growth_change()
	{
		var payload = Encoding.UTF8.GetBytes("{\"plv\":5,\"id\":\"a\",\"method\":\"pha_getChains\"}");
		using var stream = new MemoryStream(BuildMaskedTextFrame(payload));
		var socket = BuildServerSocket(stream);

		var result = socket.Receive();

		result.MessageType.ShouldBe(WebSocketMessageType.Text);
		Encoding.UTF8.GetString(result.Bytes!, 0, result.Count).ShouldContain("pha_getChains");
	}

	[Fact]
	public void Frames_above_the_ceiling_are_rejected_not_buffered()
	{
		// The 32 MiB ceiling must hold (memory-exhaustion guard): a 33 MiB frame is refused.
		var payload = new byte[33 * 1024 * 1024];
		using var stream = new MemoryStream(BuildMaskedTextFrame(payload));
		var socket = BuildServerSocket(stream);

		Should.Throw<InternalBufferOverflowException>(() => socket.Receive());
	}
}
