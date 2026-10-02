using System.IO;
using IPA.Utilities;

namespace HitScoreVisualizer.Utilities.Services;

internal class PluginDirectories
{
	private readonly DirectoryInfo configs;
	private readonly DirectoryInfo backups;

	public PluginDirectories()
	{
		var configsPath = Path.Combine(UnityGame.UserDataPath, nameof(HitScoreVisualizer));
		configs = new(configsPath);
		backups = new(Path.Combine(configs.FullName, "Backups"));
	}

	public DirectoryInfo Configs => configs;

	public DirectoryInfo Backups => backups;
}