﻿using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Dalamud.Plugin.Ipc;
using Glamourer.Structs;
using ImGuiNET;
using Penumbra.GameData.Enums;

namespace Glamourer.Api;

public class PenumbraAttach : IDisposable
{
    public const int RequiredPenumbraBreakingVersion = 4;
    public const int RequiredPenumbraFeatureVersion  = 0;

    private ICallGateSubscriber<ChangedItemType, uint, object>?                   _tooltipSubscriber;
    private ICallGateSubscriber<MouseButton, ChangedItemType, uint, object>?      _clickSubscriber;
    private ICallGateSubscriber<string, int, object>?                             _redrawSubscriberName;
    private ICallGateSubscriber<GameObject, int, object>?                         _redrawSubscriberObject;
    private ICallGateSubscriber<IntPtr, (IntPtr, string)>?                        _drawObjectInfo;
    private ICallGateSubscriber<IntPtr, string, IntPtr, IntPtr, IntPtr, object?>? _creatingCharacterBase;

    private readonly ICallGateSubscriber<object?> _initializedEvent;
    private readonly ICallGateSubscriber<object?> _disposedEvent;

    public event Action<IntPtr, IntPtr, IntPtr, IntPtr>? CreatingCharacterBase;

    public PenumbraAttach(bool attach)
    {
        _initializedEvent = Dalamud.PluginInterface.GetIpcSubscriber<object?>("Penumbra.Initialized");
        _disposedEvent    = Dalamud.PluginInterface.GetIpcSubscriber<object?>("Penumbra.Disposed");
        _initializedEvent.Subscribe(Reattach);
        _disposedEvent.Subscribe(Unattach);
        Reattach(attach);
    }

    private void Reattach()
        => Reattach(Glamourer.Config.AttachToPenumbra);

    public void Reattach(bool attach)
    {
        try
        {
            Unattach();

            var versionSubscriber = Dalamud.PluginInterface.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersions");
            var (breaking, feature) = versionSubscriber.InvokeFunc();
            if (breaking != RequiredPenumbraBreakingVersion || feature < RequiredPenumbraFeatureVersion)
                throw new Exception(
                    $"Invalid Version {breaking}.{feature:D4}, required major Version {RequiredPenumbraBreakingVersion} with feature greater or equal to {RequiredPenumbraFeatureVersion}.");

            _redrawSubscriberName   = Dalamud.PluginInterface.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            _redrawSubscriberObject = Dalamud.PluginInterface.GetIpcSubscriber<GameObject, int, object>("Penumbra.RedrawObject");
            _drawObjectInfo         = Dalamud.PluginInterface.GetIpcSubscriber<IntPtr, (IntPtr, string)>("Penumbra.GetDrawObjectInfo");

            if (!attach)
                return;

            _tooltipSubscriber = Dalamud.PluginInterface.GetIpcSubscriber<ChangedItemType, uint, object>("Penumbra.ChangedItemTooltip");
            _clickSubscriber =
                Dalamud.PluginInterface.GetIpcSubscriber<MouseButton, ChangedItemType, uint, object>("Penumbra.ChangedItemClick");
            _creatingCharacterBase =
                Dalamud.PluginInterface.GetIpcSubscriber<IntPtr, string, IntPtr, IntPtr, IntPtr, object?>("Penumbra.CreatingCharacterBase");
            _tooltipSubscriber.Subscribe(PenumbraTooltip);
            _clickSubscriber.Subscribe(PenumbraRightClick);
            _creatingCharacterBase.Subscribe(SubscribeCharacterBase);
            PluginLog.Debug("Glamourer attached to Penumbra.");
        }
        catch (Exception e)
        {
            PluginLog.Debug($"Could not attach to Penumbra:\n{e}");
        }
    }

    private void SubscribeCharacterBase(IntPtr gameObject, string _, IntPtr modelId, IntPtr customize, IntPtr equipment)
        => CreatingCharacterBase?.Invoke(gameObject, modelId, customize, equipment);

    public void Unattach()
    {
        _tooltipSubscriber?.Unsubscribe(PenumbraTooltip);
        _clickSubscriber?.Unsubscribe(PenumbraRightClick);
        _creatingCharacterBase?.Unsubscribe(SubscribeCharacterBase);
        _tooltipSubscriber     = null;
        _clickSubscriber       = null;
        _creatingCharacterBase = null;
        _redrawSubscriberName  = null;
        _drawObjectInfo        = null;
        if (_redrawSubscriberObject != null)
        {
            PluginLog.Debug("Glamourer detached from Penumbra.");
            _redrawSubscriberObject = null;
        }
    }

    public void Dispose()
    {
        _initializedEvent.Unsubscribe(Reattach);
        _disposedEvent.Unsubscribe(Unattach);
        Unattach();
    }

    private static void PenumbraTooltip(ChangedItemType type, uint _)
    {
        if (type == ChangedItemType.Item)
            ImGui.Text("Right click to apply to current Glamourer Set. [Glamourer]");
    }

    private void PenumbraRightClick(MouseButton button, ChangedItemType type, uint id)
    {
        if (button != MouseButton.Right || type != ChangedItemType.Item)
            return;

        //var gPose     = ObjectManager.GPosePlayer;
        //var player    = ObjectManager.Player;
        //var item      = (Lumina.Excel.GeneratedSheets.Item)type.GetObject(id)!;
        //var writeItem = new Item(item, string.Empty);
        //if (gPose != null)
        //{
        //    writeItem.Write(gPose.Address);
        //    UpdateCharacters(gPose, player);
        //}
        //else if (player != null)
        //{
        //    writeItem.Write(player.Address);
        //    UpdateCharacters(player);
        //}
    }

    public Actor GameObjectFromDrawObject(IntPtr drawObject)
        => _drawObjectInfo?.InvokeFunc(drawObject).Item1 ?? IntPtr.Zero;

    public void RedrawObject(GameObject? actor, RedrawType settings, bool repeat)
    {
        if (actor == null)
            return;

        if (_redrawSubscriberObject != null)
        {
            try
            {
                _redrawSubscriberObject.InvokeAction(actor, (int)settings);
            }
            catch (Exception e)
            {
                if (repeat)
                {
                    Reattach(Glamourer.Config.AttachToPenumbra);
                    RedrawObject(actor, settings, false);
                }
                else
                {
                    PluginLog.Debug($"Failure redrawing object:\n{e}");
                }
            }
        }
        else if (repeat)
        {
            Reattach(Glamourer.Config.AttachToPenumbra);
            RedrawObject(actor, settings, false);
        }
        else
        {
            PluginLog.Debug("Trying to redraw object, but not attached to Penumbra.");
        }
    }

    // Update objects without triggering PlayerWatcher Events,
    // then manually redraw using Penumbra.
    public void UpdateCharacters(Character character, Character? gPoseOriginalCharacter = null)
    {
        //RedrawObject(character, RedrawType.Redraw, true);
        //
        //// Special case for carrying over changes to the gPose player to the regular player, too.
        //if (gPoseOriginalCharacter == null)
        //    return;
        //
        //newEquip.Write(gPoseOriginalCharacter.Address);
        //RedrawObject(gPoseOriginalCharacter, RedrawType.AfterGPose, false);
    }
}
