using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace HitScoreVisualizer.Utilities.Services;

internal static class ConfigJsonPreparation
{
	private static readonly MethodInfo memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

	internal static bool TryCaptureCulture(out CultureInfo culture)
	{
		var current = CultureInfo.CurrentCulture;
		if (JsonConvert.DefaultSettings is not null || current.GetType() != typeof(CultureInfo)
			|| current.NumberFormat.GetType() != typeof(NumberFormatInfo)
			|| current.DateTimeFormat.GetType() != typeof(DateTimeFormatInfo)
			|| current.DateTimeFormat.Calendar.GetType().Assembly != typeof(Calendar).Assembly)
		{
			culture = null!;
			return false;
		}
		culture = CultureInfo.ReadOnly((CultureInfo)current.Clone());
		return true;
	}

	internal static void Warmup()
	{
		_ = HsvConfigModel.Default;
		_ = HsvConfigModel.Vanilla;
		_ = NormalJudgment.Default;
		_ = ChainHeadJudgment.Default;
		_ = ChainLinkDisplay.Default;
		_ = JudgmentSegment.Default;
	}

	internal static bool TrySnapshot(HsvConfigModel? source, out HsvConfigModel? snapshot)
		=> TrySnapshot(source, out snapshot, out _);

	internal static bool TrySnapshot(HsvConfigModel? source, out HsvConfigModel? snapshot, out Dictionary<object, object> copies)
	{
		Warmup();
		copies = new Dictionary<object, object>(IdentityComparer.Instance);
		var supported = true;
		snapshot = CloneNode(source, copies, ref supported);
		if (snapshot is null)
		{
			return supported;
		}
		var original = source!;
		snapshot.Judgments = CloneList(original.Judgments, copies, ref supported)!;
		snapshot.ChainHeadJudgments = CloneList(original.ChainHeadJudgments, copies, ref supported)!;
		snapshot.ChainLinkDisplay = CloneNode(original.ChainLinkDisplay, copies, ref supported);
		snapshot.BeforeCutAngleJudgments = CloneList(original.BeforeCutAngleJudgments, copies, ref supported);
		snapshot.AccuracyJudgments = CloneList(original.AccuracyJudgments, copies, ref supported);
		snapshot.AfterCutAngleJudgments = CloneList(original.AfterCutAngleJudgments, copies, ref supported);
		snapshot.TimeDependenceJudgments = CloneList(original.TimeDependenceJudgments, copies, ref supported);
		snapshot.BadCutDisplays = CloneList(original.BadCutDisplays, copies, ref supported);
		snapshot.MissDisplays = CloneList(original.MissDisplays, copies, ref supported);
		return supported;
	}

	private static T? CloneNode<T>(T? source, Dictionary<object, object> copies, ref bool supported) where T : class
	{
		if (source is null)
		{
			return null;
		}
		if (source.GetType() != typeof(T))
		{
			supported = false;
			return null;
		}
		if (copies.TryGetValue(source, out var existing))
		{
			return (T)existing;
		}
		var copy = (T)memberwiseClone.Invoke(source, null)!;
		copies.Add(source, copy);
		return copy;
	}

	private static List<T>? CloneList<T>(List<T>? source, Dictionary<object, object> copies, ref bool supported) where T : class
	{
		if (source is null)
		{
			return null;
		}
		if (source.GetType() != typeof(List<T>))
		{
			supported = false;
			return null;
		}
		if (copies.TryGetValue(source, out var existing))
		{
			return (List<T>)existing;
		}
		var copy = new List<T>(source.Count);
		copies.Add(source, copy);
		foreach (var node in source)
		{
			copy.Add(CloneNode(node, copies, ref supported)!);
		}
		return copy;
	}

	internal static HsvConfigModel? Parse(string content, CultureInfo culture, VectorReadChannel vectorReads)
	{
		var previous = Thread.CurrentThread.CurrentCulture;
		try
		{
			Thread.CurrentThread.CurrentCulture = culture;
			using var text = new StringReader(content);
			using var reader = new JsonTextReader(text);
			var serializer = JsonSerializer.Create(CreateSettings(vectorReads));
			serializer.CheckAdditionalContent = true;
			return serializer.Deserialize<HsvConfigModel>(reader);
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = previous;
		}
	}

	internal static string Serialize(HsvConfigModel? snapshot, CultureInfo culture)
	{
		var previous = Thread.CurrentThread.CurrentCulture;
		try
		{
			Thread.CurrentThread.CurrentCulture = culture;
			using var text = new StringWriter(CultureInfo.InvariantCulture);
			using var writer = new JsonTextWriter(text);
			JsonSerializer.Create(CreateSettings()).Serialize(writer, snapshot);
			return text.ToString();
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = previous;
		}
	}

	private static JsonSerializerSettings CreateSettings(VectorReadChannel? vectorReads = null) => new()
	{
		DefaultValueHandling = DefaultValueHandling.Include,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.Indented,
		Converters = [new Vector3Converter(vectorReads), new StringEnumConverter(), new ColorArrayConverter()],
		ContractResolver = new WorkerConfigContractResolver()
	};

	private sealed class WorkerConfigContractResolver : HsvConfigContractResolver { }

	internal sealed class VectorReadChannel
	{
		private sealed class Request
		{
			internal readonly string Text;
			internal readonly TaskCompletionSource<Vector3> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			internal Request(string text) => Text = text;
		}

		private readonly object gate = new();
		private readonly Queue<Request> pending = new();
		private TaskCompletionSource<bool> changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal Task Changed
		{
			get
			{
				lock (gate)
				{
					return changed.Task;
				}
			}
		}

		internal Vector3 Read(string text)
		{
			var request = new Request(text);
			lock (gate)
			{
				pending.Enqueue(request);
				changed.TrySetResult(true);
			}
			return request.Completion.Task.GetAwaiter().GetResult();
		}

		internal void CompletePendingOnCaller()
		{
			while (true)
			{
				Request request;
				lock (gate)
				{
					if (pending.Count == 0)
					{
						return;
					}
					request = pending.Dequeue();
					if (pending.Count == 0)
					{
						changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
					}
				}
				try
				{
					request.Completion.SetResult(JsonConvert.DeserializeObject<Vector3>(request.Text));
				}
				catch (Exception error)
				{
					request.Completion.SetException(error);
				}
			}
		}
	}

	private sealed class IdentityComparer : IEqualityComparer<object>
	{
		internal static readonly IdentityComparer Instance = new();
		public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
		public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
	}
}
