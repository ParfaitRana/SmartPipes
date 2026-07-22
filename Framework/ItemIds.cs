namespace SmartPipes.Framework;

internal static class ItemIds
{
    public const string Pipe = "(O)ParfaitRana.SmartPipes_Pipe";
    public const string Port = "(O)ParfaitRana.SmartPipes_Port";
    public const string ShippingValve = "(O)ParfaitRana.SmartPipes_ShippingValve";
    public const string Blueprint = "(O)ParfaitRana.SmartPipes_Blueprint";
    public const string Wrench = "(T)ParfaitRana.SmartPipes_Wrench";

    public const string PipeRecipe = "ParfaitRana.SmartPipes_Pipe";
    public const string PortRecipe = "ParfaitRana.SmartPipes_Port";
    public const string ShippingValveRecipe = "ParfaitRana.SmartPipes_ShippingValve";
    public const string WrenchRecipe = "ParfaitRana.SmartPipes_Wrench";

    public const string LegacyPipe = "(O)YanYim.SmartPipes_Pipe";
    public const string LegacyPort = "(O)YanYim.SmartPipes_Port";
    public const string LegacyShippingValve = "(O)YanYim.SmartPipes_ShippingValve";
    public const string LegacyObjectWrench = "(O)YanYim.SmartPipes_Wrench";
    public const string LegacyToolWrench = "(T)YanYim.SmartPipes_Wrench";

    public static bool RevealsPipes(string? qualifiedItemId)
    {
        return qualifiedItemId is Pipe or Port or ShippingValve or Wrench
            or LegacyPipe or LegacyPort or LegacyShippingValve or LegacyObjectWrench or LegacyToolWrench;
    }

    public static string? GetReplacement(string qualifiedItemId)
    {
        return qualifiedItemId switch
        {
            LegacyPipe => Pipe,
            LegacyPort => Port,
            LegacyShippingValve => ShippingValve,
            LegacyObjectWrench or LegacyToolWrench => Wrench,
            _ => null
        };
    }
}
