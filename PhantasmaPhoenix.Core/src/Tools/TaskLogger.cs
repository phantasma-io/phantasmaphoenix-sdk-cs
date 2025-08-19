using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PhantasmaPhoenix.Core.Tools;

public static class TaskLogger
{
	private static ILogger? _logger;
	private static TimeSpan _interval;
	private static CancellationToken _ct;
	private static readonly ConcurrentDictionary<string, int> _taskCounts = new();
	private static Int64 created = 0;
	private static Int64 finished = 0;

	public static void Initialize(ILogger logger, TimeSpan interval, CancellationToken? ct = default)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_interval = interval;
		_ct = ct ?? CancellationToken.None;
		_ = StartLoop();
	}

	private static async Task StartLoop()
	{
#if NET6_0_OR_GREATER
		var timer = new PeriodicTimer(_interval);
		try
		{
			while (await timer.WaitForNextTickAsync())
			{
				if (_ct.IsCancellationRequested)
				{
					break;
				}
				try
				{
					LogSnapshot();
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine("[TaskLogger] ERROR: " + ex);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// normal shutdown
		}
#else
    while (!(_ct.IsCancellationRequested))
    {
        try
        {
            await Task.Delay(_interval, _ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            break;
        }
		if (_ct.IsCancellationRequested)
		{
			break;
		}
    }
#endif
	}

	public static Task RunLogged(string name, Func<Task> func)
	{
		_taskCounts.AddOrUpdate(name, 1, (_, count) => count + 1);
		created++;

		return Task.Run(async () =>
		{
			try
			{
				await func();
			}
			catch (OperationCanceledException)
			{
				_logger?.LogInformation("[TaskLogger] Task canceled: {Name}", name);
			}
			catch (Exception ex)
			{
				var e = ex.InnerException ?? ex;
				if (e is OperationCanceledException se)
				{
					_logger?.LogInformation("[TaskLogger] Task canceled: {Name}", name);
				}
				else
				{
					_logger?.LogInformation(ex, "[TaskLogger] Exception in task: {Name}", name);
					throw;
				}
			}
			finally
			{
				_taskCounts.AddOrUpdate(name, 0, (_, count) => Math.Max(count - 1, 0));
				finished++;
			}
		});
	}

	public static void RunFireAndForget(string name, Func<Task> func)
	{
		_ = RunLogged(name, func);
	}

	private static void LogSnapshot()
	{
		var snapshot = _taskCounts.ToArray();
		var active = snapshot.Where(x => x.Value > 0).OrderByDescending(x => x.Value).ToArray();

		if (active.Length == 0)
		{
			_logger?.LogInformation($"[TaskLogger][{created}|{finished}|Δ{created - finished}] No active tasks.");
			return;
		}

		_logger?.LogInformation("[TaskLogger][{created}|{finished}|Δ{delta}] Active: {Summary}",
			created, finished, created - finished,
			string.Join(" | ", active.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
	}

	public static IReadOnlyDictionary<string, int> GetSnapshot()
	{
		return new Dictionary<string, int>(_taskCounts);
	}
}
