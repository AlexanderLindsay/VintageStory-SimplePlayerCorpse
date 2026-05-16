using Vintagestory.API.Common;
using Aml.SimplePlayerCorpse.Entities;
using System;

namespace Aml.SimplePlayerCorpse;

public class Core : ModSystem
{
    public static Config Config { get; private set; } = new();
    private const string ConfigFileName = "aml.simpleplayercorpse.json";

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterEntity("EntityPlayerCorpse", typeof(EntityPlayerCorpse));

        TryToLoadConfig(api);
    }

    private void TryToLoadConfig(ICoreAPI api)
    {
        try
        {
            Config = api.LoadModConfig<Config>(ConfigFileName) ?? new();

            api.StoreModConfig(Config, ConfigFileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Could not load the config file. Loading default settings instead.");
            Mod.Logger.Error(e);
            Config = new();
        }
    }
}
