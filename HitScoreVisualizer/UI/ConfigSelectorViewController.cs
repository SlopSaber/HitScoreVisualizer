using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Extensions;
using HitScoreVisualizer.Utilities.Services;
using HMUI;
using IPA.Utilities.Async;
using Zenject;

namespace HitScoreVisualizer.UI;

[HotReload(RelativePathToLayout = @"Views\ConfigSelector.bsml")]
[ViewDefinition("HitScoreVisualizer.UI.Views.ConfigSelector.bsml")]
internal class ConfigSelectorViewController : BSMLAutomaticViewController
{
	[Inject] private readonly PluginConfig pluginConfig = null!;
	[Inject] private readonly ConfigLoader configLoader = null!;
	[Inject] private readonly PluginDirectories directories = null!;

	[UIComponent("configs-list")]
	private readonly CustomCellListTableData configsList = null!;
	private bool active;
	private bool publishingSelection;
	private long listRevision;
	private long operationRevision;

	public bool LoadingConfigs { get; private set; }

	public bool HasLoadedConfigs => !LoadingConfigs;

	public bool ConfigPickable =>
		pluginConfig.SelectedConfig is { State: ConfigState.Compatible or ConfigState.NeedsMigration };

	public bool HasConfigCurrently =>
		!string.IsNullOrWhiteSpace(pluginConfig.ConfigFilePath);

	public string LoadedConfigText =>
		$"Currently Loaded Config<size=90%> : {(HasConfigCurrently ? Path.GetFileNameWithoutExtension(pluginConfig.ConfigFilePath) : "None")}";

	public bool ConfigYeetable => pluginConfig.SelectedConfig is { Config: not null };

	public void ConfigSelected(TableView tableView, object obj)
	{
		if (!publishingSelection)
		{
			RetirePendingOperations();
		}
		pluginConfig.SelectedConfig = (ConfigInfo)obj;
		NotifyPropertyChanged(nameof(ConfigPickable));
		NotifyPropertyChanged(nameof(ConfigYeetable));
	}

	public async void RefreshList()
	{
		try
		{
			RetirePendingOperations();
			await RefreshListInternal();
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem while refreshing config list: {ex}");
		}
	}

	public async void PickConfig()
	{
		try
		{
			RetirePendingOperations();
			var revision = operationRevision;
			var selectedConfig = pluginConfig.SelectedConfig;
			bool IsCurrent() => this && active && revision == operationRevision && ReferenceEquals(pluginConfig.SelectedConfig, selectedConfig);
			if (await configLoader.TrySelectConfig(selectedConfig, IsCurrent) && IsCurrent())
			{
				await RefreshListInternal();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem while picking selected config: {ex}");
		}
	}

	public async void UnpickConfig()
	{
		try
		{
			if (!HasConfigCurrently)
			{
				return;
			}

			RetirePendingOperations();
			configsList.TableView.ClearSelection();
			await configLoader.TrySelectConfig(null);

			NotifyPropertyChanged(nameof(HasConfigCurrently));
			NotifyPropertyChanged(nameof(LoadedConfigText));
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem while unpicking config: {ex}");
		}
	}

	public async void YeetConfig()
	{
		try
		{
			if (!ConfigYeetable)
			{
				return;
			}

			var selectedConfig = pluginConfig.SelectedConfig;
			RetirePendingOperations();
			var revision = operationRevision;
			if (selectedConfig is not null)
			{
				await selectedConfig.Yeet();
			}
			if (!this || !active || revision != operationRevision || !ReferenceEquals(pluginConfig.SelectedConfig, selectedConfig))
			{
				return;
			}
			await RefreshListInternal();

			if (this && active && revision == operationRevision)
			{
				NotifyPropertyChanged(nameof(ConfigYeetable));
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem while deleting selected config: {ex}");
		}
	}

	public async void FolderButtonPressed()
	{
		try
		{
			await ConfigFileWorker.OpenFolder(directories.Configs.FullName);
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem while opening the configuration folder: {ex}");
		}
	}

	protected override async void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
	{
		try
		{
			active = true;
			RetirePendingOperations(false);
			base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
			await RefreshListInternal();
		}
		catch (Exception ex)
		{
			Plugin.Log.Error($"Encountered a problem while deactivating {nameof(ConfigSelectorViewController)}: {ex}");
		}
	}

	protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
	{
		active = false;
		RetirePendingOperations(false);
		base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
	}

	private void RetirePendingOperations(bool retireSelection = true)
	{
		listRevision++;
		operationRevision++;
		if (retireSelection)
		{
			configLoader.RetireSelection();
		}
		if (LoadingConfigs)
		{
			LoadingConfigs = false;
			NotifyPropertyChanged(nameof(LoadingConfigs));
			NotifyPropertyChanged(nameof(HasLoadedConfigs));
		}
	}

	private async Task RefreshListInternal()
	{
		if (!this || !active)
		{
			return;
		}
		var revision = ++listRevision;
		LoadingConfigs = true;

		NotifyPropertyChanged(nameof(LoadingConfigs));
		NotifyPropertyChanged(nameof(HasLoadedConfigs));

		try
		{
			var loadedConfigs = await configLoader.LoadAllHsvConfigs();
			if (!this || !active || revision != listRevision)
			{
				return;
			}

			var intermediateConfigs = loadedConfigs.OrderByDescending(x => x.State).ThenBy(x => x.ConfigName).ToList();
			var currentConfigIndex = intermediateConfigs.FindIndex(configInfo => configInfo.File.FullName == pluginConfig.SelectedConfig?.File.FullName);
			await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
			{
				if (!this || !active || revision != listRevision)
				{
					return;
				}
				configsList.Data = intermediateConfigs;
				configsList.TableView.ReloadData();
				if (!this || !active || revision != listRevision)
				{
					return;
				}
				configsList.TableView.ScrollToCellWithIdx(0, TableView.ScrollPositionType.Beginning, false);
				if (currentConfigIndex >= 0)
				{
					publishingSelection = true;
					try
					{
						configsList.TableView.SelectCellWithIdx(currentConfigIndex, true);
					}
					finally
					{
						publishingSelection = false;
					}
				}
				NotifyPropertyChanged(nameof(HasConfigCurrently));
				NotifyPropertyChanged(nameof(LoadedConfigText));
			});
		}
		finally
		{
			if (this && active && revision == listRevision)
			{
				LoadingConfigs = false;
				NotifyPropertyChanged(nameof(LoadingConfigs));
				NotifyPropertyChanged(nameof(HasLoadedConfigs));
			}
		}
	}
}