using System.Diagnostics;

namespace PhantasmaPhoenix.Core.Tools;

public sealed class NamedStopwatch
{
	private readonly Stopwatch _stopwatch;
	public readonly string Name;

	public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
	public bool HasName => !string.IsNullOrEmpty(Name);

	public NamedStopwatch(
		string name
	)
	{
		_stopwatch = new Stopwatch();
		Name = name;
	}

	public void Start()
	{
		_stopwatch.Start();
	}

	public void Stop()
	{
		_stopwatch.Stop();
	}

	public void Reset()
	{
		_stopwatch.Reset();
	}

	public string GetPrefixedName(
		string prefix
	)
	{
		return HasName ? prefix + Name : "";
	}

	public static string StopwatchesToString(List<NamedStopwatch> stopwatches, Stopwatch totalStopwatch)
	{
		if (totalStopwatch.ElapsedMilliseconds == 0)
		{
			return "Total ElapsedMilliseconds is 0";
		}

		var stopwatchLog = "";
		decimal includedPercentage = 0;
		var stopwatchIndex = 0;
		foreach (var stopwatch in stopwatches.OrderByDescending(x => x.ElapsedMilliseconds))
		{
			includedPercentage += (decimal)stopwatch.ElapsedMilliseconds / totalStopwatch.ElapsedMilliseconds * 100;
			if (stopwatch.ElapsedMilliseconds > 0)
			{
				stopwatchLog += $"\n#{++stopwatchIndex}: {stopwatch.ElapsedMilliseconds}/{totalStopwatch.ElapsedMilliseconds}\t\t - {Math.Round((decimal)stopwatch.ElapsedMilliseconds / totalStopwatch.ElapsedMilliseconds * 100, 3)} % [{stopwatch.Name}]";
			}
		}
		stopwatchLog += $"\nincludedPercentage: {Math.Round(includedPercentage, 3)}";

		return stopwatchLog;
	}

	public static void Start(
		ref List<NamedStopwatch> stopwatches,
		string name
	)
	{
		var stopwatch = stopwatches.Where(x => x.Name == name).FirstOrDefault();

		if (stopwatch == null)
		{
			stopwatch = new NamedStopwatch(name);
			stopwatches.Add(stopwatch);
		}

		stopwatch.Start();
	}

	public static void Stop(
		List<NamedStopwatch> stopwatches,
		string name
	)
	{
		stopwatches.Where(x => x.Name == name).First().Stop();
	}
}
