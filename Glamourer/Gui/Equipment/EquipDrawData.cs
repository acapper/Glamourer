﻿using Dalamud.Game.Inventory;
using Glamourer.Designs;
using Glamourer.Events;
using Glamourer.State;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public ref struct EquipDrawData(EquipSlot slot, in DesignData designData)
{
    public readonly EquipSlot Slot = slot;
    public          bool      Locked;
    public          bool      DisplayApplication;
    public          bool      AllowRevert;

    public Action<EquipItem> ItemSetter       = null!;
    public Action<StainId>   StainSetter      = null!;
    public Action<bool>      ApplySetter      = null!;
    public Action<bool>      ApplyStainSetter = null!;
    public EquipItem         CurrentItem      = designData.Item(slot);
    public StainId           CurrentStain     = designData.Stain(slot);
    public EquipItem         GameItem         = default;
    public StainId           GameStain        = default;
    public bool              CurrentApply;
    public bool              CurrentApplyStain;

    public readonly Gender CurrentGender = designData.Customize.Gender;
    public readonly Race   CurrentRace   = designData.Customize.Race;

    public static EquipDrawData FromDesign(DesignManager manager, Design design, EquipSlot slot)
        => new(slot, design.DesignData)
        {
            ItemSetter = slot.IsEquipment() || slot.IsAccessory()
                ? i => manager.ChangeEquip(design, slot, i)
                : i => manager.ChangeWeapon(design, slot, i),
            StainSetter        = i => manager.ChangeStain(design, slot, i),
            ApplySetter        = b => manager.ChangeApplyEquip(design, slot, b),
            ApplyStainSetter   = b => manager.ChangeApplyStain(design, slot, b),
            CurrentApply       = design.DoApplyEquip(slot),
            CurrentApplyStain  = design.DoApplyStain(slot),
            Locked             = design.WriteProtected(),
            DisplayApplication = true,
        };

    public static EquipDrawData FromState(StateManager manager, ActorState state, EquipSlot slot)
        => new(slot, state.ModelData)
        {
            ItemSetter         = i => manager.ChangeItem(state, slot, i, StateSource.Manual),
            StainSetter        = i => manager.ChangeStain(state, slot, i, StateSource.Manual),
            Locked             = state.IsLocked,
            DisplayApplication = false,
            GameItem           = state.BaseData.Item(slot),
            GameStain          = state.BaseData.Stain(slot),
            AllowRevert        = true,
        };
}
