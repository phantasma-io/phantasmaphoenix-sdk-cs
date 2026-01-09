using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class TokenMetadataBuilderIconTests
{
	private const string SamplePngIconDataUri =
		"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";
	private const string SampleWebpIconDataUri = "data:image/webp;base64,UklGRg==";
	private const string SampleSvgIconDataUri =
		"data:image/svg+xml;base64,PHN2ZyB4bWxucz0naHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmcnIHZpZXdCb3g9JzAgMCAyNCAyNCc+PHBhdGggZmlsbD0nI0Y0NDMzNicgZD0nTTcgNGg1YTUgNSAwIDAxMCAxMEg5djZIN3pNOSA2djZoM2EzIDMgMCAwMDAtNnonLz48L3N2Zz4='";

	[Fact]
	public void Accepts_png_icon_data_uris()
	{
		Should.NotThrow(() => TokenMetadataBuilder.BuildAndSerialize(BuildFields()));
	}

	[Fact]
	public void Accepts_jpeg_icon_data_uris()
	{
		var jpegPayload = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF });
		var jpegIcon = $"data:image/jpeg;base64,{jpegPayload}";

		Should.NotThrow(() => TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
		{
			["icon"] = jpegIcon
		})));
	}

	[Fact]
	public void Accepts_webp_icon_data_uris()
	{
		Should.NotThrow(() => TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
		{
			["icon"] = SampleWebpIconDataUri
		})));
	}

	[Fact]
	public void Rejects_svg_icon_data_uris()
	{
		var ex = Should.Throw<ArgumentException>(() =>
			TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
			{
				["icon"] = SampleSvgIconDataUri
			})));

		ex.Message.ShouldBe("Token metadata icon must be a base64-encoded data URI (PNG, JPEG, or WebP)");
	}

	[Fact]
	public void Rejects_icons_missing_base64_flag()
	{
		var legacySvgUri =
			"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E%3Cpath fill='%23F44336' d='M7 4h5a5 5 0 010 10H9v6H7zM9 6v6h3a3 3 0 000-6z'/%3E%3C/svg%3E";

		var ex = Should.Throw<ArgumentException>(() =>
			TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
			{
				["icon"] = legacySvgUri
			})));

		ex.Message.ShouldBe("Token metadata icon must be a base64-encoded data URI (PNG, JPEG, or WebP)");
	}

	[Fact]
	public void Rejects_unsupported_icon_mime_types()
	{
		var gifIcon = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==";

		var ex = Should.Throw<ArgumentException>(() =>
			TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
			{
				["icon"] = gifIcon
			})));

		ex.Message.ShouldBe("Token metadata icon must be a base64-encoded data URI (PNG, JPEG, or WebP)");
	}

	[Fact]
	public void Rejects_empty_icon_payloads()
	{
		var ex = Should.Throw<ArgumentException>(() =>
			TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
			{
				["icon"] = "data:image/png;base64,"
			})));

		ex.Message.ShouldBe("Token metadata icon must include a non-empty base64 payload");
	}

	[Fact]
	public void Rejects_invalid_icon_payloads()
	{
		var ex = Should.Throw<ArgumentException>(() =>
			TokenMetadataBuilder.BuildAndSerialize(BuildFields(new Dictionary<string, string>
			{
				["icon"] = "data:image/jpeg;base64,@@@"
			})));

		ex.Message.ShouldBe("Token metadata icon payload is not valid base64");
	}

	private static Dictionary<string, string> BuildFields(Dictionary<string, string>? overrides = null)
	{
		var fields = new Dictionary<string, string>
		{
			["name"] = "My test token!",
			["icon"] = SamplePngIconDataUri,
			["url"] = "http://example.com",
			["description"] = "My test token description"
		};

		if (overrides != null)
		{
			foreach (var kvp in overrides)
			{
				fields[kvp.Key] = kvp.Value;
			}
		}

		return fields;
	}
}
