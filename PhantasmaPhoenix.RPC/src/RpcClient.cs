using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Types;

namespace PhantasmaPhoenix.RPC;

/// <summary>
/// Minimal JSON-RPC/REST client using Newtonsoft.Json and SDK types
/// </summary>
public sealed class RpcClient : IDisposable
{
	private const int StreamCopyBufferSize = 81920;
	public const long DefaultMaxResponseBytes = 16L * 1024 * 1024;
	public const string ApiKeyHeaderName = "X-Api-Key";

	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;
	private readonly ILogger? _logger;
	private readonly JsonSerializerSettings _jsonSerializerSettings;
	private readonly JsonSerializer _jsonSerializer;
	private readonly int _maxRetries;
	private readonly TimeSpan _retryDelay;
	private readonly long _maxResponseBytes;
	private readonly string? _apiKey;

	/// <summary>
	/// Creates a new RPC client with optional external HttpClient and logging
	/// </summary>
	/// <param name="httpClient">HttpClient instance to use or null to create an internal one</param>
	/// <param name="logger">Logger instance or null to disable logging</param>
	/// <param name="maxRetries">Number of retry attempts for transient failures</param>
	/// <param name="retryDelayMs">Delay between retries in milliseconds</param>
	/// <param name="maxResponseBytes">Maximum accepted JSON-RPC response body size. Defaults to 16 MiB.</param>
	public RpcClient(HttpClient? httpClient = null, ILogger? logger = null, int maxRetries = 0, int retryDelayMs = 1000, long maxResponseBytes = DefaultMaxResponseBytes, string? apiKey = null)
	{
		if (maxResponseBytes <= 0)
			throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "maxResponseBytes must be positive");

		if (httpClient == null)
		{
			_httpClient = new HttpClient();
			_ownsHttpClient = true;
		}
		else
		{
			_httpClient = httpClient;
			_ownsHttpClient = false;
		}

		_logger = logger;
		_maxRetries = Math.Max(0, maxRetries);
		_retryDelay = TimeSpan.FromMilliseconds(Math.Max(0, retryDelayMs));
		_maxResponseBytes = maxResponseBytes;
		_apiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;

		// Keep original property names, ignore missing members, allow enums as string or int
		_jsonSerializerSettings = new JsonSerializerSettings
		{
			ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() },
			NullValueHandling = NullValueHandling.Ignore,
			MissingMemberHandling = MissingMemberHandling.Ignore,
			Converters = { new StringEnumConverter(new DefaultNamingStrategy(), allowIntegerValues: true) }
		};
		_jsonSerializer = JsonSerializer.Create(_jsonSerializerSettings);
	}

	/// <summary>Adds the configured API key header to an outgoing request, when one is set.</summary>
	private void ApplyHeaders(HttpRequestMessage request)
	{
		if (_apiKey != null)
		{
			request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, _apiKey);
		}
	}

	/// <summary>
	/// Sends a JSON-RPC request and returns the deserialized result
	/// </summary>
	/// <typeparam name="T">Type to deserialize the result into</typeparam>
	/// <param name="url">Full RPC endpoint URL</param>
	/// <param name="method">JSON-RPC method name</param>
	/// <param name="parameters">RPC parameters in method call order</param>
	/// <returns>Deserialized result of type T or default if no result</returns>
	public async Task<T?> SendRpcAsync<T>(string url, string method, params object[] parameters)
	{
		var req = new RpcRequest
		{
			jsonrpc = "2.0",
			method = method,
			id = Guid.NewGuid().ToString(),
			@params = parameters
		};

		// Serialize request
		var body = JsonConvert.SerializeObject(req, _jsonSerializerSettings);
		if (_logger?.IsEnabled(LogLevel.Information) == true)
		{
			var paramCount = parameters?.Length ?? 0;
			_logger.LogInformation("[RPC][Request] {Url} {Method} id={RequestId} params={ParamCount} bodyBytes={BodyBytes}", url, method, req.id, paramCount, Encoding.UTF8.GetByteCount(body));
		}

		if (_logger?.IsEnabled(LogLevel.Debug) == true)
		{
			_logger.LogDebug("[RPC][Request][Body] {Url} {Method} id={RequestId} {Json}", url, method, req.id, body);
		}

		for (int attempt = 0; attempt <= _maxRetries; attempt++)
		{
			try
			{
				var stopwatch = Stopwatch.StartNew();
				using var content = new StringContent(body, Encoding.UTF8, "application/json");
				using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
				ApplyHeaders(request);
				using var resp = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

				// Read raw response JSON
				var text = await ReadContentAsStringAsync(resp.Content).ConfigureAwait(false);
				stopwatch.Stop();
				if (_logger?.IsEnabled(LogLevel.Information) == true)
				{
					var bodyBytes = Encoding.UTF8.GetByteCount(text);
					_logger.LogInformation("[RPC][Response] {Url} {Status} id={RequestId} elapsedMs={ElapsedMs} bodyBytes={BodyBytes}", url, resp.StatusCode, req.id, stopwatch.ElapsedMilliseconds, bodyBytes);
				}

				if (_logger?.IsEnabled(LogLevel.Debug) == true)
				{
					_logger.LogDebug("[RPC][Response][Body] {Url} {Status} id={RequestId} {Json}", url, resp.StatusCode, req.id, text);
				}

				// Surface infrastructure rejections (e.g. 401 keys-only, 429 rate limit) clearly instead of
				// failing later as a JSON-RPC parse error - the body is not a JSON-RPC envelope in those cases.
				if (!resp.IsSuccessStatusCode)
				{
					throw new Exception($"[RPC][HTTP] {(int)resp.StatusCode} {resp.StatusCode}: {text.Trim()}");
				}

				var envelope = JObject.Parse(text);
				var env = envelope.ToObject<RpcResponse>(_jsonSerializer)
					?? throw new Exception("[RPC][Error] Invalid JSON-RPC response");

				if (env.id == null)
					throw new Exception($"[RPC][Error] Missing response id for request {req.id}");

				if (!env.id.Equals(req.id))
					throw new Exception($"[RPC][Error] Response id mismatch: got {env.id}, expected {req.id}");

				if (env.Error != null)
					throw new Exception($"[RPC][Error] {env.Error.Code}: {env.Error.Message}");

				if (!envelope.ContainsKey("result"))
					throw new Exception("[RPC][Error] Missing response result");

				if (env.Result == null)
					return default;

				// Fast path when result is already string
				if (typeof(T) == typeof(string) && env.Result is string s)
					return (T)(object)s;

				// Directly deserialize from JToken when available
				if (env.Result is JToken tok)
					return tok.ToObject<T>(_jsonSerializer);

				// Round-trip to respect converters/settings
				var resultJson = JsonConvert.SerializeObject(env.Result, _jsonSerializerSettings);
				return JsonConvert.DeserializeObject<T>(resultJson, _jsonSerializerSettings);
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "[RPC] attempt {Attempt}: {Message}", attempt + 1, ex.Message);
				if (attempt == _maxRetries)
					throw;

				await Task.Delay(_retryDelay).ConfigureAwait(false);
			}
		}

		return default;
	}

	private async Task<string> ReadContentAsStringAsync(HttpContent content)
	{
		if (content.Headers.ContentLength is long contentLength && contentLength > _maxResponseBytes)
			throw new Exception($"[RPC][Error] Response body exceeds {_maxResponseBytes} bytes");

		using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
		using var memory = new MemoryStream();
		var buffer = new byte[StreamCopyBufferSize];
		long total = 0;
		for (; ; )
		{
			var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			if (read == 0)
				break;
			total += read;
			if (total > _maxResponseBytes)
				throw new Exception($"[RPC][Error] Response body exceeds {_maxResponseBytes} bytes");
			memory.Write(buffer, 0, read);
		}

		return Encoding.UTF8.GetString(memory.ToArray());
	}

	/// <summary>
	/// Executes a REST GET request and returns the deserialized result
	/// </summary>
	/// <typeparam name="T">Type to deserialize the response into</typeparam>
	/// <param name="url">Full URL to request</param>
	/// <returns>Deserialized result of type T or default if the body is empty</returns>
	public async Task<T?> RestGetAsync<T>(string url)
	{
		if (_logger?.IsEnabled(LogLevel.Information) == true)
		{
			_logger.LogInformation("[REST][GET] {Url}", url);
		}

		for (int attempt = 0; attempt <= _maxRetries; attempt++)
		{
			try
			{
				var stopwatch = Stopwatch.StartNew();
				using var request = new HttpRequestMessage(HttpMethod.Get, url);
				ApplyHeaders(request);
				using var resp = await _httpClient.SendAsync(request).ConfigureAwait(false);
				var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

				stopwatch.Stop();
				if (_logger?.IsEnabled(LogLevel.Information) == true)
				{
					var bodyBytes = text == null ? 0 : Encoding.UTF8.GetByteCount(text);
					_logger.LogInformation("[REST][GET][Response] {Url} {Status} elapsedMs={ElapsedMs} bodyBytes={BodyBytes}", url, resp.StatusCode, stopwatch.ElapsedMilliseconds, bodyBytes);
				}

				if (_logger?.IsEnabled(LogLevel.Debug) == true)
				{
					_logger.LogDebug("[REST][GET][Response][Body] {Url} {Json}", url, text);
				}

				if (!resp.IsSuccessStatusCode)
					throw new Exception($"HTTP error {resp.StatusCode}: {text}");

				return JsonConvert.DeserializeObject<T>(text, _jsonSerializerSettings);
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "[REST][GET] attempt {Attempt}: {Message}", attempt + 1, ex.Message);
				if (attempt == _maxRetries)
					throw;

				await Task.Delay(_retryDelay).ConfigureAwait(false);
			}
		}

		return default;
	}

	/// <summary>
	/// Executes a REST POST request with a JSON body and returns the deserialized result
	/// </summary>
	/// <typeparam name="T">Type to deserialize the response into</typeparam>
	/// <param name="url">Full URL to request</param>
	/// <param name="body">Object or raw string to send as JSON</param>
	/// <returns>Deserialized result of type T or default if the body is empty</returns>
	public async Task<T?> RestPostAsync<T>(string url, object body)
	{
		// Serialize provided body if needed
		var bodySerialized = body is string s ? s : JsonConvert.SerializeObject(body, _jsonSerializerSettings);
		if (_logger?.IsEnabled(LogLevel.Information) == true)
		{
			_logger.LogInformation("[REST][POST] {Url} bodyBytes={BodyBytes}", url, Encoding.UTF8.GetByteCount(bodySerialized));
		}

		if (_logger?.IsEnabled(LogLevel.Debug) == true)
		{
			_logger.LogDebug("[REST][POST][Body] {Url} {Json}", url, bodySerialized);
		}

		for (int attempt = 0; attempt <= _maxRetries; attempt++)
		{
			try
			{
				var stopwatch = Stopwatch.StartNew();
				using var content = new StringContent(bodySerialized, Encoding.UTF8, "application/json");
				using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
				ApplyHeaders(request);
				using var resp = await _httpClient.SendAsync(request).ConfigureAwait(false);
				var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

				stopwatch.Stop();
				if (_logger?.IsEnabled(LogLevel.Information) == true)
				{
					var bodyBytes = text == null ? 0 : Encoding.UTF8.GetByteCount(text);
					_logger.LogInformation("[REST][POST][Response] {Url} {Status} elapsedMs={ElapsedMs} bodyBytes={BodyBytes}", url, resp.StatusCode, stopwatch.ElapsedMilliseconds, bodyBytes);
				}

				if (_logger?.IsEnabled(LogLevel.Debug) == true)
				{
					_logger.LogDebug("[REST][POST][Response][Body] {Url} {Json}", url, text);
				}

				if (!resp.IsSuccessStatusCode)
					throw new Exception($"HTTP error {resp.StatusCode}: {text}");

				return JsonConvert.DeserializeObject<T>(text, _jsonSerializerSettings);
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "[REST][POST] attempt {Attempt}: {Message}", attempt + 1, ex.Message);
				if (attempt == _maxRetries)
					throw;

				await Task.Delay(_retryDelay).ConfigureAwait(false);
			}
		}

		return default;
	}

	/// <summary>
	/// Disposes internal HttpClient if it was created by this instance
	/// </summary>
	public void Dispose()
	{
		if (_ownsHttpClient)
		{
			_httpClient.Dispose();
		}
	}
}
