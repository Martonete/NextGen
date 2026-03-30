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
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class ForumPanel : RpgBaseForm
{
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

    // Current tab index: 0=General, 1=Armada Real, 2=Legion Oscura
    private int _currentTab;

    // Tab bar
    private HBoxContainer? _tabBar;

    // Views (toggled via visibility)
    private VBoxContainer? _postListView;
    private VBoxContainer? _postListBox;
    private VBoxContainer? _postDetailView;
    private VBoxContainer? _newPostView;

    // Post detail controls
    private Label? _postDetailTitle;
    private Label? _postDetailAuthor;
    private TextEdit? _postDetailBody;

    // New post controls
    private LineEdit? _newPostTitle;
    private TextEdit? _newPostBody;
    private Button? _stickyCheck;

    // Cached post lists per board (populated from GameState.ForumPosts)
    private readonly List<ForumPostEntry> _generalPosts = new();
    private readonly List<ForumPostEntry> _realPosts = new();
    private readonly List<ForumPostEntry> _caosPosts = new();

    // Currently selected post for detail view
    private ForumPostEntry? _selectedPost;

    public ForumPanel() : base("Foro", new Vector2(520, 500), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        // Clear accumulated posts when the form is hidden (close button, Hide(), etc.)
        VisibilityChanged += () =>
        {
            if (!Visible) _state?.ForumPosts.Clear();
        };

        var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(root);

        // Tab bar
        _tabBar = RpgTheme.CreateTabBar(
            new[] { "General", "Armada Real", "Legion Oscura" },
            OnTabChanged
        );
        root.AddChild(_tabBar);

        // ── Post List View ──────────────────────────────────────
        _postListView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _postListView.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(_postListView);

        _postListBox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _postListBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        _postListView.AddChild(_postListBox);

        var listBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _postListView.AddChild(listBtnRow);

        var newPostBtn = RpgTheme.CreateRpgButton("Nuevo Post", false, 12);
        newPostBtn.CustomMinimumSize = new Vector2(100, 30);
        newPostBtn.Pressed += OnNewPost;
        listBtnRow.AddChild(newPostBtn);

        // ── Post Detail View ────────────────────────────────────
        _postDetailView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _postDetailView.SizeFlagsVertical = SizeFlags.ExpandFill;
        _postDetailView.Visible = false;
        root.AddChild(_postDetailView);

        _postDetailTitle = RpgTheme.CreateTitleLabel("", 14);
        _postDetailTitle.HorizontalAlignment = HorizontalAlignment.Left;
        _postDetailView.AddChild(_postDetailTitle);

        _postDetailAuthor = RpgTheme.CreateInfoLabel("", 11);
        _postDetailAuthor.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        _postDetailView.AddChild(_postDetailAuthor);

        _postDetailBody = RpgTheme.CreateRpgTextEdit("", 0, 250, readOnly: true);
        _postDetailView.AddChild(_postDetailBody);

        var backBtn = RpgTheme.CreateRpgButton("Volver", false, 12);
        backBtn.CustomMinimumSize = new Vector2(80, 28);
        backBtn.Pressed += OnBackFromDetail;
        _postDetailView.AddChild(backBtn);

        // ── New Post View ───────────────────────────────────────
        _newPostView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _newPostView.SizeFlagsVertical = SizeFlags.ExpandFill;
        _newPostView.Visible = false;
        root.AddChild(_newPostView);

        _newPostView.AddChild(RpgTheme.CreateInfoLabel("Titulo:", 12));

        _newPostTitle = RpgTheme.CreateRpgInput("Titulo del mensaje...");
        _newPostTitle.MaxLength = 100;
        _newPostView.AddChild(_newPostTitle);

        _newPostView.AddChild(RpgTheme.CreateInfoLabel("Mensaje:", 12));

        _newPostBody = RpgTheme.CreateRpgTextEdit("Escribe tu mensaje...", 0, 200);
        _newPostView.AddChild(_newPostBody);

        // Sticky checkbox (only visible for GMs)
        _stickyCheck = RpgTheme.CreateRpgCheckbox("default", false, new Vector2(26, 26));
        var stickyRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        stickyRow.AddChild(RpgTheme.CreateInfoLabel("Fijar (Sticky)", 11));
        stickyRow.AddChild(_stickyCheck);
        stickyRow.Visible = false; // Only visible for GMs
        _newPostView.AddChild(stickyRow);
        _newPostView.SetMeta("sticky_row", stickyRow);

        var composeBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _newPostView.AddChild(composeBtnRow);

        var submitBtn = RpgTheme.CreateRpgButton("Publicar", false, 12);
        submitBtn.CustomMinimumSize = new Vector2(90, 28);
        submitBtn.Pressed += OnSubmitPost;
        composeBtnRow.AddChild(submitBtn);

        var cancelPostBtn = RpgTheme.CreateRpgButton("Cancelar", false, 12);
        cancelPostBtn.CustomMinimumSize = new Vector2(90, 28);
        cancelPostBtn.Pressed += OnCancelPost;
        composeBtnRow.AddChild(cancelPostBtn);
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
        // Tab buttons are children of the tab bar — indices: 0=General, 1=Real, 2=Caos
        if (_tabBar != null && _tabBar.GetChildCount() >= 3)
        {
            if (_tabBar.GetChild(1) is Button tabReal)
                tabReal.Visible = (vis & VIS_REAL) != 0;
            if (_tabBar.GetChild(2) is Button tabCaos)
                tabCaos.Visible = (vis & VIS_CAOS) != 0;
        }

        // Show sticky checkbox for GMs
        var stickyRow = _newPostView?.GetMeta("sticky_row").As<Control>();
        if (stickyRow != null)
            stickyRow.Visible = _state.ForumCanMakeSticky > 0;

        // Reset to list view, General tab
        ShowListView();
        _selectedPost = null;

        SwitchTab(0);
        ShowForm();
    }

    private void OnTabChanged(int tab)
    {
        SwitchTab(tab);
    }

    private void SwitchTab(int tab)
    {
        _currentTab = tab;

        RpgTheme.SetTabBarActive(_tabBar!, tab);

        // Show list view
        ShowListView();

        BuildPostList();
    }

    private void ShowListView()
    {
        if (_postListView != null) _postListView.Visible = true;
        if (_postDetailView != null) _postDetailView.Visible = false;
        if (_newPostView != null) _newPostView.Visible = false;
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
            var emptyLbl = RpgTheme.CreateInfoLabel("No hay mensajes en este foro.", 11);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _postListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var post in sorted)
        {
            var row = RpgTheme.CreateRpgButton("", true, 11);
            row.CustomMinimumSize = new Vector2(0, 34);

            string prefix = post.IsSticky ? "[FIJO] " : "";
            if (row.GetChildCount() > 0 && row.GetChild(0) is Label lbl)
            {
                lbl.Text = $"{prefix}{post.Title}  --  {post.Author}";
                lbl.HorizontalAlignment = HorizontalAlignment.Left;
                if (post.IsSticky)
                    lbl.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
            }

            var captured = post;
            row.Pressed += () => ShowPostDetail(captured);
            _postListBox.AddChild(row);
        }
    }

    private void ShowPostDetail(ForumPostEntry post)
    {
        _selectedPost = post;

        if (_postListView != null) _postListView.Visible = false;
        if (_newPostView != null) _newPostView.Visible = false;
        if (_postDetailView != null) _postDetailView.Visible = true;

        string prefix = post.IsSticky ? "[FIJO] " : "";
        _postDetailTitle!.Text = $"{prefix}{post.Title}";
        _postDetailAuthor!.Text = $"Por: {post.Author}";
        _postDetailBody!.Text = post.Body;
    }

    private void OnBackFromDetail()
    {
        if (_postDetailView != null) _postDetailView.Visible = false;
        if (_postListView != null) _postListView.Visible = true;
        _selectedPost = null;
    }

    private void OnNewPost()
    {
        if (_postListView != null) _postListView.Visible = false;
        if (_postDetailView != null) _postDetailView.Visible = false;
        if (_newPostView != null) _newPostView.Visible = true;
        _newPostTitle!.Text = "";
        _newPostBody!.Text = "";
        if (_stickyCheck != null) _stickyCheck.ButtonPressed = false;
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
        ShowListView();

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Mensaje publicado.",
            Color = "00FF00"
        });
    }

    private void OnCancelPost()
    {
        ShowListView();
    }
}
