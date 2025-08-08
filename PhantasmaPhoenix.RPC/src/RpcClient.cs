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
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;
	private readonly ILogger? _logger;
	private readonly JsonSerializerSettings _jsonSerializerSettings;
	private readonly JsonSerializer _jsonSerializer;
	private readonly int _maxRetries;
	private readonly TimeSpan _retryDelay;

	/// <summary>
	/// Creates a new RPC client with optional external HttpClient and logging
	/// </summary>
	/// <param name="httpClient">HttpClient instance to use or null to create an internal one</param>
	/// <param name="logger">Logger instance or null to disable logging</param>
	/// <param name="maxRetries">Number of retry attempts for transient failures</param>
	/// <param name="retryDelayMs">Delay between retries in milliseconds</param>
	public RpcClient(HttpClient? httpClient = null, ILogger? logger = null, int maxRetries = 0, int retryDelayMs = 1000)
	{
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
		_logger?.LogDebug("[RPC][Request] {Url} {Method} params={Params}", url, method, parameters);

		for (int attempt = 0; attempt <= _maxRetries; attempt++)
		{
			try
			{
				using var content = new StringContent(body, Encoding.UTF8, "application/json");
				using var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

				// Read raw response JSON
				var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
				_logger?.LogDebug("[RPC][Response] {Url} {Status} {Json}", url, resp.StatusCode, text);

				var env = JsonConvert.DeserializeObject<RpcResponse>(text, _jsonSerializerSettings);
				if (env?.Error != null)
					throw new Exception($"[RPC][Error] {env.Error.Code}: {env.Error.Message}");

				if (env?.Result == null)
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

	/// <summary>
	/// Executes a REST GET request and returns the deserialized result
	/// </summary>
	/// <typeparam name="T">Type to deserialize the response into</typeparam>
	/// <param name="url">Full URL to request</param>
	/// <returns>Deserialized result of type T or default if the body is empty</returns>
	public async Task<T?> RestGetAsync<T>(string url)
	{
		_logger?.LogDebug("[REST][GET] {Url}", url);

		for (int attempt = 0; attempt <= _maxRetries; attempt++)
		{
			try
			{
				using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
				var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

				_logger?.LogDebug("[REST][GET][Response] {Url} {Status} {Json}", url, resp.StatusCode, text);

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
		_logger?.LogDebug("[REST][POST] {Url} Body={Body}", url, bodySerialized);

		for (int attempt = 0; attempt <= _maxRetries; attempt++)
		{
			try
			{
				using var content = new StringContent(bodySerialized, Encoding.UTF8, "application/json");
				using var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
				var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

				_logger?.LogDebug("[REST][POST][Response] {Url} {Status} {Json}", url, resp.StatusCode, text);

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
