using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using SimplePlayerCorpse.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SimplePlayerCorpse.Systems;

public class DeathContentManager : ModSystem
{
    private static readonly MethodInfo _resendWaypointsMethod = AccessTools.Method(typeof(WaypointMapLayer), "ResendWaypoints");
    private static readonly MethodInfo _rebuildMapComponentsMethod = AccessTools.Method(typeof(WaypointMapLayer), "RebuildMapComponents");

    private ICoreServerAPI? _serverApi = null;
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _serverApi = api;
        api.Event.OnEntityDeath += OnEntityDeath;
    }

    private void OnEntityDeath(Entity entity, DamageSource damageSource)
    {
        if (entity is EntityPlayer entityPlayer)
        {
            OnPlayerDeath((IServerPlayer)entityPlayer.Player);
        }
    }

    private void OnPlayerDeath(IServerPlayer byPlayer)
    {
        bool isKeepContent = byPlayer.Entity?.Properties?.Server?.Attributes?.GetBool("keepContents") ?? false;
        if (isKeepContent)
        {
            return;
        }

        var corpseEntity = CreateCorpseEntity(byPlayer);
        if (corpseEntity.Inventory != null && !corpseEntity.Inventory.Empty)
        {
            if (Core.Config.CreateWaypointOnDeath)
            {
                CreateDeathPoint(byPlayer.Entity, corpseEntity);
            }

            if (Core.Config.MaxCorpseCount > 0)
            {
                SaveDeathContent(corpseEntity, byPlayer);
            }

            _serverApi?.World.SpawnEntity(corpseEntity);

            string message = string.Format(
                "Created {0} at {1}, id {2}",
                corpseEntity.GetName(),
                Utils.MakeRelativePos(corpseEntity.Pos.XYZ, _serverApi?.World.DefaultSpawnPosition.XYZ ?? new Vec3d(0, 0, 0)),
                corpseEntity.EntityId);

            Mod.Logger.Notification(message);
        }
        else
        {
            string message = $"Inventory is empty, {corpseEntity.OwnerName}'s corpse not created";
            Mod.Logger.Notification(message);
        }
    }

    private EntityPlayerCorpse CreateCorpseEntity(IServerPlayer byPlayer)
    {
        if (_serverApi is null)
        {
            throw new Exception("Unable to instantiate player corpse: No Server Api");
        }

        var entityLocation = new AssetLocation(Constants.ModId, "playercorpse");
        var entityType = _serverApi.World.GetEntityType(entityLocation);
        if (entityType is null)
        {
            throw new Exception("Unable to instantiate player corpse: No entity type found");
        }

        if (_serverApi.World.ClassRegistry.CreateEntity(entityType) is not EntityPlayerCorpse corpse)
        {
            throw new Exception("Unable to instantiate player corpse: Incorrect entity type");
        }

        corpse.OwnerUID = byPlayer.PlayerUID;
        corpse.OwnerName = byPlayer.PlayerName;
        corpse.CreationTime = _serverApi.World.Calendar.TotalHours;
        corpse.CreationRealDatetime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        corpse.Inventory = TakeContentFromPlayer(byPlayer);

        BlockPos floorPos = TryFindFloor(byPlayer.Entity.Pos.AsBlockPos);
        var pos = floorPos.ToVec3d().Add(.5, 0, .5);

        corpse.Pos.SetPos(pos);
        corpse.World = _serverApi.World;

        return corpse;
    }

    private BlockPos TryFindFloor(BlockPos pos)
    {
        var floorPos = new BlockPos(pos.dimension);

        for (int i = pos.Y; i > 0; i--)
        {
            floorPos.Set(pos.X, i, pos.Z);
            var block = _serverApi?.World.BlockAccessor.GetBlock(floorPos);
            if (block != null && block.BlockId != 0 && block.CollisionBoxes?.Length > 0)
            {
                floorPos.Set(pos.X, i + 1, pos.Z);
                return floorPos;
            }
        }

        return pos;
    }

    private static int GetMaxCorpseSlots(IServerPlayer byPlayer, IEnumerable<string> inventoryTypes)
    {
        int maxCorpseSlots = 0;
        foreach (var invClassName in inventoryTypes)
        {
            maxCorpseSlots += byPlayer.InventoryManager.GetOwnInventory(invClassName)?.Count ?? 0;
        }
        return maxCorpseSlots;
    }

    private InventoryGeneric TakeContentFromPlayer(IServerPlayer byPlayer)
    {
        var inventoryTypes = Constants.InventoryTypes;

        var maxCorpseSlots = GetMaxCorpseSlots(byPlayer, inventoryTypes);
        var inv = new InventoryGeneric(maxCorpseSlots, $"playercorpse-{byPlayer.PlayerUID}", _serverApi);

        int lastSlotId = 0;

        var isDropArmorVanilla = byPlayer.Entity.Properties.Server?.Attributes?.GetBool("dropArmorOnDeath") ?? false;
        var isDropArmor = isDropArmorVanilla;

        foreach (var invClassName in inventoryTypes)
        {
            if (invClassName == GlobalConstants.characterInvClassName && !isDropArmor)
            {
                continue;
            }

            if (invClassName.Equals(GlobalConstants.backpackInvClassName) &&
                byPlayer.InventoryManager.GetOwnInventory("xskillshotbar") != null
            )
            {
                int i = 0;
                var backpackInv = byPlayer.InventoryManager.GetOwnInventory(invClassName);
                foreach (var slot in backpackInv)
                {
                    if (i > backpackInv.Count - 4)
                    {
                        break;
                    }

                    inv[lastSlotId++].Itemstack = TakeSlotContent(slot);
                }
                continue;
            }

            foreach (var slot in byPlayer.InventoryManager.GetOwnInventory(invClassName))
            {
                inv[lastSlotId++].Itemstack = TakeSlotContent(slot);
            }
        }

        return inv;
    }

    private static ItemStack? TakeSlotContent(ItemSlot slot)
    {
        if (slot.Empty)
        {
            return null;
        }

        if (slot.Inventory.ClassName == GlobalConstants.characterInvClassName)
        {
            bool isArmor = slot.Itemstack.ItemAttributes?["protectionModifiers"].Exists ?? false;
            if (!isArmor)
            {
                return null;
            }
        }

        return slot.TakeOutWhole();
    }

    public static void CreateDeathPoint(EntityPlayer? byPlayer, EntityPlayerCorpse corpseEntity)
    {
        if (byPlayer?.Api is ICoreServerAPI)
        {
            var mapLayer = GetMapLayer(byPlayer.Api);

            if (mapLayer is null)
            {
                byPlayer.Api.Logger.Error("Failed to create waypoint, maplayer is null");
                return;
            }

            var wp = new Waypoint()
            {
                Position = byPlayer.Pos.AsBlockPos.ToVec3d(),
                Title = $"{Lang.Get($"{Constants.ModId}:item-creature-playercorpse")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                Pinned = Core.Config.PinWaypoint,
                Icon = Core.Config.WaypointIcon,
                Color = ColorUtil.ColorFromRgba(Core.Config.WaypointColor.Z, Core.Config.WaypointColor.Y, Core.Config.WaypointColor.X, 255), //actually bgra
                OwningPlayerUid = byPlayer.PlayerUID,
                Guid = corpseEntity.CorpseId.ToString()
            };

            mapLayer.AddWaypoint(wp, byPlayer.Player as IServerPlayer);
        }
    }

    private static WaypointMapLayer? GetMapLayer(ICoreAPI api)
    {
        return api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.FirstOrDefault(ml => ml is WaypointMapLayer) as WaypointMapLayer;
    }

    public static void RemoveDeathPoint(EntityPlayer byPlayer, EntityPlayerCorpse corpseEntity)
    {
        if (byPlayer is null || corpseEntity is null) return;

        if (byPlayer.Api is ICoreServerAPI sapi)
        {
            var serverPlayer = byPlayer.Player as IServerPlayer;
            var mapLayer = GetMapLayer(sapi);
            var waypoints = mapLayer?.Waypoints ?? [];

            foreach (Waypoint waypoint in waypoints.ToList().Where(w => w.OwningPlayerUid == byPlayer.PlayerUID))
            {
                if (waypoint.Guid == corpseEntity.CorpseId.ToString())
                {
                    waypoints.Remove(waypoint);
                    _resendWaypointsMethod.Invoke(mapLayer, [serverPlayer]);
                    _rebuildMapComponentsMethod.Invoke(mapLayer, null);
                }
            }
        }
    }

    public string GetDeathDataPath(IPlayer player)
    {
        ICoreAPI api = player.Entity.Api;
        string uidFixed = Regex.Replace(player.PlayerUID, "[^0-9a-zA-Z]", "");
        string localPath = Path.Combine("ModData", api?.World?.SavegameIdentifier ?? "null", Mod.Info.ModID, uidFixed);
        return api?.GetOrCreateDataPath(localPath) ?? string.Empty;
    }

    public string[] GetDeathDataFiles(IPlayer player)
    {
        string path = GetDeathDataPath(player);
        return Directory
            .GetFiles(path)
            .OrderByDescending(f => new FileInfo(f).Name)
            .ToArray();
    }

    public void SaveDeathContent(EntityPlayerCorpse playerCorpse, IPlayer player)
    {
        string path = GetDeathDataPath(player);
        string[] files = GetDeathDataFiles(player);

        for (int i = files.Length - 1; i > Core.Config.MaxCorpseCount - 2; i--)
        {
            File.Delete(files[i]);
        }

        var tree = new TreeAttribute();
        playerCorpse.Inventory?.ToTreeAttributes(tree);
        tree.SetBytes("corpseId", playerCorpse.CorpseId.ToByteArray());
        tree.SetDouble("corpsePosition.x", playerCorpse.Pos.X);
        tree.SetDouble("corpsePosition.y", playerCorpse.Pos.Y);
        tree.SetDouble("corpsePosition.z", playerCorpse.Pos.Z);

        string name = $"inventory-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.dat";
        File.WriteAllBytes($"{path}/{name}", tree.ToBytes());
    }

    public (Guid, Vec3d, InventoryGeneric) LoadLastDeathContent(IPlayer player, int offset = 0)
    {
        if (Core.Config.MaxCorpseCount <= offset)
        {
            throw new IndexOutOfRangeException("offset is too large or save data disabled");
        }

        string file = GetDeathDataFiles(player).ElementAt(offset);

        var tree = new TreeAttribute();
        tree.FromBytes(File.ReadAllBytes(file));

        var inv = new InventoryGeneric(tree.GetInt("qslots"), $"playercorpse-{player.PlayerUID}", player.Entity.Api);
        inv.FromTreeAttributes(tree);
        var id = new Guid(tree.GetBytes("corpseId"));

        var x = tree.GetDouble("corpsePosition.x");
        var y = tree.GetDouble("corpsePosition.y");
        var z = tree.GetDouble("corpsePosition.z");
        var position = new Vec3d(x,y,z);

        return (id, position, inv);
    }
}