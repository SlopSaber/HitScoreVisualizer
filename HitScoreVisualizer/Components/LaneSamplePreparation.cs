using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace HitScoreVisualizer.Components;

[HarmonyPatch(typeof(LevelScenesTransitionSetupData), nameof(LevelScenesTransitionSetupData.BeforeScenesWillBeActivatedAsync))]
internal static class LaneSamplePreparation
{
	private const int MinimumNotes = 1024;
	private const int MaximumNotes = 100000;
	private static readonly FieldInfo? groupsField = typeof(BeatmapData).GetField(
		"_beatmapDataItemsPerTypeAndId", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? listsField = groupsField?.FieldType.GetField(
		"_items", BindingFlags.Instance | BindingFlags.NonPublic);
	private static ConditionalWeakTable<GameplayCoreSceneSetupData, Preparation> prepared = new();
	private static Task<List<PlayerMovementFollower.LaneSample>>? inFlight;
	private static int ownerThread;
	private static int revision;
	private static bool active;

	private readonly record struct Input(float Time, bool Bomb);
	private sealed record Request(Input[] Inputs, Quaternion Rotation, float Epsilon);
	private sealed record Preparation(WeakReference<IReadonlyBeatmapData> Data, NoteData[] Notes,
		Input[] Inputs, List<PlayerMovementFollower.LaneSample> Samples, int Revision);

	[StructLayout(LayoutKind.Explicit)]
	private struct FloatBits
	{
		[FieldOffset(0)] public float Value;
		[FieldOffset(0)] public int Bits;
	}

	internal static void Enable()
	{
		ownerThread = Thread.CurrentThread.ManagedThreadId;
		active = true;
		Retire();
	}

	internal static void Disable()
	{
		active = false;
		Retire();
	}

	private static void Retire()
	{
		revision++;
		prepared = new ConditionalWeakTable<GameplayCoreSceneSetupData, Preparation>();
	}

	[HarmonyPostfix]
	private static void Prepare(LevelScenesTransitionSetupData __instance, ref Task __result)
	{
		if (!active || __result == null || Thread.CurrentThread.ManagedThreadId != ownerThread ||
		    SynchronizationContext.Current == null)
		{
			return;
		}

		Retire();
		__result = PrepareAfterLoading(__instance, __result, revision);
	}

	private static async Task PrepareAfterLoading(LevelScenesTransitionSetupData setup, Task loading,
		int capturedRevision)
	{
		await loading;
		Task<List<PlayerMovementFollower.LaneSample>>? work = null;
		try
		{
			if (!IsCurrent(capturedRevision) || inFlight is { IsCompleted: false })
			{
				return;
			}

			var scene = setup.gameplayCoreSceneSetupData;
			if (scene == null || scene.beatmapKey.characteristic is
			    BeatmapCharacteristic.Degree90 or BeatmapCharacteristic.Degree360)
			{
				return;
			}

			var data = scene.transformedBeatmapData;
			if (data == null || !TrySnapshot(data, out var notes, out var inputs))
			{
				return;
			}

			var request = new Request(inputs, Quaternion.identity, Mathf.Epsilon);
			if (ExecutionContext.IsFlowSuppressed())
			{
				work = Start(request);
			}
			else
			{
				using (ExecutionContext.SuppressFlow())
				{
					work = Start(request);
				}
			}

			inFlight = work;
			var samples = await work;
			if (IsCurrent(capturedRevision) && ReferenceEquals(scene, setup.gameplayCoreSceneSetupData) &&
			    ReferenceEquals(data, scene.transformedBeatmapData))
			{
				prepared.Add(scene, new Preparation(new WeakReference<IReadonlyBeatmapData>(data),
					notes, inputs, samples, capturedRevision));
			}
		}
		catch
		{
			// Preparation failures leave the original synchronous constructor available.
		}
		finally
		{
			if (work != null && ReferenceEquals(inFlight, work))
			{
				inFlight = null;
			}
		}
	}

	private static bool IsCurrent(int capturedRevision) => active && revision == capturedRevision &&
		Thread.CurrentThread.ManagedThreadId == ownerThread;

	private static Task<List<PlayerMovementFollower.LaneSample>> Start(Request request) =>
		Task.Factory.StartNew(static state => CreateSamples((Request)state!), request,
			CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

	private static List<PlayerMovementFollower.LaneSample> CreateSamples(Request request)
	{
		var samples = new List<PlayerMovementFollower.LaneSample>(request.Inputs.Length);
		foreach (var input in request.Inputs.OrderBy(static input => input.Time))
		{
			if (input.Bomb || samples.Count > 0 &&
			    Approximately(samples[samples.Count - 1].Time, input.Time, request.Epsilon))
			{
				continue;
			}

			samples.Add(new PlayerMovementFollower.LaneSample(input.Time, request.Rotation,
				Array.Empty<string>()));
		}

		return samples;
	}

	private static bool Approximately(float a, float b, float epsilon) => Math.Abs(b - a) <
		Max(0.000001f * Max(Math.Abs(a), Math.Abs(b)), epsilon * 8f);

	private static float Max(float a, float b) => a > b ? a : b;

	private static bool TryGetNotes(IReadonlyBeatmapData data, out LinkedList<BeatmapDataItem> notes)
	{
		notes = null!;
		if (data == null || data.GetType() != typeof(BeatmapData) || groupsField == null || listsField == null)
		{
			return false;
		}

		var groups = groupsField.GetValue(data);
		if (groups == null || groups.GetType() != groupsField.FieldType ||
		    listsField.GetValue(groups) is not Dictionary<(Type, int), ISortedList<BeatmapDataItem>> lists ||
		    !ReferenceEquals(lists.Comparer, EqualityComparer<(Type, int)>.Default))
		{
			return false;
		}

		var runtimeType = typeof(NoteData).GetType();
		foreach (var key in lists.Keys)
		{
			if (key.Item1 is null || key.Item1.GetType() != runtimeType)
			{
				return false;
			}
		}

		if (!lists.TryGetValue((typeof(NoteData), 0), out var list) || list == null ||
		    list.GetType() != typeof(global::SortedList<NoteData, BeatmapDataItem>) &&
		    list.GetType() != typeof(global::SortedList<BeatmapDataItem>))
		{
			return false;
		}

		notes = list.items;
		return notes != null;
	}

	private static bool TrySnapshot(IReadonlyBeatmapData data, out NoteData[] notes, out Input[] inputs)
	{
		notes = Array.Empty<NoteData>();
		inputs = Array.Empty<Input>();
		if (!TryGetNotes(data, out var current) || current.Count < MinimumNotes || current.Count > MaximumNotes)
		{
			return false;
		}

		notes = new NoteData[current.Count];
		inputs = new Input[notes.Length];
		var index = 0;
		foreach (var item in current)
		{
			if (item is not NoteData note || note.GetType() != typeof(NoteData) || index >= notes.Length)
			{
				return false;
			}

			notes[index] = note;
			inputs[index++] = new Input(note.time, note.gameplayType == NoteData.GameplayType.Bomb);
		}

		return index == notes.Length;
	}

	internal static bool TryConsume(GameplayCoreSceneSetupData scene, IReadonlyBeatmapData data,
		out List<PlayerMovementFollower.LaneSample> samples)
	{
		samples = null!;
		if (!active || Thread.CurrentThread.ManagedThreadId != ownerThread || !prepared.TryGetValue(scene, out var result))
		{
			return false;
		}

		prepared.Remove(scene);
		try
		{
			if (!IsCurrent(result.Revision) || !result.Data.TryGetTarget(out var expected) ||
			    !ReferenceEquals(expected, data) || !ReferenceEquals(data, scene.transformedBeatmapData) ||
			    !TryGetNotes(data, out var current) || current.Count != result.Notes.Length)
			{
				return false;
			}

			var index = 0;
			foreach (var item in current)
			{
				if (index >= result.Notes.Length || !ReferenceEquals(item, result.Notes[index]) ||
				    item is not NoteData note || new FloatBits { Value = note.time }.Bits !=
				    new FloatBits { Value = result.Inputs[index].Time }.Bits ||
				    (note.gameplayType == NoteData.GameplayType.Bomb) != result.Inputs[index].Bomb)
				{
					return false;
				}

				index++;
			}

			if (index != result.Notes.Length)
			{
				return false;
			}

			samples = result.Samples;
			return true;
		}
		catch
		{
			return false;
		}
	}
}
