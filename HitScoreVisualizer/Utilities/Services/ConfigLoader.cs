using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Extensions;
using HitScoreVisualizer.Utilities.Json;
using IPA.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Zenject;

namespace HitScoreVisualizer.Utilities.Services;

public class ConfigLoader : IInitializable
{
	private readonly PluginConfig pluginConfig;
	private readonly ConfigMigrator configMigrator;
	private readonly PluginDirectories directories;

	private Task? initializationTask;
	private readonly TaskCompletionSource<bool> initializationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private TaskCompletionSource<bool>? selectionCompleted;
	private TaskCompletionSource<bool> selectionChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private long selectionRevision;
	private readonly JsonSerializerSettings configSerializerSettings = new()
	{
		DefaultValueHandling = DefaultValueHandling.Include,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.Indented,
		Converters = [ new Vector3Converter(), new StringEnumConverter(), new ColorArrayConverter() ],
		ContractResolver = new HsvConfigContractResolver()
	};

	internal ConfigLoader(PluginConfig pluginConfig, ConfigMigrator configMigrator, PluginDirectories directories)
	{
		this.pluginConfig = pluginConfig;
		this.configMigrator = configMigrator;
		this.directories = directories;
	}

	public event Action<HsvConfigModel?>? ConfigChanged;

	public async void Initialize()
	{
		try
		{
			initializationTask ??= InitializeInternal();
			await initializationTask;
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Problem encountered while initializing loader:\n {ex}");
		}
		finally
		{
			initializationCompleted.TrySetResult(true);
		}
	}

	internal async Task WaitForReadiness(System.Threading.CancellationToken cancellationToken)
	{
		var retired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (cancellationToken.Register(static state =>
			((TaskCompletionSource<bool>)state!).TrySetResult(true), retired))
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var selection = selectionCompleted?.Task;
				var changed = selectionChanged.Task;
				await Task.WhenAny(initializationCompleted.Task, retired.Task);
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				if (selection != null)
				{
					await Task.WhenAny(selection, changed, retired.Task);
				}

				if (cancellationToken.IsCancellationRequested ||
				    ReferenceEquals(selection, selectionCompleted?.Task))
				{
					return;
				}
			}
		}
	}

	private async Task InitializeInternal()
	{
		var revision = selectionRevision;
		await ConfigFileWorker.EnsureDirectories(directories.Configs.FullName, directories.Backups.FullName);
		await CreateDefaultConfig();
		if (revision == selectionRevision)
		{
			await LoadSelectedConfig(revision);
		}
	}

	internal void RetireSelection()
	{
		selectionRevision++;
	}

	internal async Task<ConfigInfo[]> LoadAllHsvConfigs()
	{
		var paths = await ConfigFileWorker.Catalog(directories.Configs.FullName, directories.Backups.FullName);
		var createFileTasks = paths.Select(path => new FileInfo(path))
			.Where(file => !file.FullName.StartsWith(directories.Backups.FullName))
			.Select(GetConfigInfo);

		return await Task.WhenAll(createFileTasks);
	}

	internal Task<bool> TrySelectConfig(ConfigInfo? configInfo) => TrySelectConfig(configInfo, null);

	internal Task<bool> TrySelectConfig(ConfigInfo? configInfo, Func<bool>? mayPublish)
	{
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		selectionCompleted = completion;
		var changed = selectionChanged;
		selectionChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		changed.TrySetResult(true);
		return SelectAndComplete(configInfo, mayPublish, completion);
	}

	private async Task<bool> SelectAndComplete(ConfigInfo? configInfo, Func<bool>? mayPublish,
		TaskCompletionSource<bool> completion)
	{
		try
		{
			return await TrySelectConfigInternal(configInfo, mayPublish);
		}
		finally
		{
			completion.TrySetResult(true);
		}
	}

	private async Task<bool> TrySelectConfigInternal(ConfigInfo? configInfo, Func<bool>? mayPublish)
	{
		if (mayPublish is not null && !mayPublish())
		{
			return false;
		}
		var revision = ++selectionRevision;
		var originalConfig = configInfo?.Config;
		bool IsCurrent() => revision == selectionRevision && ReferenceEquals(configInfo?.Config, originalConfig) && (mayPublish?.Invoke() ?? true);
		if (configInfo is not { State: ConfigState.Compatible or ConfigState.NeedsMigration })
		{
			pluginConfig.SelectedConfig = null;
			pluginConfig.ConfigFilePath = null;
			ConfigChanged?.Invoke(null);
			return false;
		}

		if (configInfo.State is ConfigState.NeedsMigration)
		{
			Plugin.Log.Warn("Selected a config that needs migration");
			configInfo = await configMigrator.MigrateConfig(configInfo, IsCurrent);
			if (!IsCurrent())
			{
				return false;
			}
			await SaveConfig(configInfo);
		}
		if (!IsCurrent())
		{
			return false;
		}

		if (configInfo is { Config: not null, State: ConfigState.Compatible })
		{
			JudgmentTemplateCache.Prewarm(configInfo.Config);
			Plugin.Log.Info($"Selecting config {configInfo.ConfigName}");
			pluginConfig.SelectedConfig = configInfo;
			pluginConfig.ConfigFilePath = configInfo.File.FullName.Substring(directories.Configs.FullName.Length + 1);
			ConfigChanged?.Invoke(configInfo.Config);
		}

		return pluginConfig.ConfigFilePath != null;
	}

	private async Task<ConfigInfo> GetConfigInfo(FileInfo file)
	{
		var config = await TryLoadConfig(file);
		var version = config?.GetVersion() ?? Plugin.Metadata.HVersion;
		var state = await configMigrator.GetConfigStateAsync(config, file.Name);
		var description = state.GetConfigDescription(version);
		return new(file, description, state)
		{
			Config = config
		};
	}

	private async Task<HsvConfigModel?> TryLoadConfig(FileInfo file)
	{
		try
		{
			var (token, content) = await ConfigFileWorker.BeginRead(file.FullName);
			try
			{
				HsvConfigModel? config;
				if (ConfigJsonPreparation.TryCaptureCulture(out var culture))
				{
					ConfigJsonPreparation.Warmup();
					config = await ConfigFileWorker.PrepareRead(token, culture);
				}
				else
				{
					config = JsonConvert.DeserializeObject<HsvConfigModel>(content, configSerializerSettings);
				}
				if (config is not null)
				{
					JudgmentTemplateCache.Prewarm(config);
				}
				return config;
			}
			finally
			{
				await ConfigFileWorker.EndRead(token);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.Warn($"Problem encountered when trying to load {file.Name}\n{ex}");
			return null;
		}
	}

	private async Task SaveConfig(ConfigInfo config)
	{
		try
		{
			if (config.Config is null)
			{
				return;
			}

			Plugin.Log.Info($"Saving config {config.ConfigName}");
			var token = await ConfigFileWorker.BeginSave(config.File.FullName);
			string content = string.Empty;
			bool prepared;
			HsvConfigModel? snapshot;
			System.Globalization.CultureInfo culture;
			try
			{
				snapshot = null;
				prepared = ConfigJsonPreparation.TryCaptureCulture(out culture) && ConfigJsonPreparation.TrySnapshot(config.Config, out snapshot);
				if (!prepared)
				{
					content = JsonConvert.SerializeObject(config.Config, Formatting.Indented, configSerializerSettings);
				}
			}
			catch
			{
				await ConfigFileWorker.AbortSave(token);
				throw;
			}
			if (prepared)
			{
				await ConfigFileWorker.CommitConfig(token, snapshot, culture);
			}
			else
			{
				await ConfigFileWorker.CommitSave(token, content);
			}
		}
		catch (Exception e)
		{
			Plugin.Log.Error(e);
		}
	}

	private async Task CreateDefaultConfig()
	{
		const string defaultConfigName = "HitScoreVisualizerConfig (default).json";
		var defaultConfigPath = Path.Combine(directories.Configs.FullName, defaultConfigName);
		var defaultConfigFile = new FileInfo(defaultConfigPath);
		if (!await ConfigFileWorker.Exists(defaultConfigFile.FullName))
		{
			var defaultConfigDescription = ConfigState.Compatible.GetConfigDescription(Plugin.Metadata.HVersion);
			await SaveConfig(new(defaultConfigFile, defaultConfigDescription, ConfigState.Compatible)
			{
				Config = HsvConfigModel.Default
			});
		}

		var legacyConfigPath = Path.Combine(UnityGame.UserDataPath, "HitScoreVisualizerConfig.json");
		var destinationHsvConfigPath = Path.Combine(directories.Configs.FullName, "HitScoreVisualizerConfig (imported).json");
		await ConfigFileWorker.MoveIfExists(legacyConfigPath, destinationHsvConfigPath);
	}

	private async Task LoadSelectedConfig(long revision)
	{
		if (pluginConfig.ConfigFilePath == null)
		{
			return;
		}

		var selectedPath = pluginConfig.ConfigFilePath;
		var fullPath = Path.Combine(directories.Configs.FullName, selectedPath);
		var fileInfo = new FileInfo(fullPath);
		var exists = await ConfigFileWorker.Exists(fileInfo.FullName);
		bool IsCurrent() => revision == selectionRevision && pluginConfig.ConfigFilePath == selectedPath;
		if (!IsCurrent())
		{
			return;
		}
		if (!exists)
		{
			Plugin.Log.Warn("Selected config file was not found; resetting to default.");
			pluginConfig.ConfigFilePath = null;
			return;
		}

		var selectedConfig = await GetConfigInfo(fileInfo);
		if (!IsCurrent())
		{
			return;
		}
		if (selectedConfig.Config is null)
		{
			Plugin.Log.Warn("Problem encountered when trying to load selected config; resetting to default.");
			pluginConfig.ConfigFilePath = null;
			return;
		}

		if (selectedConfig.State.HasWarning())
		{
			Plugin.Log.Warn(selectedConfig.State.GetWarningMessage(selectedConfig.ConfigName, selectedConfig.Config.GetVersion()));
		}

		await TrySelectConfig(selectedConfig);
	}
}
