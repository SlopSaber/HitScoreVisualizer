using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HitScoreVisualizer.Components;

internal sealed class LanePresencePreparation : IDisposable
{
	internal const int MinimumNotes = 16384;
	internal const int PrefixCount = 8192;
	internal const int MinimumRemainingNotes = 4096;
	private const int ProbeCount = 64;

	private Task<bool>? work;

	private readonly struct PresenceValue(Quaternion rotation, int trackCount)
	{
		internal readonly Quaternion Rotation = rotation;
		internal readonly int TrackCount = trackCount;
	}

	private sealed class Request(PresenceValue[] values, Quaternion identity)
	{
		internal readonly PresenceValue[] Values = values;
		internal readonly Quaternion Identity = identity;
	}

	private LanePresencePreparation() { }

	internal static LanePresencePreparation? TryStart(List<PlayerMovementFollower.LaneSample> samples)
	{
		LanePresencePreparation? preparation = null;
		try
		{
			preparation = new LanePresencePreparation();
			var identity = Quaternion.identity;
			for (var i = 0; i < ProbeCount; i++)
			{
				var sample = samples[i];
				if (Quaternion.Angle(sample.Rotation, identity) > 0.01f || sample.Tracks.Length > 0)
					return preparation;
			}

			var values = new PresenceValue[PrefixCount - ProbeCount];
			for (var i = ProbeCount; i < PrefixCount; i++)
			{
				var sample = samples[i];
				values[i - ProbeCount] = new PresenceValue(sample.Rotation, sample.Tracks.Length);
			}

			var request = new Request(values, identity);
			if (ExecutionContext.IsFlowSuppressed())
				preparation.work = Start(request);
			else
				using (ExecutionContext.SuppressFlow())
					preparation.work = Start(request);
			return preparation;
		}
		catch (Exception)
		{
			preparation?.Dispose();
			return null;
		}
	}

	private static Task<bool> Start(Request request) => Task.Factory.StartNew(static state =>
	{
		var input = (Request)state!;
		foreach (var value in input.Values)
			if (Quaternion.Angle(value.Rotation, input.Identity) > 0.01f || value.TrackCount > 0)
				return true;
		return false;
	}, request, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

	internal bool TryComplete(List<PlayerMovementFollower.LaneSample> samples, out bool result)
	{
		if (work == null)
		{
			result = true;
			return true;
		}
		if (!TryWait(out result))
			return false;
		if (result)
			return true;

		var identity = Quaternion.identity;
		for (var i = PrefixCount; i < samples.Count; i++)
		{
			var sample = samples[i];
			if (Quaternion.Angle(sample.Rotation, identity) > 0.01f || sample.Tracks.Length > 0)
			{
				result = true;
				break;
			}
		}
		return true;
	}

	private bool TryWait(out bool result)
	{
		// Retire the physical task even when the owner loop fails or its wait is interrupted.
		while (work != null && !work.IsCompleted)
		{
			try { work.Wait(); }
			catch (ThreadInterruptedException) { }
			catch (AggregateException) { }
		}
		try
		{
			result = work != null && work.GetAwaiter().GetResult();
			return true;
		}
		catch (Exception)
		{
			result = false;
			return false;
		}
	}

	public void Dispose() => TryWait(out _);
}
