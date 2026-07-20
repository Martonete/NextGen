using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Manages inventory/spell tab switching, macro toggles, Lanzar button,
/// and inventory drag-drop routing. Extracted from Main.cs.
/// </summary>
public class InventoryUI
{
    private readonly GameState _state;

    // Tab switching state
    private bool _showingSpells;

    // Panel references (set via BindPanels)
    private InventoryPanel? _inventoryPanel;
    private SpellPanel? _spellPanel;
    private TextureButton? _dydToggle;
    private Texture2D? _dydOffTex;
    private Texture2D? _dydOnTex;
    private TextureButton? _lanzarButton;
    private TextureButton? _infoButton;
    private TextureButton? _spellUpButton;
    private TextureButton? _spellDownButton;
    private TextureRect? _invEquImage;
    private ImageTexture? _invEquInvTexture;
    private ImageTexture? _invEquSpellTexture;
    private TooltipPanel? _tooltipPanel;
    private TextureButton? _invTabButton;
    private TextureButton? _spellTabButton;

    /// <summary>Whether the spell tab is currently active.</summary>
    public bool ShowingSpells => _showingSpells;

    /// <summary>Callback to send a packet via TCP.</summary>
    public Action<byte[]>? SendPacket;

    /// <summary>Callback for showing the drop dialog (slot, itemName).</summary>
    public Action<int, string>? OnShowDropDialog;

    /// <summary>Callback to check if a drop landed on the trade panel.</summary>
    public Func<int, Vector2, bool>? TryTradeOffer;

    /// <summary>Callback to check if a drop landed on the vault panel.</summary>
    public Func<int, Vector2, bool>? TryVaultDeposit;

    /// <summary>Callback to check if a drop landed on the guild bank panel.</summary>
    public Func<int, Vector2, bool>? TryGuildBankDeposit;

    public InventoryUI(GameState state)
    {
        _state = state;
    }

    /// <summary>Bind references to inventory/spell UI nodes.</summary>
    public void BindPanels(
        InventoryPanel inventoryPanel, SpellPanel spellPanel,
        TextureButton dydToggle,
        Texture2D? dydOffTex, Texture2D? dydOnTex,
        TextureButton lanzarButton, TextureButton infoButton,
        TextureButton spellUpButton, TextureButton spellDownButton,
        TextureRect? invEquImage, ImageTexture? invEquInvTexture, ImageTexture? invEquSpellTexture,
        TooltipPanel? tooltipPanel)
    {
        _inventoryPanel = inventoryPanel;
        _spellPanel = spellPanel;
        _dydToggle = dydToggle;
        _dydOffTex = dydOffTex;
        _dydOnTex = dydOnTex;
        _lanzarButton = lanzarButton;
        _infoButton = infoButton;
        _spellUpButton = spellUpButton;
        _spellDownButton = spellDownButton;
        _invEquImage = invEquImage;
        _invEquInvTexture = invEquInvTexture;
        _invEquSpellTexture = invEquSpellTexture;
        _tooltipPanel = tooltipPanel;
    }

    public void BindTabButtons(TextureButton invTab, TextureButton spellTab)
    {
        _invTabButton = invTab;
        _spellTabButton = spellTab;
        UpdateTabVisuals();
    }

    private void UpdateTabVisuals()
    {
        if (_invTabButton != null)
            _invTabButton.Modulate = _showingSpells ? new Color(0.6f, 0.55f, 0.5f) : Colors.White;
        if (_spellTabButton != null)
            _spellTabButton.Modulate = _showingSpells ? Colors.White : new Color(0.6f, 0.55f, 0.5f);
    }

    /// <summary>Update InvEqu textures after background image loading.</summary>
    public void SetInvEquTextures(ImageTexture? invTex, ImageTexture? spellTex)
    {
        _invEquInvTexture = invTex;
        _invEquSpellTexture = spellTex;
        // Default to inventory background
        if (_invEquImage != null && _invEquInvTexture != null)
            _invEquImage.Texture = _invEquInvTexture;
    }

    /// <summary>Switch to inventory tab (VB6: default view).</summary>
    public void OnInventoryTabPressed()
    {
        _showingSpells = false;
        _tooltipPanel?.Hide();
        _inventoryPanel!.Visible = true;
        _dydToggle!.Visible = true;
        // Sync DyD button texture with current state
        _dydToggle.TextureNormal = _inventoryPanel.DyDEnabled ? _dydOnTex : _dydOffTex;
        _spellPanel!.Visible = false;
        _lanzarButton!.Visible = false;
        _infoButton!.Visible = false;
        _spellUpButton!.Visible = false;
        _spellDownButton!.Visible = false;
        if (_invEquImage != null && _invEquInvTexture != null)
            _invEquImage.Texture = _invEquInvTexture;
        UpdateTabVisuals();
    }

    /// <summary>Switch to spell tab.</summary>
    public void OnSpellTabPressed()
    {
        _showingSpells = true;
        _tooltipPanel?.Hide();
        _inventoryPanel!.CancelDrag();
        _inventoryPanel!.Visible = false;
        _dydToggle!.Visible = false;
        _spellPanel!.Visible = true;
        _lanzarButton!.Visible = true;
        _infoButton!.Visible = true;
        _spellUpButton!.Visible = true;
        _spellDownButton!.Visible = true;
        // Swap InvEqu background to spells
        if (_invEquImage != null && _invEquSpellTexture != null)
            _invEquImage.Texture = _invEquSpellTexture;
        UpdateTabVisuals();
    }

    /// <summary>
    /// VB6 CmdLanzar_Click: sends LH, sets UsingSkill, changes cursor to crosshair.
    /// </summary>
    public void OnLanzarPressed()
    {
        // VB6: dead players can't cast
        if (_state.Dead) return;

        _spellPanel!.CastSelected();
        if (_state.UsingSkill > 0)
        {
            // VB6: frmMain.MousePointer = 2 (crosshair cursor)
            Input.SetDefaultCursorShape(Input.CursorShape.Cross);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Haz click sobre el objetivo...",
                Color = "6464B4"
            });
        }
    }

    /// <summary>
    /// Toggle work macro — auto-repeats UseItem on the currently selected inventory slot.
    /// VB6: tmrTrabajo timer for fishing/mining/woodcutting/smelting auto-repeat.
    /// </summary>
    public void HandleWorkMacroToggle()
    {
        if (_state.WorkMacro.Active)
        {
            _state.WorkMacro.Stop();
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = ">>MACRO DE TRABAJO DESACTIVADO<<",
                Color = "FF0000"
            });
        }
        else
        {
            int slot = _state.SelectedInvSlot;
            if (slot < 0 || slot >= _state.MaxInventorySlots || _state.Inventory[slot].ObjIndex <= 0)
            {
                _state.ChatMessages.Enqueue(new ChatMessage
                {
                    Text = "Selecciona un objeto de trabajo primero.",
                    Color = "FF0000"
                });
                return;
            }
            byte serverSlot = (byte)(slot + 1);
            _state.WorkMacro.Start(() =>
            {
                SendPacket?.Invoke(ClientPackets.WriteUseItem(serverSlot));
            });
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $">>MACRO DE TRABAJO ACTIVADO<< ({_state.Inventory[slot].Name})",
                Color = "00FF00"
            });
        }
    }

    /// <summary>
    /// Toggle spell training macro — auto-repeats CastSpell for the selected spell slot.
    /// </summary>
    public void HandleSpellMacroToggle()
    {
        if (_state.SpellMacro.Active)
        {
            _state.SpellMacro.Stop();
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = ">>MACRO DE HECHIZOS DESACTIVADO<<",
                Color = "FF0000"
            });
        }
        else
        {
            int spellSlot = -1;
            for (int i = 0; i < 20; i++)
            {
                if (_state.Spells[i].SpellId > 0)
                {
                    spellSlot = i;
                    break;
                }
            }
            if (spellSlot < 0)
            {
                _state.ChatMessages.Enqueue(new ChatMessage
                {
                    Text = "No tienes hechizos disponibles.",
                    Color = "FF0000"
                });
                return;
            }
            byte slot = (byte)(spellSlot + 1);
            string spellName = _state.Spells[spellSlot].Name;
            _state.SpellMacro.Start(() =>
            {
                SendPacket?.Invoke(ClientPackets.WriteCastSpell(slot));
            });
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $">>MACRO DE HECHIZOS ACTIVADO<< ({spellName})",
                Color = "00FF00"
            });
        }
    }

    /// <summary>
    /// Handle inventory drag & drop that ended outside the inventory panel.
    /// Routes to trade panel, vault panel, guild bank, or ground drop based on drop position.
    /// </summary>
    public void OnInventoryDropOutside(int slot, Vector2 globalPos)
    {
        if (slot < 0 || slot >= _state.MaxInventorySlots) return;
        var item = _state.Inventory[slot];
        if (item.ObjIndex <= 0 || item.Amount <= 0) return;

        byte slot1 = (byte)(slot + 1); // server uses 1-indexed slots

        // Check if drop landed on the trade panel
        if (_state.Trading && TryTradeOffer != null && TryTradeOffer(slot, globalPos))
            return;

        // Check if drop landed on the vault panel
        if (_state.BovedaAbierta && TryVaultDeposit != null && TryVaultDeposit(slot, globalPos))
            return;

        // Check if drop landed on the guild bank panel
        if (_state.ShowGuildBank && TryGuildBankDeposit != null && TryGuildBankDeposit(slot, globalPos))
            return;

        // Not on any panel — drop on ground
        if (item.Amount > 1)
        {
            OnShowDropDialog?.Invoke(slot, item.Name);
        }
        else
        {
            SendPacket?.Invoke(ClientPackets.WriteDropItem(slot1, (short)item.Amount));
        }
    }
}
