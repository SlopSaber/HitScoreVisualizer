using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
	{
		Warmup();
		var copies = new Dictionary<object, object>(IdentityComparer.Instance);
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

	internal static HsvConfigModel? Parse(string content, CultureInfo culture)
	{
		var previous = Thread.CurrentThread.CurrentCulture;
		try
		{
			Thread.CurrentThread.CurrentCulture = culture;
			using var text = new StringReader(content);
			using var reader = new JsonTextReader(text);
			var serializer = JsonSerializer.Create(CreateSettings());
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

	private static JsonSerializerSettings CreateSettings() => new()
	{
		DefaultValueHandling = DefaultValueHandling.Include,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.Indented,
		Converters = [new Vector3Converter(false), new StringEnumConverter(), new ColorArrayConverter()],
		ContractResolver = new HsvConfigContractResolver()
	};

	private sealed class IdentityComparer : IEqualityComparer<object>
	{
		internal static readonly IdentityComparer Instance = new();
		public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
		public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
	}
}
