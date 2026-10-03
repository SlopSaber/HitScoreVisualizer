using System;
using System.Runtime.ExceptionServices;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Models.ConfigMigrations;
using HitScoreVisualizer.Utilities.Extensions;
using Version = Hive.Versioning.Version;

namespace HitScoreVisualizer.Utilities.Services;

internal class ConfigMigrator
{
	private readonly PluginDirectories directories;

	private readonly IHsvConfigMigration[] migrations = [
		new ConfigMigration200(),
		new ConfigMigration210(),
		new ConfigMigration223(),
		new ConfigMigration320(),
		new ConfigMigration340(),
		new ConfigMigration360()
	];

	private readonly Version minimumMigratableVersion;
	private readonly Version maximumMigrationNeededVersion;

	public ConfigMigrator(PluginDirectories directories)
	{
		this.directories = directories;

		var versions = migrations.Select(m => m.Version).ToList();
		minimumMigratableVersion = versions.Min();
		maximumMigrationNeededVersion = versions.Max();
	}

	public ConfigState GetConfigState(HsvConfigModel? configuration, string fileName)
	{
		try
		{
			if (configuration is null)
			{
				return ConfigState.Broken;
			}

			var configVersion = configuration.GetVersion();

			return configVersion.NewerThan(Plugin.Metadata.HVersion) ? ConfigState.NewerVersion
				: configVersion < minimumMigratableVersion ? ConfigState.Incompatible
				: configVersion < maximumMigrationNeededVersion ? ConfigState.NeedsMigration
				: configuration.Validate() ? ConfigState.Compatible : ConfigState.ValidationFailed;
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem when getting the state of {fileName}: \n{ex}");
			return ConfigState.Broken;
		}
	}

	public async Task<ConfigState> GetConfigStateAsync(HsvConfigModel? configuration, string fileName)
	{
		try
		{
			if (configuration is null)
			{
				return ConfigState.Broken;
			}
			if (!ConfigStatePreparation.TryCapture(configuration, Plugin.Metadata.HVersion, minimumMigratableVersion, maximumMigrationNeededVersion, out var request))
			{
				return GetConfigState(configuration, fileName);
			}
			var result = await ConfigFileWorker.PrepareState(request);
			foreach (var warning in result.Warnings)
			{
				Plugin.Log.Warn(warning);
			}
			if (result.Error is not null)
			{
				ExceptionDispatchInfo.Capture(result.Error).Throw();
			}
			return result.State;
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem when getting the state of {fileName}: \n{ex}");
			return ConfigState.Broken;
		}
	}

	public async Task<ConfigInfo> MigrateConfig(ConfigInfo configInfo, Func<bool> isCurrent)
	{
		if (configInfo.Config is null)
		{
			Plugin.Log.Warn($"Can't migrate {configInfo.ConfigName} because there is no HSV config");
			return configInfo;
		}

		// Create a backup file
		var backupName = $"{configInfo.ConfigName} (backup of config made for {configInfo.Config.GetVersion()}{configInfo.File.Extension})";
		var backupPath = Path.Combine(directories.Backups.FullName, backupName);
		await ConfigFileWorker.Backup(configInfo.File.FullName, backupPath);
		if (!isCurrent())
		{
			return configInfo;
		}

		foreach (var migration in migrations.Where((m => m.Version >= configInfo.Config.GetVersion())))
		{
			Plugin.Log.Debug($"Running migration {migration.Version} on {configInfo.ConfigName}");
			if (ConfigMigrationPreparation.TryCapture(migration, configInfo.Config, out var capture))
			{
				var result = await ConfigFileWorker.PrepareMigration(capture.WorkerRequest);
				if (!isCurrent()) return configInfo;
				if (capture.IsUnchanged()) capture.Apply(result);
				else migration.Migrate(configInfo.Config);
			}
			else migration.Migrate(configInfo.Config);
		}

		configInfo.Config.SetVersion(Plugin.Metadata.HVersion);
		return configInfo;
	}
}