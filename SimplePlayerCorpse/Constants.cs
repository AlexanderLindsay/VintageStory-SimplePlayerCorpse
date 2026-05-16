using System.Collections.Immutable;
using Vintagestory.API.Config;

namespace SimplePlayerCorpse;

public static class Constants
{
    public static string ModId => "amlsimpleplayercorpse";

    public static ImmutableList<string> InventoryTypes => [
        GlobalConstants.hotBarInvClassName,
            GlobalConstants.backpackInvClassName,
            GlobalConstants.craftingInvClassName,
            GlobalConstants.mousecursorInvClassName,
            GlobalConstants.characterInvClassName
    ];
}