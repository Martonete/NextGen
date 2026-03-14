using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Manages modal dialogs: window mode, escape menu, mensaje dialog, drop dialog.
/// Now uses RpgTheme for consistent RPG-styled UI.
/// </summary>
public class DialogManager
{
    // Window mode startup dialog
    private PanelContainer? _windowModeDialog;

    // Saved window mode before minimize
    private DisplayServer.WindowMode _preMiniMode;
    private bool _restoringFullscreen;

    // Escape menu
    private Control? _escapeMenu;

    // Message dialog (VB6: Mensaje form)
    private ColorRect? _mensajeOverlay; // full-screen modal backdrop
    private Control? _mensajeDialog;
    private Label? _mensajeLabel;

    // Drop quantity dialog (VB6: frmCantidad)
    private Control? _dropDialog;
    private Label? _dropDialogLabel;
    private LineEdit? _dropDialogInput;

    private readonly GameState _state;

    // Callbacks
    public Action? OnLogout;
    public Action? OnQuit;
    public Action? OnOptions;
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
        // Use a simple PanelContainer with RpgTheme styling
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

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingLg);
        _windowModeDialog.AddChild(vbox);

        var label = RpgTheme.CreateTitleLabel("¿Deseas ejecutar en modo ventana?", 14);
        vbox.AddChild(label);

        var btnBox = RpgTheme.CreateRow(RpgTheme.SpacingXl);
        btnBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnBox);

        var yesBtn = RpgTheme.CreateRpgButton("Sí (Ventana)", false, 13);
        yesBtn.CustomMinimumSize = new Vector2(140, 36);
        yesBtn.Pressed += () => OnWindowModeChosen?.Invoke(true);
        btnBox.AddChild(yesBtn);

        var noBtn = RpgTheme.CreateRpgButton("No (Completa)", false, 13);
        noBtn.CustomMinimumSize = new Vector2(140, 36);
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
    }

    public void ShowWindowModeDialog()
    {
        if (_windowModeDialog != null)
            _windowModeDialog.Visible = true;
    }

    public void HideWindowModeDialog()
    {
        if (_windowModeDialog != null)
            _windowModeDialog.Visible = false;
    }

    // === Escape Menu ===

    public void CreateEscapeMenu(Node parent)
    {
        _escapeMenu = new Control();
        _escapeMenu.CustomMinimumSize = new Vector2(280, 220);
        _escapeMenu.Size = new Vector2(280, 220);
        _escapeMenu.Visible = false;
        _escapeMenu.ZIndex = 200;
        _escapeMenu.MouseFilter = Control.MouseFilterEnum.Stop;

        // Background frame
        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.10f, 0.09f, 0.08f, 0.95f);
        solidBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _escapeMenu.AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        var frame = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        _escapeMenu.AddChild(frame);
        RpgTheme.FillParent(frame);

        // Title
        var titleBg = RpgTheme.CreateNinePatch("dialoge_frame.png", new Vector4(20, 10, 20, 10));
        _escapeMenu.AddChild(titleBg);
        titleBg.AnchorLeft = 0f; titleBg.AnchorRight = 1f;
        titleBg.AnchorTop = 0f;  titleBg.AnchorBottom = 0f;
        titleBg.OffsetLeft = 8;  titleBg.OffsetTop = 4;
        titleBg.OffsetRight = -8; titleBg.OffsetBottom = 50;

        var titleLabel = RpgTheme.CreateTitleLabel("Menú", 18);
        titleBg.AddChild(titleLabel);
        RpgTheme.FillParent(titleLabel);

        // Close X button (top-right)
        var closeBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(28, 28));
        closeBtn.Pressed += HideEscapeMenu;
        _escapeMenu.AddChild(closeBtn);
        closeBtn.AnchorLeft = 1.0f; closeBtn.AnchorRight = 1.0f;
        closeBtn.AnchorTop = 0.0f;  closeBtn.AnchorBottom = 0.0f;
        closeBtn.OffsetLeft = -38;   closeBtn.OffsetTop = 13;
        closeBtn.OffsetRight = -8;   closeBtn.OffsetBottom = 36;

        // Content
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", RpgTheme.FormMarginTop);
        margin.AddThemeConstantOverride("margin_left", RpgTheme.FormMarginLeft);
        margin.AddThemeConstantOverride("margin_right", RpgTheme.FormMarginRight);
        margin.AddThemeConstantOverride("margin_bottom", RpgTheme.FormMarginBottom);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        _escapeMenu.AddChild(margin);
        RpgTheme.FillParent(margin);

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        margin.AddChild(vbox);

        var optionsBtn = RpgTheme.CreateRpgButton("Ajustes", true, 14);
        optionsBtn.CustomMinimumSize = new Vector2(0, 38);
        optionsBtn.Pressed += () =>
        {
            HideEscapeMenu();
            OnOptions?.Invoke();
        };
        vbox.AddChild(optionsBtn);

        var logoutBtn = RpgTheme.CreateRpgButton("Cerrar Sesión", true, 14);
        logoutBtn.CustomMinimumSize = new Vector2(0, 38);
        logoutBtn.Pressed += () =>
        {
            HideEscapeMenu();
            OnLogout?.Invoke();
        };
        vbox.AddChild(logoutBtn);

        var quitBtn = RpgTheme.CreateRpgButton("Salir del Juego", true, 14);
        quitBtn.CustomMinimumSize = new Vector2(0, 38);
        quitBtn.Pressed += () => OnQuit?.Invoke();
        vbox.AddChild(quitBtn);

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
        // Full-screen dark overlay — blocks ALL input behind the dialog
        _mensajeOverlay = new ColorRect();
        _mensajeOverlay.Color = new Color(0f, 0f, 0f, 0.55f);
        // Explicit size (FullRect anchors don't work in CanvasLayer)
        _mensajeOverlay.Position = Vector2.Zero;
        _mensajeOverlay.Size = new Vector2(800, 600);
        _mensajeOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
        _mensajeOverlay.Visible = false;
        _mensajeOverlay.ZIndex = 200;
        parent.AddChild(_mensajeOverlay);

        // Dialog panel — CHILD of overlay so it renders on top and receives input
        _mensajeDialog = new Control();
        _mensajeDialog.CustomMinimumSize = new Vector2(380, 200);
        _mensajeDialog.Size = new Vector2(380, 200);
        _mensajeDialog.MouseFilter = Control.MouseFilterEnum.Stop;
        _mensajeOverlay.AddChild(_mensajeDialog);

        // V2 background
        var bg = new TextureRect();
        bg.Texture = RpgTheme.GetTex("big_bar.png");
        bg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bg.StretchMode = TextureRect.StretchModeEnum.Scale;
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mensajeDialog.AddChild(bg);
        RpgTheme.FillParent(bg);

        // Content with V2 margins
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_left", 36);
        margin.AddThemeConstantOverride("margin_right", 36);
        margin.AddThemeConstantOverride("margin_bottom", 38);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mensajeDialog.AddChild(margin);
        RpgTheme.FillParent(margin);

        var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        margin.AddChild(root);

        // Message text — expands
        _mensajeLabel = RpgTheme.CreateInfoLabel("", 13);
        _mensajeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mensajeLabel.VerticalAlignment = VerticalAlignment.Center;
        _mensajeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mensajeLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.AddChild(_mensajeLabel);

        // Footer with Aceptar button
        var footer = RpgTheme.CreateFooterRow();
        root.AddChild(footer);
        var btnRow = footer.GetMeta("row").As<HBoxContainer>();

        var acceptBtn = RpgTheme.CreateRpgButton("Aceptar", true, 14);
        acceptBtn.CustomMinimumSize = new Vector2(130, 36);
        acceptBtn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        acceptBtn.Pressed += OnMensajeAccept;
        btnRow.AddChild(acceptBtn);
    }

    public void ShowMensaje(string text, Vector2 screenSize)
    {
        if (_mensajeDialog == null || _mensajeLabel == null || _mensajeOverlay == null) return;

        _mensajeLabel.Text = text;

        if (text.Length > 120)
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 10);
        else if (text.Length > 75)
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 11);
        else
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 13);

        // Show overlay (blocks all input behind) — dialog is child, auto-visible
        _mensajeOverlay.Visible = true;

        // Center dialog in screen
        _mensajeDialog.Position = new Vector2(
            (screenSize.X - _mensajeDialog.Size.X) / 2,
            (screenSize.Y - _mensajeDialog.Size.Y) / 2
        );
    }

    public void OnMensajeAccept()
    {
        if (_mensajeOverlay != null)
            _mensajeOverlay.Visible = false;
    }

    public bool IsMensajeVisible => _mensajeOverlay != null && _mensajeOverlay.Visible;

    // === Drop Dialog ===

    public void CreateDropDialog(Control parent)
    {
        _dropDialog = new Control();
        _dropDialog.CustomMinimumSize = new Vector2(220, 130);
        _dropDialog.Size = new Vector2(220, 130);
        _dropDialog.Position = new Vector2(170, 290);
        _dropDialog.Visible = false;
        _dropDialog.MouseFilter = Control.MouseFilterEnum.Stop;

        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.10f, 0.09f, 0.08f, 0.95f);
        solidBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dropDialog.AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        var frame = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        _dropDialog.AddChild(frame);
        RpgTheme.FillParent(frame);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", RpgTheme.PanelMarginTop);
        margin.AddThemeConstantOverride("margin_left", RpgTheme.PanelMarginLeft);
        margin.AddThemeConstantOverride("margin_right", RpgTheme.PanelMarginRight);
        margin.AddThemeConstantOverride("margin_bottom", RpgTheme.PanelMarginBottom);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dropDialog.AddChild(margin);
        RpgTheme.FillParent(margin);

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        margin.AddChild(vbox);

        _dropDialogLabel = RpgTheme.CreateInfoLabel("Cantidad a tirar:", 13);
        _dropDialogLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_dropDialogLabel);

        _dropDialogInput = RpgTheme.CreateRpgInput("1", 160);
        _dropDialogInput.Text = "1";
        _dropDialogInput.Alignment = HorizontalAlignment.Center;
        _dropDialogInput.TextSubmitted += (_) => OnDropDialogOk();
        vbox.AddChild(_dropDialogInput);

        var hbox = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        var okBtn = RpgTheme.CreateRpgButton("Tirar", false, 12);
        okBtn.CustomMinimumSize = new Vector2(60, 28);
        okBtn.Pressed += OnDropDialogOk;
        hbox.AddChild(okBtn);

        var allBtn = RpgTheme.CreateRpgButton("Todo", false, 12);
        allBtn.CustomMinimumSize = new Vector2(60, 28);
        allBtn.Pressed += OnDropDialogAll;
        hbox.AddChild(allBtn);

        var cancelBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(24, 24));
        cancelBtn.Pressed += CloseDropDialog;
        hbox.AddChild(cancelBtn);

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
