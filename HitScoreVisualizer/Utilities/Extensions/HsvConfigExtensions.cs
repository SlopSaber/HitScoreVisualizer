using System.Threading.Tasks;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Services;
using Hive.Versioning;

namespace HitScoreVisualizer.Utilities.Extensions;

internal static class HsvConfigExtensions
{
	public static void SetVersion(this HsvConfigModel config, Version version)
	{
		config.MajorVersion = version.Major;
		config.MinorVersion = version.Minor;
		config.PatchVersion = version.Patch;
	}

	public static Version GetVersion(this HsvConfigModel config)
	{
		return new(config.MajorVersion, config.MinorVersion, config.PatchVersion);
	}

	internal static Task Yeet(this ConfigInfo configInfo)
	{
		return ConfigFileWorker.Delete(configInfo.File.FullName);
	}
}