using HarmonyLib;
using SmartPipes.Framework;
using StardewValley.Quests;

namespace SmartPipes.Patches;

internal static class QuestDisplayPatch
{
    public static Func<string>? Title { get; set; }

    public static Func<string>? Description { get; set; }

    public static Func<string>? Objective { get; set; }

    private static bool IsTutorial(Quest quest) => quest.id.Value == StoryIds.TutorialQuest;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.questTitle), MethodType.Getter)]
    private static void AfterGetTitle(Quest __instance, ref string __result)
    {
        if (IsTutorial(__instance) && Title is not null)
            __result = Title();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.GetName))]
    private static void AfterGetName(Quest __instance, ref string __result)
    {
        if (IsTutorial(__instance) && Title is not null)
            __result = Title();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.questDescription), MethodType.Getter)]
    private static void AfterGetDescription(Quest __instance, ref string __result)
    {
        if (IsTutorial(__instance) && Description is not null)
            __result = Description();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.GetDescription))]
    private static void AfterGetDescriptionMethod(Quest __instance, ref string __result)
    {
        if (IsTutorial(__instance) && Description is not null)
            __result = Description();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.currentObjective), MethodType.Getter)]
    private static void AfterGetObjective(Quest __instance, ref string __result)
    {
        if (IsTutorial(__instance) && Objective is not null)
            __result = Objective();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.GetObjectiveDescriptions))]
    private static void AfterGetObjectiveDescriptions(Quest __instance, ref List<string> __result)
    {
        if (IsTutorial(__instance) && Objective is not null)
            __result = new List<string> { Objective() };
    }
}
