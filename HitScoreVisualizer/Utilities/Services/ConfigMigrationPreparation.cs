using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Models.ConfigMigrations;

namespace HitScoreVisualizer.Utilities.Services;

internal static class ConfigMigrationPreparation
{
	internal enum Kind { Threshold210, Order360 }

	internal sealed class Request
	{
		internal readonly Kind Kind;
		internal readonly HsvConfigModel Config;
		internal readonly CultureInfo Culture;

		internal Request(Kind kind, HsvConfigModel config, CultureInfo culture)
		{
			Kind = kind;
			Config = config;
			Culture = culture;
		}
	}

	internal sealed class Result
	{
		internal readonly HsvConfigModel Config;
		internal readonly bool NormalChanged;
		internal readonly bool AccuracyChanged;
		internal readonly bool ChainChanged;
		internal readonly bool TimeChanged;
		internal readonly Exception? Error;

		internal Result(HsvConfigModel config, bool normalChanged, bool accuracyChanged, bool chainChanged, bool timeChanged, Exception? error)
		{
			Config = config;
			NormalChanged = normalChanged;
			AccuracyChanged = accuracyChanged;
			ChainChanged = chainChanged;
			TimeChanged = timeChanged;
			Error = error;
		}
	}

	internal sealed class Capture
	{
		internal readonly Request WorkerRequest;
		private readonly HsvConfigModel target;
		private readonly Dictionary<object, object> originals = new(IdentityComparer.Instance);
		private readonly List<NormalJudgment>? normal;
		private readonly List<ChainHeadJudgment>? chain;
		private readonly List<JudgmentSegment>? accuracy;
		private readonly List<TimeDependenceJudgmentSegment>? time;
		private readonly List<NormalJudgment>? normalSnapshot;
		private readonly List<ChainHeadJudgment>? chainSnapshot;
		private readonly List<JudgmentSegment>? accuracySnapshot;
		private readonly List<TimeDependenceJudgmentSegment>? timeSnapshot;
		private readonly ulong major;
		private readonly ulong minor;
		private readonly ulong patch;

		internal Capture(HsvConfigModel target, Request request, Dictionary<object, object> copies)
		{
			this.target = target;
			WorkerRequest = request;
			foreach (var copy in copies)
			{
				originals.Add(copy.Value, copy.Key);
			}
			normal = target.Judgments;
			chain = target.ChainHeadJudgments;
			accuracy = target.AccuracyJudgments;
			time = target.TimeDependenceJudgments;
			normalSnapshot = request.Config.Judgments;
			chainSnapshot = request.Config.ChainHeadJudgments;
			accuracySnapshot = request.Config.AccuracyJudgments;
			timeSnapshot = request.Config.TimeDependenceJudgments;
			major = request.Config.MajorVersion;
			minor = request.Config.MinorVersion;
			patch = request.Config.PatchVersion;
		}

		internal bool IsUnchanged()
		{
			if (target.MajorVersion != major || target.MinorVersion != minor || target.PatchVersion != patch)
			{
				return false;
			}
			if (WorkerRequest.Kind == Kind.Threshold210)
			{
				return Matches(normal, target.Judgments, normalSnapshot, (a, b) => a.Threshold == b.Threshold && a.Text == b.Text && a.Color.Equals(b.Color) && a.Fade == b.Fade)
					&& Matches(accuracy, target.AccuracyJudgments, accuracySnapshot, (a, b) => a.Threshold == b.Threshold && a.Text == b.Text);
			}
			return Matches(normal, target.Judgments, normalSnapshot, (a, b) => a.Threshold == b.Threshold)
				&& Matches(chain, target.ChainHeadJudgments, chainSnapshot, (a, b) => a.Threshold == b.Threshold)
				&& Matches(time, target.TimeDependenceJudgments, timeSnapshot, (a, b) => a.Threshold.Equals(b.Threshold));
		}

		private bool Matches<T>(List<T>? original, List<T>? current, List<T>? snapshot, Func<T, T, bool> sameFields) where T : class
		{
			if (!ReferenceEquals(original, current))
			{
				return false;
			}
			if (current is null || snapshot is null)
			{
				return current is null && snapshot is null;
			}
			if (current.Count != snapshot.Count)
			{
				return false;
			}
			for (var index = 0; index < current.Count; index++)
			{
				var node = current[index];
				var copy = snapshot[index];
				if (node is null || copy is null)
				{
					if (node is not null || copy is not null) return false;
					continue;
				}
				if (!ReferenceEquals(node, originals[copy]) || !sameFields(node, copy)) return false;
			}
			return true;
		}

		internal void Apply(Result result)
		{
			if (result.NormalChanged)
			{
				target.Judgments = WorkerRequest.Kind == Kind.Threshold210 ? result.Config.Judgments : Rebind(result.Config.Judgments);
			}
			if (result.AccuracyChanged) target.AccuracyJudgments = result.Config.AccuracyJudgments;
			if (result.ChainChanged) target.ChainHeadJudgments = Rebind(result.Config.ChainHeadJudgments);
			if (result.TimeChanged) target.TimeDependenceJudgments = Rebind(result.Config.TimeDependenceJudgments!);
			if (result.Error is not null) ExceptionDispatchInfo.Capture(result.Error).Throw();
		}

		private List<T> Rebind<T>(List<T> nodes) where T : class => nodes.Select(node => (T)originals[node]).ToList();
	}

	internal static bool TryCapture(IHsvConfigMigration migration, HsvConfigModel config, out Capture capture)
	{
		capture = null!;
		Kind kind;
		if (migration.GetType() == typeof(ConfigMigration210)) kind = Kind.Threshold210;
		else if (migration.GetType() == typeof(ConfigMigration360)) kind = Kind.Order360;
		else return false;
		if (!ConfigJsonPreparation.TryCaptureCulture(out var culture)
			|| !ConfigJsonPreparation.TrySnapshot(config, out var snapshot, out var copies)) return false;
		capture = new(config, new(kind, snapshot!, culture), copies);
		return true;
	}

	internal static Result Process(Request request)
	{
		var config = request.Config;
		var normal = config.Judgments;
		var accuracy = config.AccuracyJudgments;
		var chain = config.ChainHeadJudgments;
		var time = config.TimeDependenceJudgments;
		var previousCulture = Thread.CurrentThread.CurrentCulture;
		Exception? error = null;
		try
		{
			Thread.CurrentThread.CurrentCulture = request.Culture;
			if (request.Kind == Kind.Threshold210) new ConfigMigration210().Migrate(config);
			else new ConfigMigration360().Migrate(config);
		}
		catch (Exception exception)
		{
			error = exception;
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = previousCulture;
		}
		return new(config, !ReferenceEquals(normal, config.Judgments), !ReferenceEquals(accuracy, config.AccuracyJudgments),
			!ReferenceEquals(chain, config.ChainHeadJudgments), !ReferenceEquals(time, config.TimeDependenceJudgments), error);
	}

	private sealed class IdentityComparer : IEqualityComparer<object>
	{
		internal static readonly IdentityComparer Instance = new();
		public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
		public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
	}
}
