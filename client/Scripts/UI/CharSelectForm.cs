using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// Character selection form with preview.
/// Row 1: [CharList] | [Preview + Conectar + Borrar]
/// Row 2: [Notice]
/// Row 3: [Crear Personaje] [Salir]
/// </summary>
public partial class CharSelectForm : RpgBaseForm
{
    private ItemList? _charList;
    private TextureButton? _enterButton;
    private Label? _noticeLabel;
    private Label? _previewHintLabel;
    private Node2D? _previewNode;
    private SubViewport? _previewViewport;

    private GameState? _state;
    private GameData? _data;
    private GrhAnimator _animator = new();

    public ItemList? CharList => _charList;
    public TextureButton? EnterButton => _enterButton;
    public Label? NoticeLabel => _noticeLabel;

    public Action? OnEnterPressed;
    public Action? OnDisconnect;
    public Action? OnDeletePressed;
    public Action? OnCreatePressed;

    public CharSelectForm()
        : base("Seleccionar Personaje", new Vector2(480, 400), "v2")
    {
        Draggable = false;
        ShowCloseButton = false;
    }

    public void Init(GameState state, GameData data)
    {
        _state = state;
        _data = data;
    }

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        ContentContainer.AddChild(root);

        // === ROW 1: CharList (left) | Preview + buttons (right) ===
        var row1 = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        row1.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(row1);

        // Left column — character list
        var leftCol = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.SizeFlagsVertical = SizeFlags.ExpandFill;
        row1.AddChild(leftCol);

        _charList = RpgTheme.CreateRpgItemList(0, 0);
        _charList.SizeFlagsVertical = SizeFlags.ExpandFill;
        _charList.ItemActivated += (long idx) => OnEnterPressed?.Invoke();
        _charList.ItemSelected += (long idx) => UpdatePreview((int)idx);
        leftCol.AddChild(_charList);

        // Right column — preview + connect + delete
        var rightCol = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        rightCol.SizeFlagsVertical = SizeFlags.ExpandFill;
        RpgTheme.SetMinW(rightCol, 140);
        row1.AddChild(rightCol);

        // Character preview — SubViewport for rendering
        var previewWrapper = new Control();
        previewWrapper.CustomMinimumSize = new Vector2(130, 130);
        previewWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        previewWrapper.ClipContents = true;
        rightCol.AddChild(previewWrapper);

        var previewBg = new ColorRect();
        previewBg.Color = new Color(0.04f, 0.04f, 0.04f, 0.8f);
        previewBg.MouseFilter = MouseFilterEnum.Ignore;
        previewWrapper.AddChild(previewBg);
        RpgTheme.FillParent(previewBg);

        _previewHintLabel = RpgTheme.CreateInfoLabel("Selecciona un\npersonaje", 9);
        _previewHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _previewHintLabel.VerticalAlignment = VerticalAlignment.Center;
        _previewHintLabel.MouseFilter = MouseFilterEnum.Ignore;
        previewWrapper.AddChild(_previewHintLabel);
        RpgTheme.FillParent(_previewHintLabel);

        var previewContainer = new SubViewportContainer();
        previewContainer.CustomMinimumSize = new Vector2(130, 130);
        previewContainer.Stretch = true;
        previewContainer.MouseFilter = MouseFilterEnum.Ignore;
        previewWrapper.AddChild(previewContainer);
        RpgTheme.FillParent(previewContainer);

        _previewViewport = new SubViewport();
        _previewViewport.Size = new Vector2I(130, 130);
        _previewViewport.TransparentBg = true;
        _previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        previewContainer.AddChild(_previewViewport);

        _previewNode = new Node2D();
        _previewNode.Draw += DrawCharPreview;
        _previewViewport.AddChild(_previewNode);

        // Connect button
        _enterButton = RpgTheme.CreateRpgButton("Conectar", true, 14);
        _enterButton.CustomMinimumSize = new Vector2(0, 36);
        _enterButton.Pressed += () => OnEnterPressed?.Invoke();
        rightCol.AddChild(_enterButton);

        // Delete button
        var deleteBtn = RpgTheme.CreateRpgButton("Borrar", false, 12);
        deleteBtn.CustomMinimumSize = new Vector2(0, 30);
        deleteBtn.Pressed += () => OnDeletePressed?.Invoke();
        rightCol.AddChild(deleteBtn);

        // === ROW 2: Notice label ===
        _noticeLabel = RpgTheme.CreateInfoLabel("", 11);
        _noticeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _noticeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _noticeLabel.CustomMinimumSize = new Vector2(0, 20);
        root.AddChild(_noticeLabel);

        // === ROW 3: Create + Exit buttons ===
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(btnRow);

        var createBtn = RpgTheme.CreateRpgButton("Crear Personaje", false, 13);
        createBtn.CustomMinimumSize = new Vector2(150, 34);
        createBtn.Pressed += () => OnCreatePressed?.Invoke();
        btnRow.AddChild(createBtn);

        var exitBtn = RpgTheme.CreateRpgButton("Salir", false, 13);
        exitBtn.CustomMinimumSize = new Vector2(100, 34);
        exitBtn.Pressed += () => OnDisconnect?.Invoke();
        btnRow.AddChild(exitBtn);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // Keep preview redrawing (animator needs continuous updates for static frame resolve)
        _previewNode?.QueueRedraw();
    }

    private void UpdatePreview(int index)
    {
        if (_state == null || index < 0 || index >= _state.CharacterList.Count)
        {
            if (_previewHintLabel != null) _previewHintLabel.Visible = true;
            return;
        }
        if (_previewHintLabel != null) _previewHintLabel.Visible = false;
    }

    private void DrawCharPreview()
    {
        if (_state == null || _data == null || _previewNode == null) return;
        if (_charList == null || !_charList.IsAnythingSelected()) return;

        int[] sel = _charList.GetSelectedItems();
        if (sel.Length == 0 || sel[0] >= _state.CharacterList.Count) return;

        var charInfo = _state.CharacterList[sel[0]];
        if (charInfo.Body <= 0)
        {
            GD.Print($"[CHARSEL] Body<=0 for slot {sel[0]}, body={charInfo.Body}");
            return;
        }

        var ch = new Character();
        ch.Body = charInfo.Body;
        ch.Head = charInfo.Head;
        ch.WeaponAnim = charInfo.Weapon;
        ch.ShieldAnim = charInfo.Shield;
        ch.CascoAnim = charInfo.Helmet;
        ch.Heading = 3;
        ch.Dead = charInfo.Dead;
        ch.Name = ""; // No name in preview (already shown in list)
        ch._debugLogged = true;
        ch._equipDebugLogged = true;

        // Center character in 130x130 viewport.
        // Use the tile-center approach: in-game characters are centered at pos.X + 16.
        // Then measure body height + head offset for vertical centering.
        // Heading 3 = South: shield draws right, weapon draws left.
        // Shift X slightly right (+5) to account for shield visual weight.
        float posX = 49f; // tile center at 65 (49+16=65)
        float posY = 65f;
        if (charInfo.Body > 0 && charInfo.Body < _data.Bodies.Length)
        {
            var body = _data.Bodies[charInfo.Body];
            int walkGrh = body.Walk[3];
            if (walkGrh > 0)
            {
                var res = _data.ResolveGrh(walkGrh, 0);
                if (res != null)
                {
                    float bodyH = res.PixelHeight;
                    float headOff = body.HeadOffsetY;
                    float bodyDrawY = 0f;
                    if (res.TileHeight != 1f && res.TileHeight > 0)
                        bodyDrawY = -((int)(res.TileHeight * 32f) - 32f);

                    // Vertical: center between head top and body bottom
                    posY = 65f - (headOff + bodyDrawY + bodyH) / 2f;
                }
            }
        }
        // Equipment offset: shield extends right on heading 3 (south)
        bool hasShield = ch.ShieldAnim > 0;
        bool hasWeapon = ch.WeaponAnim > 0;
        if (hasShield) posX -= 5f; // shift left so shield+body visual center hits 65
        CharRenderer.DrawCharacter(_previewNode, ch, new Vector2(posX, posY), _data, _animator);
    }
}
