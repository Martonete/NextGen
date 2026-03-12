using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Manages modal dialogs: window mode, escape menu, mensaje dialog, drop dialog.
/// Extracted from Main.cs.
/// </summary>
public class DialogManager
{
    // Window mode startup dialog
    private PanelContainer? _windowModeDialog;

    // Saved window mode before minimize
    private DisplayServer.WindowMode _preMiniMode;
    private bool _restoringFullscreen;

    // Escape menu
    private PanelContainer? _escapeMenu;

    // Message dialog (VB6: Mensaje form)
    private ColorRect? _mensajeOverlay; // full-screen modal backdrop
    private PanelContainer? _mensajeDialog;
    private Label? _mensajeLabel;

    // Drop quantity dialog (VB6: frmCantidad)
    private PanelContainer? _dropDialog;
    private Label? _dropDialogLabel;
    private LineEdit? _dropDialogInput;
    private Button? _dropDialogOk;
    private Button? _dropDialogAll;
    private Button? _dropDialogCancel;

    private readonly GameState _state;

    // Callbacks
    public Action? OnLogout;
    public Action? OnQuit;
    public Action<bool>? OnWindowModeChosen;

    /// <summary>Access for centering from Main's CallDeferred.</summary>
    public PanelContainer? WindowModeDialog => _windowModeDialog;

    public DialogManager(GameState state)
    {
        _state = state;
    }

    // === Window Mode Dialog ===

    public void CreateWindowModeDialog(Node parent)
    {
        _windowModeDialog = new PanelContainer();
        _windowModeDialog.Size = new Vector2(360, 140);
        _windowModeDialog.Visible = false;
        _windowModeDialog.ZIndex = 200;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        bg.BorderColor = new Color(0.55f, 0.45f, 0.3f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(14);
        bg.SetCornerRadiusAll(4);
        _windowModeDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        _windowModeDialog.AddChild(vbox);

        var label = new Label();
        label.Text = "¿Deseas ejecutar en modo ventana?";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(label);

        var btnBox = new HBoxContainer();
        btnBox.AddThemeConstantOverride("separation", 16);
        btnBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnBox);

        var yesBtn = new Button();
        yesBtn.Text = "Sí (Ventana)";
        yesBtn.CustomMinimumSize = new Vector2(130, 34);
        yesBtn.AddThemeFontSizeOverride("font_size", 12);
        yesBtn.Pressed += () => OnWindowModeChosen?.Invoke(true);
        btnBox.AddChild(yesBtn);

        var noBtn = new Button();
        noBtn.Text = "No (Pantalla Completa)";
        noBtn.CustomMinimumSize = new Vector2(160, 34);
        noBtn.AddThemeFontSizeOverride("font_size", 12);
        noBtn.Pressed += () => OnWindowModeChosen?.Invoke(false);
        btnBox.AddChild(noBtn);

        parent.AddChild(_windowModeDialog);
    }

    public void CenterWindowModeDialog(Vector2 screenSize)
    {
        if (_windowModeDialog == null) return;
        _windowModeDialog.Position = new Vector2(
            (screenSize.X - _windowModeDialog.Size.X) / 2,
            (screenSize.Y - _windowModeDialog.Size.Y) / 2
        );
        GD.Print($"[DIALOG] Window mode dialog centered at {_windowModeDialog.Position}, screen={screenSize}");
    }

    public void ShowWindowModeDialog()
    {
        if (_windowModeDialog != null)
        {
            _windowModeDialog.Visible = true;
            GD.Print("[DIALOG] Window mode dialog shown");
        }
    }

    public void HideWindowModeDialog()
    {
        if (_windowModeDialog != null)
            _windowModeDialog.Visible = false;
    }

    // === Escape Menu ===

    public void CreateEscapeMenu(Node parent)
    {
        _escapeMenu = new PanelContainer();
        _escapeMenu.Size = new Vector2(260, 200);
        _escapeMenu.Visible = false;
        _escapeMenu.ZIndex = 200;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        bg.BorderColor = new Color(0.55f, 0.45f, 0.3f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(14);
        bg.SetCornerRadiusAll(4);
        _escapeMenu.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        _escapeMenu.AddChild(vbox);

        var title = new Label();
        title.Text = "Menú";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Colors.White);
        title.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(title);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        var logoutBtn = new Button();
        logoutBtn.Text = "Cerrar Sesión";
        logoutBtn.CustomMinimumSize = new Vector2(0, 34);
        logoutBtn.AddThemeFontSizeOverride("font_size", 12);
        logoutBtn.Pressed += () =>
        {
            HideEscapeMenu();
            OnLogout?.Invoke();
        };
        vbox.AddChild(logoutBtn);

        var quitBtn = new Button();
        quitBtn.Text = "Salir del Juego";
        quitBtn.CustomMinimumSize = new Vector2(0, 34);
        quitBtn.AddThemeFontSizeOverride("font_size", 12);
        quitBtn.Pressed += () => OnQuit?.Invoke();
        vbox.AddChild(quitBtn);

        var backBtn = new Button();
        backBtn.Text = "Volver";
        backBtn.CustomMinimumSize = new Vector2(0, 34);
        backBtn.AddThemeFontSizeOverride("font_size", 12);
        backBtn.Pressed += HideEscapeMenu;
        vbox.AddChild(backBtn);

        parent.AddChild(_escapeMenu);
    }

    public void ShowEscapeMenu(Vector2 screenSize)
    {
        if (_escapeMenu == null) return;
        _escapeMenu.Position = new Vector2(
            (screenSize.X - _escapeMenu.Size.X) / 2,
            (screenSize.Y - _escapeMenu.Size.Y) / 2
        );
        _escapeMenu.Visible = true;
        _state.EscapeMenuOpen = true;
    }

    public void HideEscapeMenu()
    {
        if (_escapeMenu != null)
            _escapeMenu.Visible = false;
        _state.EscapeMenuOpen = false;
    }

    // === Minimize / Fullscreen Restore ===

    public void OnMinimizePressed()
    {
        _preMiniMode = DisplayServer.WindowGetMode();
        _restoringFullscreen = false;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
    }

    /// <summary>Check if fullscreen restore is needed on window focus.</summary>
    public bool ShouldRestoreFullscreen()
    {
        return _preMiniMode == DisplayServer.WindowMode.Fullscreen
            && DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen
            && !_restoringFullscreen;
    }

    public void BeginRestoreFullscreen()
    {
        _restoringFullscreen = true;
    }

    /// <summary>Callback for fullscreen restore (wired by Main).</summary>
    public Action? OnRestoreFullscreen;

    public void RestoreFullscreen(GameConfig cfg)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        OnRestoreFullscreen?.Invoke();
        tree.CreateTimer(0.5).Timeout += () => _restoringFullscreen = false;
    }

    // === Mensaje Dialog ===

    public void CreateMensajeDialog(Node parent)
    {
        // Full-screen dark overlay — blocks all input behind the dialog
        _mensajeOverlay = new ColorRect();
        _mensajeOverlay.Color = new Color(0f, 0f, 0f, 0.55f);
        _mensajeOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mensajeOverlay.MouseFilter = Control.MouseFilterEnum.Stop; // block clicks behind
        _mensajeOverlay.Visible = false;
        _mensajeOverlay.ZIndex = 199;
        parent.AddChild(_mensajeOverlay);

        _mensajeDialog = new PanelContainer();
        _mensajeDialog.Visible = false;
        _mensajeDialog.ZIndex = 200;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        bg.BorderColor = new Color(0.55f, 0.45f, 0.3f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(14);
        bg.SetCornerRadiusAll(4);
        _mensajeDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        _mensajeDialog.AddChild(vbox);

        _mensajeLabel = new Label();
        _mensajeLabel.Text = "";
        _mensajeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mensajeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mensajeLabel.CustomMinimumSize = new Vector2(300, 0);
        _mensajeLabel.AddThemeColorOverride("font_color", Colors.White);
        _mensajeLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_mensajeLabel);

        var acceptBtn = new Button();
        acceptBtn.Text = "Aceptar";
        acceptBtn.CustomMinimumSize = new Vector2(100, 30);
        acceptBtn.AddThemeFontSizeOverride("font_size", 12);
        acceptBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        acceptBtn.Pressed += OnMensajeAccept;
        vbox.AddChild(acceptBtn);

        parent.AddChild(_mensajeDialog);
    }

    public void ShowMensaje(string text, Vector2 screenSize)
    {
        if (_mensajeDialog == null || _mensajeLabel == null) return;

        _mensajeLabel.Text = text;

        if (text.Length > 120)
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 10);
        else if (text.Length > 75)
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 11);
        else
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 12);

        // Show the dark overlay first
        if (_mensajeOverlay != null)
            _mensajeOverlay.Visible = true;

        // Reset and let PanelContainer auto-size from content, then center
        _mensajeDialog.ResetSize();
        _mensajeDialog.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        // Force a reasonable width so autowrap works, height auto-fits
        _mensajeDialog.CustomMinimumSize = new Vector2(340, 0);
        _mensajeDialog.Visible = true;

        // Defer centering so Godot can compute the final size after layout
        _mensajeDialog.CallDeferred("set_position", new Vector2(
            (screenSize.X - 340) / 2,
            (screenSize.Y - 140) / 2
        ));
    }

    public void OnMensajeAccept()
    {
        if (_mensajeDialog != null)
            _mensajeDialog.Visible = false;
        if (_mensajeOverlay != null)
            _mensajeOverlay.Visible = false;
    }

    public bool IsMensajeVisible => _mensajeDialog != null && _mensajeDialog.Visible;

    // === Drop Dialog ===

    public void CreateDropDialog(Control parent)
    {
        _dropDialog = new PanelContainer();
        _dropDialog.Size = new Vector2(200, 110);
        _dropDialog.Position = new Vector2(180, 297);
        _dropDialog.Visible = false;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.95f);
        bg.BorderColor = new Color(0.4f, 0.4f, 0.6f);
        bg.SetBorderWidthAll(1);
        bg.SetContentMarginAll(8);
        _dropDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _dropDialog.AddChild(vbox);

        _dropDialogLabel = new Label();
        _dropDialogLabel.Text = "Cantidad a tirar:";
        _dropDialogLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _dropDialogLabel.AddThemeColorOverride("font_color", Colors.White);
        _dropDialogLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_dropDialogLabel);

        _dropDialogInput = new LineEdit();
        _dropDialogInput.Text = "1";
        _dropDialogInput.Alignment = HorizontalAlignment.Center;
        _dropDialogInput.FocusMode = Control.FocusModeEnum.Click;
        _dropDialogInput.AddThemeFontSizeOverride("font_size", 12);
        _dropDialogInput.TextSubmitted += (_) => OnDropDialogOk();
        vbox.AddChild(_dropDialogInput);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        _dropDialogOk = new Button();
        _dropDialogOk.Text = "Tirar";
        _dropDialogOk.CustomMinimumSize = new Vector2(55, 24);
        _dropDialogOk.AddThemeFontSizeOverride("font_size", 11);
        _dropDialogOk.Pressed += OnDropDialogOk;
        hbox.AddChild(_dropDialogOk);

        _dropDialogAll = new Button();
        _dropDialogAll.Text = "Todo";
        _dropDialogAll.CustomMinimumSize = new Vector2(55, 24);
        _dropDialogAll.AddThemeFontSizeOverride("font_size", 11);
        _dropDialogAll.Pressed += OnDropDialogAll;
        hbox.AddChild(_dropDialogAll);

        _dropDialogCancel = new Button();
        _dropDialogCancel.Text = "X";
        _dropDialogCancel.CustomMinimumSize = new Vector2(30, 24);
        _dropDialogCancel.AddThemeFontSizeOverride("font_size", 11);
        _dropDialogCancel.Pressed += CloseDropDialog;
        hbox.AddChild(_dropDialogCancel);

        parent.AddChild(_dropDialog);
    }

    /// <summary>Callback for drop: (slot, quantity).</summary>
    public Action<int, int>? OnDropItem;

    private void OnDropDialogOk()
    {
        if (_dropDialogInput == null) return;
        int qty = 0;
        int.TryParse(_dropDialogInput.Text, out qty);
        if (qty > 0)
        {
            OnDropItem?.Invoke(_state.DropDialogSlot, qty);
        }
        CloseDropDialog();
    }

    private void OnDropDialogAll()
    {
        int slot = _state.DropDialogSlot;
        if (slot >= 0 && slot < 25)
        {
            int qty = _state.Inventory[slot].Amount;
            OnDropItem?.Invoke(slot, qty);
        }
        CloseDropDialog();
    }

    public void CloseDropDialog()
    {
        _state.DropDialogOpen = false;
        if (_dropDialog != null)
            _dropDialog.Visible = false;
    }

    /// <summary>Show the drop dialog for a specific item slot.</summary>
    public void ShowDropDialog(int slot, string itemName)
    {
        _state.DropDialogSlot = slot;
        _state.DropDialogOpen = true;
        if (_dropDialog != null)
        {
            _dropDialog.Visible = true;
            _dropDialogLabel!.Text = $"Tirar {itemName}:";
            _dropDialogInput!.Text = "1";
            _dropDialogInput.GrabFocus();
            _dropDialogInput.SelectAll();
        }
    }

    /// <summary>Show the drop dialog if state says it should be open but isn't visible.</summary>
    public void UpdateDropDialogVisibility()
    {
        if (_state.DropDialogOpen && _dropDialog != null && !_dropDialog.Visible)
        {
            int slot = _state.DropDialogSlot;
            string itemName = (slot >= 0 && slot < 25) ? _state.Inventory[slot].Name : "item";
            _dropDialogLabel!.Text = $"Tirar: {itemName}";
            _dropDialogInput!.Text = "1";
            _dropDialog.Visible = true;
            _dropDialogInput.GrabFocus();
            _dropDialogInput.SelectAll();
        }
    }
}
