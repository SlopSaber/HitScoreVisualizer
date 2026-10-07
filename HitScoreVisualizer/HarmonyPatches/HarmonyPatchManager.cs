using System;
using System.Reflection;
using HarmonyLib;
using Zenject;

namespace HitScoreVisualizer.HarmonyPatches;

internal class HarmonyPatchManager : IInitializable, IDisposable
{
	private readonly Harmony harmony = new(Plugin.Metadata.Id);
	private readonly Assembly executingAssembly = Assembly.GetExecutingAssembly();

	public void Initialize()
	{
		try
		{
			HitScoreVisualizer.Components.LaneSamplePreparation.Enable();
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