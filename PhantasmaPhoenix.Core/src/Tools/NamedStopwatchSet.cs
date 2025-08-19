using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PhantasmaPhoenix.Core.Tools;

public sealed class NamedStopwatchSet
{
	private bool _enabled;
	private List<NamedStopwatch> _stopwatches;
	private string? _lastStartedName;
	private readonly Stopwatch? _totalStopwatch;
	private uint _reportFrequencyLimitInSeconds = 0;
	private uint _reportMinElapsedLimitInMilliseconds = 0;
	private DateTime _lastReported;
	public long ElapsedMilliseconds => _totalStopwatch?.ElapsedMilliseconds ?? 0;
	public NamedStopwatchSet(bool enabled = true, bool startTotal = true)
	{
		_enabled = enabled;
		_stopwatches = new();
		_lastStartedName = null;

		if (!_enabled)
		{
			return;
		}

		_totalStopwatch = new();
		if (startTotal)
		{
			_totalStopwatch.Start();
		}

		_lastReported = DateTime.Now.AddSeconds(-1 * (_reportFrequencyLimitInSeconds + 1));
	}
	public void Configure(uint reportFrequencyLimitInSeconds, uint reportMinElapsedLimitInMilliseconds = 100)
	{
		_reportFrequencyLimitInSeconds = reportFrequencyLimitInSeconds;
		_reportMinElapsedLimitInMilliseconds = reportMinElapsedLimitInMilliseconds;
	}

	public void Start(string name)
	{
		if (!_enabled)
		{
			return;
		}

		NamedStopwatch.Start(ref _stopwatches, name);
		_lastStartedName = name;
	}

	public void Stop(string name)
	{
		if (!_enabled)
		{
			return;
		}

		NamedStopwatch.Stop(_stopwatches, name);
		_lastStartedName = null;
	}

	public void Stop()
	{
		if (!_enabled)
		{
			return;
		}

		if (_lastStartedName == null)
		{
			throw new Exception("Cannot find last started stopwatch");
		}

		Stop(_lastStartedName);
	}

	public void TotalStart()
	{
		_totalStopwatch?.Start();
	}
	public void TotalStop()
	{
		_totalStopwatch?.Stop();
	}

	public void StopAll(bool stopTotal = false)
	{
		foreach (var sw in _stopwatches)
		{
			sw.Stop();
		}
		_lastStartedName = null;

		if (stopTotal)
		{
			_totalStopwatch?.Stop();
		}
	}

	public void Reset(bool resetTotal = true)
	{
		foreach (var sw in _stopwatches)
		{
			sw.Reset();
		}

		if (resetTotal)
		{
			_totalStopwatch?.Reset();
		}
	}

	public override string ToString()
	{
		if (!_enabled)
		{
			return "";
		}

		_totalStopwatch?.Stop();
		return _totalStopwatch != null ? NamedStopwatch.StopwatchesToString(_stopwatches, _totalStopwatch) : "";
	}

	public void Report(string messagePrefix, ILogger logger)
	{
		if (!_enabled)
		{
			return;
		}
		if (_reportFrequencyLimitInSeconds > 0 && (DateTime.Now - _lastReported).TotalSeconds < _reportFrequencyLimitInSeconds)
		{
			return;
		}
		if (_reportMinElapsedLimitInMilliseconds > 0 && ElapsedMilliseconds < _reportMinElapsedLimitInMilliseconds)
		{
			return;
		}

		logger.LogInformation(messagePrefix + " Elapsed times:" + ToString());
		_lastReported = DateTime.Now;
	}
}
