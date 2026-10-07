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
	private static ConditionalWeakTable<GameplayCoreSceneSetupData, OrderedPreparation> preparedOrders = new();
	private static readonly Guid customModule = new("259554d3-934a-42b5-8db1-54a77cbc58e2");
	private static Task? inFlight;
	private static HitScoreVisualizer.Utilities.Services.ConfigLoader? configLoader;
	private static CancellationTokenSource? readinessLifetime;
	private static int ownerThread;
	private static int revision;
	private static bool active;

	private readonly record struct Input(float Time, bool Bomb);
	private sealed record Request(Input[] Inputs, Quaternion Rotation, float Epsilon);
	private sealed record Preparation(WeakReference<IReadonlyBeatmapData> Data, NoteData[] Notes,
		Input[] Inputs, List<PlayerMovementFollower.LaneSample> Samples, int Revision);
	private sealed record OrderedPreparation(WeakReference<IReadonlyBeatmapData> Data, NoteData[] Notes,
		float[] Times, int[] Order, int Revision);

	[StructLayout(LayoutKind.Explicit)]
	private struct FloatBits
	{
		[FieldOffset(0)] public float Value;
		[FieldOffset(0)] public int Bits;
	}

	internal static void Enable(HitScoreVisualizer.Utilities.Services.ConfigLoader loader)
	{
		ownerThread = Thread.CurrentThread.ManagedThreadId;
		configLoader = loader;
		active = true;
		Retire();
	}

	internal static void Disable()
	{
		active = false;
		Retire();
		configLoader = null;
	}

	private static void Retire()
	{
		revision++;
		readinessLifetime?.Cancel();
		readinessLifetime?.Dispose();
		readinessLifetime = active ? new CancellationTokenSource() : null;
		prepared = new ConditionalWeakTable<GameplayCoreSceneSetupData, Preparation>();
		preparedOrders = new ConditionalWeakTable<GameplayCoreSceneSetupData, OrderedPreparation>();
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
		__result = PrepareAfterLoading(__instance, __result, revision, readinessLifetime!.Token);
	}

	private static async Task PrepareAfterLoading(LevelScenesTransitionSetupData setup, Task loading,
		int capturedRevision, CancellationToken readinessToken)
	{
		await loading;
		Task? work = null;
		try
		{
			if (!IsCurrent(capturedRevision))
			{
				return;
			}

			var loader = configLoader;
			if (loader != null)
			{
				await loader.WaitForReadiness(readinessToken);
			}

			if (!IsCurrent(capturedRevision) || readinessToken.IsCancellationRequested ||
			    inFlight is { IsCompleted: false })
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
			if (data == null)
			{
				return;
			}

			if (TryOrderSnapshot(data, out var orderedNotes, out var times))
			{
				Task<int[]> ordering;
				if (ExecutionContext.IsFlowSuppressed())
				{
					ordering = StartOrder(times);
				}
				else
				{
					using (ExecutionContext.SuppressFlow())
					{
						ordering = StartOrder(times);
					}
				}

				work = ordering;
				inFlight = ordering;
				var order = await ordering;
				if (IsCurrent(capturedRevision) && ReferenceEquals(scene, setup.gameplayCoreSceneSetupData) &&
				    ReferenceEquals(data, scene.transformedBeatmapData))
				{
					preparedOrders.Add(scene, new OrderedPreparation(new WeakReference<IReadonlyBeatmapData>(data),
						orderedNotes, times, order, capturedRevision));
				}

				return;
			}

			if (!TrySnapshot(data, out var notes, out var inputs))
			{
				return;
			}

			var request = new Request(inputs, Quaternion.identity, Mathf.Epsilon);
			Task<List<PlayerMovementFollower.LaneSample>> sampling;
			if (ExecutionContext.IsFlowSuppressed())
			{
				sampling = Start(request);
			}
			else
			{
				using (ExecutionContext.SuppressFlow())
				{
					sampling = Start(request);
				}
			}

			work = sampling;
			inFlight = sampling;
			var samples = await sampling;
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

	private static Task<int[]> StartOrder(float[] times) => Task.Factory.StartNew(static state =>
	{
		var keys = (float[])state!;
		return Enumerable.Range(0, keys.Length).OrderBy(index => keys[index]).ToArray();
	}, times, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

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

	private static bool TryGetNotes(IReadonlyBeatmapData data, out LinkedList<BeatmapDataItem> notes,
		Type? customNote = null)
	{
		notes = null!;
		if (data == null || customNote == null && data.GetType() != typeof(BeatmapData) ||
		    groupsField == null || listsField == null)
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
		    list.GetType() != typeof(global::SortedList<BeatmapDataItem>) &&
		    (customNote == null || list.GetType() != typeof(global::SortedList<,>).MakeGenericType(
			    customNote, typeof(BeatmapDataItem))))
		{
			return false;
		}

		notes = list.items;
		return notes != null;
	}

	private static Type? CustomNoteType(IReadonlyBeatmapData data)
	{
		var type = data.GetType();
		if (!type.IsSealed || type.BaseType != typeof(BeatmapData) ||
		    type.FullName != "CustomJSONData.CustomBeatmap.CustomBeatmapData" ||
		    type.Module.ModuleVersionId != customModule)
		{
			return null;
		}

		var noteType = type.Assembly.GetType("CustomJSONData.CustomBeatmap.CustomNoteData", false);
		return noteType?.BaseType == typeof(NoteData) ? noteType : null;
	}

	private static bool TryOrderSnapshot(IReadonlyBeatmapData data, out NoteData[] notes, out float[] times)
	{
		notes = Array.Empty<NoteData>();
		times = Array.Empty<float>();
		var customNote = CustomNoteType(data);
		if (customNote == null || !TryGetNotes(data, out var current, customNote) ||
		    current.Count < MinimumNotes || current.Count > MaximumNotes)
		{
			return false;
		}

		notes = new NoteData[current.Count];
		times = new float[notes.Length];
		var index = 0;
		foreach (var item in current)
		{
			if (item is not NoteData note ||
			    note.GetType() != customNote && note.GetType() != typeof(NoteData) || index >= notes.Length)
			{
				return false;
			}

			notes[index] = note;
			times[index++] = note.time;
		}

		return index == notes.Length;
	}

	internal static bool TryConsumeOrder(GameplayCoreSceneSetupData scene, IReadonlyBeatmapData data,
		out NoteData[] orderedNotes)
	{
		orderedNotes = null!;
		if (!active || Thread.CurrentThread.ManagedThreadId != ownerThread ||
		    !preparedOrders.TryGetValue(scene, out var result))
		{
			return false;
		}

		preparedOrders.Remove(scene);
		try
		{
			var customNote = CustomNoteType(data);
			if (!IsCurrent(result.Revision) || !result.Data.TryGetTarget(out var expected) ||
			    !ReferenceEquals(expected, data) || !ReferenceEquals(data, scene.transformedBeatmapData) ||
			    customNote == null || !TryGetNotes(data, out var current, customNote) ||
			    current.Count != result.Notes.Length)
			{
				return false;
			}

			var index = 0;
			foreach (var item in current)
			{
				if (index >= result.Notes.Length || !ReferenceEquals(item, result.Notes[index]) ||
				    new FloatBits { Value = ((NoteData)item).time }.Bits !=
				    new FloatBits { Value = result.Times[index] }.Bits)
				{
					return false;
				}

				index++;
			}

			if (index != result.Notes.Length)
			{
				return false;
			}

			orderedNotes = new NoteData[result.Order.Length];
			for (var i = 0; i < orderedNotes.Length; i++)
			{
				orderedNotes[i] = result.Notes[result.Order[i]];
			}

			return true;
		}
		catch
		{
			return false;
		}
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
