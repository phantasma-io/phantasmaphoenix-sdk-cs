using System.Text.RegularExpressions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenMetadataBuilder
{
	private static readonly Regex IconDataUriPrefixPattern =
		new(@"^data:image/(png|jpeg|webp);base64,", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex Base64PayloadPattern =
		new(@"^[A-Za-z0-9+/]+={0,2}$", RegexOptions.Compiled);

	public static byte[] BuildAndSerialize(Dictionary<string, string> fields)
	{
		var requiredFields = new[] { "name", "icon", "url", "description" };

		if (fields == null || fields.Count < requiredFields.Length)
		{
			throw new ArgumentException("Token metadata is mandatory", nameof(fields));
		}

		var missing = requiredFields
			.Where(field => !fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
			.ToArray();

		if (missing.Length > 0)
		{
			throw new ArgumentException(
				$"Token metadata is missing required fields: {string.Join(", ", missing)}",
				nameof(fields));
		}

		ValidateIcon(fields["icon"]);

		var metadataFields = fields
			.Select(f => new VmNamedDynamicVariable
			{
				name = new SmallString(f.Key),
				value = new VmDynamicVariable(f.Value)
			})
			.ToArray();

		// Create a carbon structure for the token metadata
		using MemoryStream metadataBuffer = new();
		using BinaryWriter wMetadata = new(metadataBuffer);
		wMetadata.Write(new VmDynamicStruct
		{
			fields = metadataFields
		});

		return metadataBuffer.ToArray();
	}

	private static void ValidateIcon(string icon)
	{
		var candidate = icon?.Trim();
		if (string.IsNullOrEmpty(candidate))
		{
			throw new ArgumentException("Token metadata icon must be a base64-encoded data URI (PNG, JPEG, or WebP)");
		}

		if (!IconDataUriPrefixPattern.IsMatch(candidate))
		{
			throw new ArgumentException("Token metadata icon must be a base64-encoded data URI (PNG, JPEG, or WebP)");
		}

		var commaIndex = candidate.IndexOf(',');
		var payload = commaIndex >= 0 ? candidate.Substring(commaIndex + 1).Trim() : string.Empty;
		if (payload.Length == 0)
		{
			throw new ArgumentException("Token metadata icon must include a non-empty base64 payload");
		}

		if (!Base64PayloadPattern.IsMatch(payload) || payload.Length % 4 != 0)
		{
			throw new ArgumentException("Token metadata icon payload is not valid base64");
		}

		try
		{
			var decoded = Convert.FromBase64String(payload);
			if (decoded.Length == 0)
			{
				throw new ArgumentException("Token metadata icon must include a non-empty base64 payload");
			}

			// Normalize to catch invalid base64 padding/characters that still decode.
			var normalizedPayload = Convert.ToBase64String(decoded).TrimEnd('=');
			var normalizedInput = payload.TrimEnd('=');
			if (!string.Equals(normalizedPayload, normalizedInput, StringComparison.Ordinal))
			{
				throw new ArgumentException("Token metadata icon payload is not valid base64");
			}
		}
		catch (FormatException)
		{
			throw new ArgumentException("Token metadata icon payload is not valid base64");
		}
	}
}
