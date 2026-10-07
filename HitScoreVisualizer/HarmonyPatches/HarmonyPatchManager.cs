using System;
using System.Reflection;
using HarmonyLib;
using Zenject;

namespace HitScoreVisualizer.HarmonyPatches;

internal class HarmonyPatchManager : IInitializable, IDisposable
{
	private readonly HitScoreVisualizer.Utilities.Services.ConfigLoader configLoader;
	private readonly Harmony harmony = new(Plugin.Metadata.Id);
	private readonly Assembly executingAssembly = Assembly.GetExecutingAssembly();

	public HarmonyPatchManager(HitScoreVisualizer.Utilities.Services.ConfigLoader configLoader)
	{
		this.configLoader = configLoader;
	}

	public void Initialize()
	{
		try
		{
			HitScoreVisualizer.Components.LaneSamplePreparation.Enable(configLoader);
			harmony.PatchAll(executingAssembly);
		}
		catch (Exception e)
		{
			Plugin.Log.Error(e);
		}
	}

	public void Dispose()
	{
		HitScoreVisualizer.Components.LaneSamplePreparation.Disable();
		harmony.UnpatchSelf();
	}
}