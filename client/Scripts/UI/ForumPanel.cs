using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Forum panel (VB6: frmForo) — 3-board in-game forum.
/// Boards: General (type 0/1), Armada Real (type 4/5), Legion Oscura (type 2/3).
/// Server sends AddForumMsg packets (accumulated in GameState.ForumPosts),
/// then ShowForumForm to trigger display.
/// </summary>
public partial class ForumPanel : Control
{
    private const int PanelW = 520;
    private const int PanelH = 500;

    // Forum type constants (match server forum_msg_type)
    private const byte TYPE_GENERAL = 0;
    private const byte TYPE_GENERAL_STICKY = 1;
    private const byte TYPE_CAOS = 2;
    private const byte TYPE_CAOS_STICKY = 3;
    private const byte TYPE_REAL = 4;
    private const byte TYPE_REAL_STICKY = 5;

    // Visibility bitflags (match server forum_visibility)
    private const byte VIS_GENERAL = 1;
    private const byte VIS_CAOS = 2;
    private const byte VIS_REAL = 4;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Current tab index: 0=General, 1=Armada Real, 2=Legion Oscura
    private int _currentTab;

    // UI controls
    private Label? _titleLabel;
    private Button? _closeBtn;
    private HBoxContainer? _tabBar;
    private Button? _tabGeneral;
    private Button? _tabReal;
    private Button? _tabCaos;
    private ScrollContainer? _postListScroll;
    private VBoxContainer? _postListBox;
    private Panel? _postDetailPanel;
    private Label? _postDetailTitle;
    private Label? _postDetailAuthor;
    private RichTextLabel? _postDetailBody;
    private Button? _backBtn;
    private Button? _newPostBtn;

    // New post form
    private Panel? _newPostPanel;
    private LineEdit? _newPostTitle;
    private TextEdit? _newPostBody;
    private Button? _submitBtn;
    private Button? _cancelPostBtn;
    private CheckBox? _stickyCheck;

    // Cached post lists per board (populated from GameState.ForumPosts)
    private readonly List<ForumPostEntry> _generalPosts = new();
    private readonly List<ForumPostEntry> _realPosts = new();
    private readonly List<ForumPostEntry> _caosPosts = new();

    // Currently selected post for detail view
    private ForumPostEntry? _selectedPost;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        // Background
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Title
        _titleLabel = new Label();
        _titleLabel.Text = "Foro";
        _titleLabel.Position = new Vector2(10, 4);
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_titleLabel);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Text = "X";
        _closeBtn.Position = new Vector2(PanelW - 28, 2);
        _closeBtn.Size = new Vector2(24, 24);
        _closeBtn.Pressed += OnClose;
        AddChild(_closeBtn);

        // Tab bar
        _tabBar = new HBoxContainer();
        _tabBar.Position = new Vector2(8, 28);
        _tabBar.Size = new Vector2(PanelW - 16, 28);
        AddChild(_tabBar);

        _tabGeneral = new Button { Text = "General", ToggleMode = true, ButtonPressed = true };
        _tabGeneral.CustomMinimumSize = new Vector2(100, 26);
        _tabGeneral.Pressed += () => SwitchTab(0);
        _tabBar.AddChild(_tabGeneral);

        _tabReal = new Button { Text = "Armada Real", ToggleMode = true };
        _tabReal.CustomMinimumSize = new Vector2(120, 26);
        _tabReal.Pressed += () => SwitchTab(1);
        _tabBar.AddChild(_tabReal);

        _tabCaos = new Button { Text = "Legion Oscura", ToggleMode = true };
        _tabCaos.CustomMinimumSize = new Vector2(130, 26);
        _tabCaos.Pressed += () => SwitchTab(2);
        _tabBar.AddChild(_tabCaos);

        // Post list (scrollable)
        _postListScroll = new ScrollContainer();
        _postListScroll.Position = new Vector2(8, 60);
        _postListScroll.Size = new Vector2(PanelW - 16, PanelH - 110);
        AddChild(_postListScroll);

        _postListBox = new VBoxContainer();
        _postListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _postListScroll.AddChild(_postListBox);

        // Post detail panel (hidden by default)
        _postDetailPanel = new Panel();
        _postDetailPanel.Position = new Vector2(8, 60);
        _postDetailPanel.Size = new Vector2(PanelW - 16, PanelH - 110);
        _postDetailPanel.Visible = false;
        AddChild(_postDetailPanel);

        _postDetailTitle = new Label();
        _postDetailTitle.Position = new Vector2(8, 8);
        _postDetailTitle.Size = new Vector2(PanelW - 40, 20);
        _postDetailTitle.AddThemeFontSizeOverride("font_size", 13);
        _postDetailPanel.AddChild(_postDetailTitle);

        _postDetailAuthor = new Label();
        _postDetailAuthor.Position = new Vector2(8, 30);
        _postDetailAuthor.Size = new Vector2(PanelW - 40, 18);
        _postDetailAuthor.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        _postDetailPanel.AddChild(_postDetailAuthor);

        _postDetailBody = new RichTextLabel();
        _postDetailBody.Position = new Vector2(8, 54);
        _postDetailBody.Size = new Vector2(PanelW - 40, PanelH - 200);
        _postDetailBody.BbcodeEnabled = false;
        _postDetailBody.ScrollActive = true;
        _postDetailPanel.AddChild(_postDetailBody);

        _backBtn = new Button { Text = "Volver" };
        _backBtn.Position = new Vector2(8, PanelH - 140);
        _backBtn.Size = new Vector2(80, 28);
        _backBtn.Pressed += OnBackFromDetail;
        _postDetailPanel.AddChild(_backBtn);

        // New post panel (hidden by default)
        _newPostPanel = new Panel();
        _newPostPanel.Position = new Vector2(8, 60);
        _newPostPanel.Size = new Vector2(PanelW - 16, PanelH - 110);
        _newPostPanel.Visible = false;
        AddChild(_newPostPanel);

        var titleLbl = new Label { Text = "Titulo:" };
        titleLbl.Position = new Vector2(8, 8);
        _newPostPanel.AddChild(titleLbl);

        _newPostTitle = new LineEdit();
        _newPostTitle.Position = new Vector2(8, 28);
        _newPostTitle.Size = new Vector2(PanelW - 40, 28);
        _newPostTitle.MaxLength = 100;
        _newPostTitle.PlaceholderText = "Titulo del mensaje...";
        _newPostPanel.AddChild(_newPostTitle);

        var bodyLbl = new Label { Text = "Mensaje:" };
        bodyLbl.Position = new Vector2(8, 62);
        _newPostPanel.AddChild(bodyLbl);

        _newPostBody = new TextEdit();
        _newPostBody.Position = new Vector2(8, 82);
        _newPostBody.Size = new Vector2(PanelW - 40, PanelH - 240);
        _newPostBody.PlaceholderText = "Escribe tu mensaje...";
        _newPostPanel.AddChild(_newPostBody);

        _stickyCheck = new CheckBox { Text = "Fijar (Sticky)" };
        _stickyCheck.Position = new Vector2(8, PanelH - 150);
        _stickyCheck.Visible = false; // Only visible for GMs
        _newPostPanel.AddChild(_stickyCheck);

        _submitBtn = new Button { Text = "Publicar" };
        _submitBtn.Position = new Vector2(PanelW - 220, PanelH - 148);
        _submitBtn.Size = new Vector2(90, 28);
        _submitBtn.Pressed += OnSubmitPost;
        _newPostPanel.AddChild(_submitBtn);

        _cancelPostBtn = new Button { Text = "Cancelar" };
        _cancelPostBtn.Position = new Vector2(PanelW - 120, PanelH - 148);
        _cancelPostBtn.Size = new Vector2(90, 28);
        _cancelPostBtn.Pressed += OnCancelPost;
        _newPostPanel.AddChild(_cancelPostBtn);

        // Bottom bar: Nuevo Post + Cerrar
        _newPostBtn = new Button { Text = "Nuevo Post" };
        _newPostBtn.Position = new Vector2(8, PanelH - 40);
        _newPostBtn.Size = new Vector2(100, 30);
        _newPostBtn.Pressed += OnNewPost;
        AddChild(_newPostBtn);

        var closeBottomBtn = new Button { Text = "Cerrar" };
        closeBottomBtn.Position = new Vector2(PanelW - 80, PanelH - 40);
        closeBottomBtn.Size = new Vector2(70, 30);
        closeBottomBtn.Pressed += OnClose;
        AddChild(closeBottomBtn);
    }

    /// <summary>
    /// Open the forum panel with the data accumulated in GameState.ForumPosts.
    /// Called by Main.cs when ShowForumPanel is triggered.
    /// </summary>
    public void ShowForum()
    {
        if (_state == null || _postListBox == null) return;

        // Parse accumulated posts into per-board lists
        _generalPosts.Clear();
        _realPosts.Clear();
        _caosPosts.Clear();

        foreach (var post in _state.ForumPosts)
        {
            switch (post.ForumType)
            {
                case TYPE_GENERAL:
                case TYPE_GENERAL_STICKY:
                    _generalPosts.Add(post);
                    break;
                case TYPE_CAOS:
                case TYPE_CAOS_STICKY:
                    _caosPosts.Add(post);
                    break;
                case TYPE_REAL:
                case TYPE_REAL_STICKY:
                    _realPosts.Add(post);
                    break;
            }
        }

        // Show/hide faction tabs based on visibility flags
        byte vis = _state.ForumVisibility;
        _tabReal!.Visible = (vis & VIS_REAL) != 0;
        _tabCaos!.Visible = (vis & VIS_CAOS) != 0;

        // Show sticky checkbox for GMs
        _stickyCheck!.Visible = _state.ForumCanMakeSticky > 0;

        // Reset to list view, General tab
        _postDetailPanel!.Visible = false;
        _newPostPanel!.Visible = false;
        _postListScroll!.Visible = true;
        _selectedPost = null;

        SwitchTab(0);
        Visible = true;
    }

    private void SwitchTab(int tab)
    {
        _currentTab = tab;

        // Update toggle state
        _tabGeneral!.ButtonPressed = (tab == 0);
        _tabReal!.ButtonPressed = (tab == 1);
        _tabCaos!.ButtonPressed = (tab == 2);

        // Show list view
        _postDetailPanel!.Visible = false;
        _newPostPanel!.Visible = false;
        _postListScroll!.Visible = true;

        BuildPostList();
    }

    private void BuildPostList()
    {
        if (_postListBox == null) return;

        // Clear existing entries
        foreach (var child in _postListBox.GetChildren())
            child.QueueFree();

        var posts = _currentTab switch
        {
            1 => _realPosts,
            2 => _caosPosts,
            _ => _generalPosts
        };

        // Stickies first (sorted to top)
        var sorted = new List<ForumPostEntry>(posts);
        sorted.Sort((a, b) =>
        {
            if (a.IsSticky != b.IsSticky) return a.IsSticky ? -1 : 1;
            return 0; // preserve server order within same category
        });

        if (sorted.Count == 0)
        {
            var emptyLbl = new Label { Text = "No hay mensajes en este foro." };
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _postListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var post in sorted)
        {
            var row = new Button();
            row.CustomMinimumSize = new Vector2(PanelW - 30, 36);
            row.Alignment = HorizontalAlignment.Left;
            row.ClipText = true;

            string prefix = post.IsSticky ? "[FIJO] " : "";
            row.Text = $"{prefix}{post.Title}  —  {post.Author}";

            if (post.IsSticky)
            {
                row.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
            }

            var captured = post;
            row.Pressed += () => ShowPostDetail(captured);
            _postListBox.AddChild(row);
        }
    }

    private void ShowPostDetail(ForumPostEntry post)
    {
        _selectedPost = post;

        _postListScroll!.Visible = false;
        _newPostPanel!.Visible = false;
        _postDetailPanel!.Visible = true;

        string prefix = post.IsSticky ? "[FIJO] " : "";
        _postDetailTitle!.Text = $"{prefix}{post.Title}";
        _postDetailAuthor!.Text = $"Por: {post.Author}";
        _postDetailBody!.Text = post.Body;
    }

    private void OnBackFromDetail()
    {
        _postDetailPanel!.Visible = false;
        _postListScroll!.Visible = true;
        _selectedPost = null;
    }

    private void OnNewPost()
    {
        _postListScroll!.Visible = false;
        _postDetailPanel!.Visible = false;
        _newPostPanel!.Visible = true;
        _newPostTitle!.Text = "";
        _newPostBody!.Text = "";
        _stickyCheck!.ButtonPressed = false;
        _newPostTitle.GrabFocus();
    }

    private void OnSubmitPost()
    {
        if (_tcp == null || _state == null) return;

        string title = _newPostTitle!.Text.Trim();
        string body = _newPostBody!.Text.Trim();

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body))
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "El titulo y el mensaje no pueden estar vacios.",
                Color = "FF0000"
            });
            return;
        }

        // Determine forum message type based on current tab + sticky flag
        bool sticky = _stickyCheck!.ButtonPressed && _state.ForumCanMakeSticky > 0;
        byte msgType = _currentTab switch
        {
            1 => sticky ? TYPE_REAL_STICKY : TYPE_REAL,
            2 => sticky ? TYPE_CAOS_STICKY : TYPE_CAOS,
            _ => sticky ? TYPE_GENERAL_STICKY : TYPE_GENERAL
        };

        _tcp.SendPacket(ClientPackets.WriteForumPost(msgType, title, body));

        // Return to post list
        _newPostPanel!.Visible = false;
        _postListScroll!.Visible = true;

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Mensaje publicado.",
            Color = "00FF00"
        });
    }

    private void OnCancelPost()
    {
        _newPostPanel!.Visible = false;
        _postListScroll!.Visible = true;
    }

    private void OnClose()
    {
        Visible = false;
        // Clear accumulated posts (they'll be resent on next forum open)
        _state?.ForumPosts.Clear();
    }

    // ── Dragging ─────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < 28)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                }
                else
                {
                    _dragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Position - _dragOffset;
        }
    }
}
