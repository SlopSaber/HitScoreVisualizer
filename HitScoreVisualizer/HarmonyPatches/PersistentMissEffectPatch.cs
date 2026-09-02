using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HitScoreVisualizer.HarmonyPatches;

[HarmonyPatch]
internal static class PersistentMissEffectPatch
{
	private static readonly FieldInfo GetHide = AccessTools.Field(typeof(CoreGameHUDController.InitData), nameof(CoreGameHUDController.InitData.hide));

	/*
	 * Changes:
	 *  - if (_initData.hide)
	 *  + if (_initData.hide && !overrideNoTextsAndHuds)
	 *
	 * Description: Allows the MissedNoteEffectSpawner to subscribe to the noteWasMissedEvent event when HUD is disabled
	 */

	// Missed and BadCut have the same IL for the if statement
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(MissedNoteEffectSpawner), nameof(MissedNoteEffectSpawner.Start));
		yield return AccessTools.Method(typeof(BadNoteCutEffectSpawner), nameof(BadNoteCutEffectSpawner.Start));
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => new CodeMatcher(instructions)
		.MatchStartForward(
			new CodeMatch(OpCodes.Ldfld, GetHide),
			new CodeMatch(OpCodes.Brfalse)
		)
		.ThrowIfInvalid("Couldn't find match for if (_initData.hide)")
		.Advance(1) // Insert new boolean between "hide" and the Brfalse
		.Insert(
			Transpilers.EmitDelegate<Func<bool, bool>>(
				(hide => hide && !Plugin.Config.OverrideNoTextsAndHuds))
		)
		.InstructionEnumeration();
}
