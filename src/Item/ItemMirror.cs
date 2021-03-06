﻿using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace TemporalMirror
{
    public class ItemMirror : Item
    {
        SimpleParticleProperties particles = new SimpleParticleProperties(
            minQuantity: 1,
            maxQuantity: 1,
            color: ColorUtil.WhiteAhsl,
            minPos: new Vec3d(),
            maxPos: new Vec3d(),
            minVelocity: new Vec3f(-0.25f, 0.1f, -0.25f),
            maxVelocity: new Vec3f(0.25f, 0.1f, 0.25f),
            lifeLength: 0.2f,
            gravityEffect: 0.075f,
            minSize: 0.1f,
            maxSize: 0.1f,
            model: EnumParticleModel.Cube
        );
        ILoadedSound sound;

        bool teleported;
        bool isSavePoint;
        BlockPos beforeTpPos;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (slot.Itemstack.Item.Variant["type"] == "frame") return;

            if (slot.Itemstack.Item.Variant["type"] == "magic")
            {
                isSavePoint = false;

                if (blockSel != null && byEntity.Controls.Sneak)
                {
                    slot.Itemstack.Attributes.SetInt("x", blockSel.Position.X);
                    slot.Itemstack.Attributes.SetInt("y", blockSel.Position.Y);
                    slot.Itemstack.Attributes.SetInt("z", blockSel.Position.Z);

                    handling = EnumHandHandling.Handled;
                    isSavePoint = true;
                    return;
                }

                if (!slot.Itemstack.Attributes.HasAttribute("x")) return;
            }

            if (byEntity.World is IClientWorldAccessor world)
            {
                sound = world.LoadSound(new SoundParams()
                {
                    Location = new AssetLocation(Constants.MOD_ID, "sounds/teleport.ogg"),
                    ShouldLoop = false,
                    Position = byEntity.Pos.XYZ.ToVec3f(),
                    DisposeOnFinish = true,
                    Volume = 1f,
                    Pitch = 0.7f
                });
                sound?.Start();
            }

            teleported = false;
            handling = EnumHandHandling.PreventDefault;

        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (api.Side == EnumAppSide.Client)
            {
                ModelTransform tf = new ModelTransform();
                tf.EnsureDefaultValues();

                float trans = Math.Min(secondsUsed, Constants.SECONDS_FOR_OPEN);
                tf.Translation.Set(-trans * 0.05f, trans * 0.025f, trans * 0.05f);
                byEntity.Controls.UsingHeldItemTransformAfter = tf;

                if (secondsUsed > 0.5)
                {
                    Vec3d pos =
                       byEntity.Pos.XYZ.Add(0, byEntity.LocalEyePos.Y, 0)
                        .Ahead(0.5, byEntity.Pos.Pitch, byEntity.Pos.Yaw);
                    ;

                    Random rand = new Random();
                    particles.Color = GetRandomColor(api as ICoreClientAPI, slot.Itemstack);

                    particles.MinPos = pos.AddCopy(-0.05, -0.05, -0.05);
                    particles.AddPos.Set(0.1, 0.1, 0.1);
                    particles.MinSize = 0.01f;
                    particles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.SINUS, 0.5f);
                    byEntity.World.SpawnParticles(particles);
                }
            }

            if (!teleported && secondsUsed > Constants.SECONDS_FOR_OPEN)
            {
                beforeTpPos = byEntity.Pos.AsBlockPos;

                if (slot.Itemstack.Item.Variant["type"] == "magic")
                {
                    Vec3d tpPos = new Vec3d(
                        slot.Itemstack.Attributes.GetInt("x") + 0.5f,
                        slot.Itemstack.Attributes.GetInt("y") + 1,
                        slot.Itemstack.Attributes.GetInt("z") + 0.5f
                    );


                    if ((int)beforeTpPos.DistanceTo(tpPos.AsBlockPos) >= slot.Itemstack.Collectible.Durability)
                    {
                        return false;
                    }

                    if (!byEntity.Teleporting)
                    {
                        byEntity.TeleportToDouble(tpPos.X, tpPos.Y, tpPos.Z, () => teleported = true);
                        api.World.Logger.ModNotification("Teleported to {0}", tpPos);
                    }
                }
                else if (slot.Itemstack.Item.Variant["type"] == "wormhole")
                {
                    string playerUID = slot.Itemstack.Attributes.GetString("playerUID") ?? "";
                    IPlayer toPlayer = api.World.PlayerByUid(playerUID);

                    if (toPlayer?.Entity == null || !api.World.AllOnlinePlayers.Contains(toPlayer))
                    {
                        if (api.Side == EnumAppSide.Client)
                        {
                            GuiDialogWormholeMirror dlg = new GuiDialogWormholeMirror(api as ICoreClientAPI);
                            dlg.TryOpen();
                        }
                    }
                    else
                    {
                        if ((int)beforeTpPos.DistanceTo(toPlayer.Entity.Pos.AsBlockPos) >= slot.Itemstack.Collectible.Durability)
                        {
                            return false;
                        }

                        if (!byEntity.Teleporting)
                        {
                            byEntity.TeleportTo(toPlayer.Entity.Pos, () => teleported = true);
                            api.World.Logger.ModNotification("Teleported to {0} at {1}", toPlayer.PlayerName, toPlayer.Entity.Pos);
                            byEntity?.WatchedAttributes?.SetString("playerUID", null);
                        }
                    }
                }
            }

            return !teleported;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            bool flag = base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);

            if (flag)
            {
                sound?.Stop();
            }

            return flag;
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            sound?.Stop();

            if (!isSavePoint && teleported && (byEntity as EntityPlayer)?.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, (int)beforeTpPos.DistanceTo(byEntity.Pos.AsBlockPos));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            if (inSlot.Itemstack.Item.Variant["type"] == "magic")
            {
                return new WorldInteraction[]
                {
                new WorldInteraction()
                {
                    ActionLangCode = Constants.MOD_ID + ":heldhelp-teleport",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction()
                {
                    ActionLangCode = Constants.MOD_ID + ":heldhelp-savepoint",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                }
                };
            }
            else if (inSlot.Itemstack.Item.Variant["type"] == "wormhole")
            {
                return new WorldInteraction[]
                {
                new WorldInteraction()
                {
                    ActionLangCode = Constants.MOD_ID + ":heldhelp-teleport-to-player",
                    MouseButton = EnumMouseButton.Right
                },
                };
            }
            else return null;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (inSlot.Itemstack.Attributes.HasAttribute("x"))
            {
                BlockPos tpPos = new BlockPos().Set(
                        inSlot.Itemstack.Attributes.GetInt("x"),
                        inSlot.Itemstack.Attributes.GetInt("y") + 1,
                        inSlot.Itemstack.Attributes.GetInt("z")
                );
                BlockPos mapTpPos = MapPos(tpPos);
                dsc.AppendLine(Lang.Get("Saved point: {0}, {1}, {2}", mapTpPos.X, mapTpPos.Y, mapTpPos.Z));
                dsc.AppendLine(Lang.Get("Distance: {0} m", (int)(api as ICoreClientAPI)?.World?.Player?.Entity?.Pos.AsBlockPos.DistanceTo(tpPos)));
            }
        }

        private BlockPos MapPos(BlockPos pos)
        {
            int x = pos.X - api.World.DefaultSpawnPosition.XYZInt.X;
            int z = pos.Z - api.World.DefaultSpawnPosition.XYZInt.Z;
            return new BlockPos(x, pos.Y + 1, z);
        }
    }
}