using System;
using System.IO;
using System.Linq;
using System.Text;
using Aml.SimplePlayerCorpse.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Aml.SimplePlayerCorpse.Systems;

public class Commands : ModSystem
{
    private ICoreServerAPI? _serverApi = null;
    private DeathContentManager? _deathContentManager = null;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _serverApi = api;
        _deathContentManager = api.ModLoader.GetModSystem<DeathContentManager>();

        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands
            .Create("deaths")
            .RequiresPrivilege(Privilege.gamemode)
            .WithDescription("Handle unclaim deaths of player")
            .BeginSubCommand("list")
                .WithArgs(parsers.OnlinePlayer("player"))
                .HandleWith(ShowDeathList)
            .EndSubCommand()
            .BeginSubCommand("return")
                .WithArgs(
                    parsers.OnlinePlayer("player"),
                    parsers.OptionalInt("id", 0)
                )
                .HandleWith(ReturnThings)
            .EndSubCommand();
    }

    private TextCommandResult ShowDeathList(TextCommandCallingArgs args)
    {
        IPlayer player = (IPlayer)args[0];
        string[] files = _deathContentManager?.GetDeathDataFiles(player) ?? [];

        if (files.Length == 0)
        {
            return TextCommandResult.Error(Lang.Get("No data saved"));
        }

        var sb = new StringBuilder();
        for (int i = 0; i < files.Length; i++)
        {
            sb.AppendLine($"{i}. {Path.GetFileName(files[i])}");
        }
        return TextCommandResult.Success(sb.ToString());
    }

    private TextCommandResult ReturnThings(TextCommandCallingArgs args)
    {
        IPlayer player = (IPlayer)args[0];
        int id = 0;

        if (args.ArgCount == 2)
        {
            id = (int)args[1];
        }

        string[] files = _deathContentManager?.GetDeathDataFiles(player) ?? [];

        if (_serverApi == null)
        {
            return TextCommandResult.Error("Server Api is null");
        }

        if (player == null)
        {
            return TextCommandResult.Error("Server Api is null");
        }

        if (id < 0 || files.Length <= id)
        {
            return TextCommandResult.Error(Lang.Get("Index {0} not found", id));
        }

        var dcm = _serverApi.ModLoader.GetModSystem<DeathContentManager>();
        (Guid corpseId, Vec3d position, InventoryGeneric inventory) = dcm.LoadLastDeathContent(player, id);

        foreach (var slot in inventory)
        {
            if (slot.Empty)
            {
                continue;
            }

            if (!player.InventoryManager.TryGiveItemstack(slot.Itemstack))
            {
                _serverApi.World.SpawnItemEntity(slot.Itemstack, player.Entity.Pos.XYZ.AddCopy(0, 1, 0) ?? new Vec3d(0, 0, 0));
            }
            slot.Itemstack = null;
            slot.MarkDirty();
        }

        var corpseEntities = _serverApi.World.GetEntitiesAround(position, 1f, 1f, e =>
        {
            if (e is EntityPlayerCorpse entityPlayerCorpse)
            {
                return entityPlayerCorpse.CorpseId == corpseId;
            }

            return false;
        });

        if (corpseEntities.SingleOrDefault() is EntityPlayerCorpse corpseEntity)
        {
            corpseEntity.Die();
            DeathContentManager.RemoveDeathPoint(player.Entity, corpseEntity);
        }

        return TextCommandResult.Success(Lang.Get(
            "Returned things to {0} with index {1}",
            player.PlayerName, id));
    }
}