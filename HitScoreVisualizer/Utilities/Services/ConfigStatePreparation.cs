using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Extensions;
using Version = Hive.Versioning.Version;

namespace HitScoreVisualizer.Utilities.Services;

internal static class ConfigStatePreparation
{
	internal sealed class VersionSnapshot
	{
		private readonly ulong major;
		private readonly ulong minor;
		private readonly ulong patch;
		private readonly string[] preRelease;
		private readonly string[] build;

		internal VersionSnapshot(Version source)
		{
			major = source.Major;
			minor = source.Minor;
			patch = source.Patch;
			preRelease = source.PreReleaseIds.ToArray();
			build = source.BuildIds.ToArray();
		}

		internal Version Create() => new(major, minor, patch, preRelease, build);
	}

	internal sealed class Request
	{
		internal readonly HsvConfigModel Config;
		internal readonly CultureInfo Culture;
		internal readonly VersionSnapshot Current;
		internal readonly VersionSnapshot Minimum;
		internal readonly VersionSnapshot Maximum;

		internal Request(HsvConfigModel config, CultureInfo culture, Version current, Version minimum, Version maximum)
		{
			Config = config;
			Culture = culture;
			Current = new(current);
			Minimum = new(minimum);
			Maximum = new(maximum);
		}
	}

	internal sealed class Result
	{
		internal readonly ConfigState State;
		internal readonly List<string> Warnings;
		internal readonly Exception? Error;

		internal Result(ConfigState state, List<string> warnings, Exception? error = null)
		{
			State = state;
			Warnings = warnings;
			Error = error;
		}
	}

	internal static bool TryCapture(HsvConfigModel configuration, Version current, Version minimum, Version maximum, out Request request)
	{
		request = null!;
		if (current.GetType() != typeof(Version) || minimum.GetType() != typeof(Version) || maximum.GetType() != typeof(Version)
			|| !ConfigJsonPreparation.TryCaptureCulture(out var culture)
			|| !ConfigJsonPreparation.TrySnapshot(configuration, out var snapshot))
		{
			return false;
		}
		_ = UnityEngine.Mathf.Epsilon;
		request = new(snapshot!, culture, current, minimum, maximum);
		return true;
	}

	internal static Result Process(Request request)
	{
		var warnings = new List<string>();
		var previousCulture = Thread.CurrentThread.CurrentCulture;
		try
		{
			Thread.CurrentThread.CurrentCulture = request.Culture;
			var version = request.Config.GetVersion();
			var state = version.NewerThan(request.Current.Create()) ? ConfigState.NewerVersion
				: version < request.Minimum.Create() ? ConfigState.Incompatible
				: version < request.Maximum.Create() ? ConfigState.NeedsMigration
				: request.Config.Validate(warnings) ? ConfigState.Compatible : ConfigState.ValidationFailed;
			return new(state, warnings);
		}
		catch (Exception error)
		{
			return new(ConfigState.Broken, warnings, error);
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = previousCulture;
		}
	}
}
