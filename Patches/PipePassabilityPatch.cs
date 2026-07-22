using System.Reflection;
using HarmonyLib;
using SmartPipes.Framework;
using SObject = StardewValley.Object;

namespace SmartPipes.Patches;

/// <summary>Makes placed Smart Pipes behave like floor infrastructure for collision checks.</summary>
[HarmonyPatch]
internal static class PipePassabilityPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return AccessTools.GetDeclaredMethods(typeof(SObject))
            .Where(method => method.Name == "isPassable" && method.ReturnType == typeof(bool));
    }

    private static bool Prefix(SObject __instance, ref bool __result)
    {
        if (__instance.QualifiedItemId is not (ItemIds.Pipe or ItemIds.LegacyPipe))
            return true;

        __result = true;
        return false;
    }
}
