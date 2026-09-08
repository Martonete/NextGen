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
    private Button? _enterButton;
    private Label? _selectedName;
    private Label? _noticeLabel;
    private Label? _previewHintLabel;
    private Node2D? _previewNode;
    private SubViewport? _previewViewport;

    private GameState? _state;
    private GameData? _data;
    private GrhAnimator _animator = new();

    public ItemList? CharList => _charList;
    public Button? EnterButton => _enterButton;
    public Label? NoticeLabel => _noticeLabel;

    public Action? OnEnterPressed;
    public Action? OnDisconnect;
    public Action? OnDeletePressed;
    public Action? OnCreatePressed;

    public CharSelectForm()
        : base("Seleccionar Personaje", new Vector2(620, 530), "entry")
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
        root.AddChild(EntryTheme.Header("ARGENTUM NEXTGEN  /  TUS PERSONAJES", "Elegí tu destino", "Una nueva historia te espera del otro lado."));

        // === ROW 1: CharList (left) | Preview + buttons (right) ===
        var row1 = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        row1.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(row1);

        // Left column — character list
        var leftCol = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.SizeFlagsVertical = SizeFlags.ExpandFill;
        row1.AddChild(leftCol);
        leftCol.AddChild(EntryTheme.Text("PERSONAJES DE LA CUENTA", 11, true));

        _charList = RpgTheme.CreateRpgItemList(0, 0);
        _charList.SizeFlagsVertical = SizeFlags.ExpandFill;
        _charList.AddThemeStyleboxOverride("panel", EntryTheme.Box("0b1419", "35474d"));
        _charList.AddThemeStyleboxOverride("selected", EntryTheme.Box("35453e", "c5a772", 6));
        _charList.AddThemeStyleboxOverride("selected_focus", EntryTheme.Box("35453e", "e0c797", 6));
        _charList.AddThemeFontSizeOverride("font_size", 15);
        _charList.AddThemeConstantOverride("v_separation", 18);
        _charList.ItemActivated += (long idx) => { if (_enterButton?.Disabled == false) OnEnterPressed?.Invoke(); };
        _charList.ItemSelected += (long idx) => UpdatePreview((int)idx);
        leftCol.AddChild(_charList);

        // Right column — preview + connect + delete
        var rightCol = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        rightCol.SizeFlagsVertical = SizeFlags.ExpandFill;
        RpgTheme.SetMinW(rightCol, 190);
        row1.AddChild(rightCol);

        // Character preview — SubViewport for rendering
        var previewWrapper = new Control();
        previewWrapper.CustomMinimumSize = new Vector2(190, 150);
        previewWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        previewWrapper.ClipContents = true;
        rightCol.AddChild(previewWrapper);

        var previewBg = new Panel();
        previewBg.AddThemeStyleboxOverride("panel", EntryTheme.Box("0b161c", "62543b"));
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
        previewContainer.CustomMinimumSize = new Vector2(190, 150);
        previewContainer.Stretch = true;
        previewContainer.MouseFilter = MouseFilterEnum.Ignore;
        previewWrapper.AddChild(previewContainer);
        RpgTheme.FillParent(previewContainer);

        _previewViewport = new SubViewport();
        _previewViewport.Size = new Vector2I(130, 130);
        _previewViewport.TransparentBg = true;
        _previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        previewContainer.AddChild(_previewViewport);

        _previewNode = new Node2D();
        _previewNode.Scale = new Vector2(2, 2);
        _previewNode.TextureFilter = TextureFilterEnum.Nearest;
        _previewNode.Draw += DrawCharPreview;
        _previewViewport.AddChild(_previewNode);
        _selectedName = EntryTheme.Text("Tu próximo capítulo", 14);
        _selectedName.HorizontalAlignment = HorizontalAlignment.Center;
        _selectedName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        rightCol.AddChild(_selectedName);

        // Connect button
        _enterButton = EntryTheme.Button("Entrar al mundo", true);
        _enterButton.CustomMinimumSize = new Vector2(0, 36);
        _enterButton.Pressed += () => OnEnterPressed?.Invoke();
        rightCol.AddChild(_enterButton);

        // Delete button
        var deleteBtn = EntryTheme.Button("Eliminar personaje");
        deleteBtn.AddThemeColorOverride("font_color", new Color("ce9d94"));
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

        var createBtn = EntryTheme.Button("+  Crear personaje");
        createBtn.CustomMinimumSize = new Vector2(150, 34);
        createBtn.Pressed += () => OnCreatePressed?.Invoke();
        btnRow.AddChild(createBtn);

        var exitBtn = EntryTheme.Button("Volver a mi cuenta");
        exitBtn.CustomMinimumSize = new Vector2(100, 34);
        exitBtn.Pressed += () => OnDisconnect?.Invoke();
        btnRow.AddChild(exitBtn);
    }

    private void UpdatePreview(int index)
    {
        if (_state == null || index < 0 || index >= _state.CharacterList.Count)
        {
            if (_previewHintLabel != null) _previewHintLabel.Visible = true;
            return;
        }
        if (_previewHintLabel != null) _previewHintLabel.Visible = false;
        if (_selectedName != null) _selectedName.Text = _state.CharacterList[index].Name;
        if (_previewViewport != null)
            _previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _previewNode?.QueueRedraw();
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
        ch.FovAlpha = 1f; // UI preview — always fully visible
        ch.Body = charInfo.Body;
        ch.Head = charInfo.Head;
        ch.Heading = 3;
        ch.Dead = charInfo.Dead;
        // Only show equipment if character has a head (boats don't)
        if (charInfo.Head > 0)
        {
            ch.WeaponAnim = charInfo.Weapon;
            ch.ShieldAnim = charInfo.Shield;
            ch.CascoAnim = charInfo.Helmet;
        }
        ch.Name = ""; // No name in preview (already shown in list)

        // Center character in 130x130 viewport.
        // Use the tile-center approach: in-game characters are centered at pos.X + 16.
        // Then measure body height + head offset for vertical centering.
        // Heading 3 = South: shield draws right, weapon draws left.
        // Shift X slightly right (+5) to account for shield visual weight.
        float centerX = (_previewViewport?.Size.X ?? 130) / 4f;
        float centerY = (_previewViewport?.Size.Y ?? 130) / 4f;
        float posX = centerX - 16f;
        float posY = centerY;
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
                    posY = centerY - (headOff + bodyDrawY + bodyH) / 2f;
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
