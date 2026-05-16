using System.Collections.Immutable;
using Vintagestory.API.Config;

namespace Aml.SimplePlayerCorpse;

public static class Constants
{
    public static string ModId => "aml.simpleplayercorpse";

    public static ImmutableList<string> InventoryTypes => [
        GlobalConstants.hotBarInvClassName,
            GlobalConstants.backpackInvClassName,
            GlobalConstants.craftingInvClassName,
            GlobalConstants.mousecursorInvClassName,
            GlobalConstants.characterInvClassName
    ];
}