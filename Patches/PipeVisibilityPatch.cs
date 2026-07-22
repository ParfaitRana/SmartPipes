using System.Reflection;
using HarmonyLib;
using SmartPipes.Framework;
using SObject = StardewValley.Object;

namespace SmartPipes.Patches;

[HarmonyPatch]
internal static class PipeVisibilityPatch
{
    internal static Func<bool>? ShouldShowPipes { get; set; }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        return AccessTools.GetDeclaredMethods(typeof(SObject))
            .Where(method => method.Name == nameof(SObject.draw));
    }

    private static bool Prefix(SObject __instance)
    {
        return __instance.QualifiedItemId != ItemIds.Pipe
            || ShouldShowPipes?.Invoke() != false;
    }
}
