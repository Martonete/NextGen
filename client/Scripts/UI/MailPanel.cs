using Godot;
using System.Linq;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Mail panel — inbox list, read view, and compose view.
/// Follows ForumPanel pattern: programmatic nodes, dark theme, draggable.
/// </summary>
public partial class MailPanel : Control
{
    private const int PanelW = 520;
    private const int PanelH = 480;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Current view: 0=inbox, 1=read, 2=compose
    private int _currentView;

    // UI controls — shared
    private Label? _titleLabel;
    private Button? _closeBtn;
    private HBoxContainer? _tabBar;
    private Button? _tabInbox;
    private Button? _tabCompose;

    // Inbox view
    private ScrollContainer? _inboxScroll;
    private VBoxContainer? _inboxListBox;
    private Label? _inboxCountLabel;

    // Read view
    private Panel? _readPanel;
    private Label? _readSender;
    private Label? _readSubject;
    private Label? _readDate;
    private RichTextLabel? _readBody;
    private Label? _readAttachLabel;
    private Button? _readReplyBtn;
    private Button? _readDeleteBtn;
    private Button? _readExtractBtn;
    private Button? _readBackBtn;

    // Compose view
    private Panel? _composePanel;
    private LineEdit? _composeRecipient;
    private LineEdit? _composeSubject;
    private TextEdit? _composeBody;
    private Button? _composeSendBtn;
    private Button? _composeCancelBtn;

    // Currently selected mail for read view
    private MailEntry? _selectedMail;

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
        _titleLabel.Text = "Correo";
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

        _tabInbox = new Button { Text = "Bandeja de Entrada", ToggleMode = true, ButtonPressed = true };
        _tabInbox.CustomMinimumSize = new Vector2(160, 26);
        _tabInbox.Pressed += () => SwitchView(0);
        _tabBar.AddChild(_tabInbox);

        _tabCompose = new Button { Text = "Escribir", ToggleMode = true };
        _tabCompose.CustomMinimumSize = new Vector2(100, 26);
        _tabCompose.Pressed += () => SwitchView(2);
        _tabBar.AddChild(_tabCompose);

        BuildInboxView();
        BuildReadView();
        BuildComposeView();

        // Bottom close button
        var closeBottomBtn = new Button { Text = "Cerrar" };
        closeBottomBtn.Position = new Vector2(PanelW - 80, PanelH - 36);
        closeBottomBtn.Size = new Vector2(70, 28);
        closeBottomBtn.Pressed += OnClose;
        AddChild(closeBottomBtn);
    }

    private void BuildInboxView()
    {
        // Inbox count label
        _inboxCountLabel = new Label();
        _inboxCountLabel.Text = "0 mensajes";
        _inboxCountLabel.Position = new Vector2(PanelW - 180, 30);
        _inboxCountLabel.Size = new Vector2(160, 20);
        _inboxCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _inboxCountLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        _inboxCountLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_inboxCountLabel);

        _inboxScroll = new ScrollContainer();
        _inboxScroll.Position = new Vector2(8, 60);
        _inboxScroll.Size = new Vector2(PanelW - 16, PanelH - 106);
        AddChild(_inboxScroll);

        _inboxListBox = new VBoxContainer();
        _inboxListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _inboxScroll.AddChild(_inboxListBox);
    }

    private void BuildReadView()
    {
        _readPanel = new Panel();
        _readPanel.Position = new Vector2(8, 60);
        _readPanel.Size = new Vector2(PanelW - 16, PanelH - 106);
        _readPanel.Visible = false;
        AddChild(_readPanel);

        _readSender = new Label();
        _readSender.Position = new Vector2(8, 8);
        _readSender.Size = new Vector2(PanelW - 40, 18);
        _readSender.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1.0f));
        _readSender.AddThemeFontSizeOverride("font_size", 12);
        _readPanel.AddChild(_readSender);

        _readSubject = new Label();
        _readSubject.Position = new Vector2(8, 28);
        _readSubject.Size = new Vector2(PanelW - 40, 20);
        _readSubject.AddThemeFontSizeOverride("font_size", 13);
        _readPanel.AddChild(_readSubject);

        _readDate = new Label();
        _readDate.Position = new Vector2(8, 50);
        _readDate.Size = new Vector2(PanelW - 40, 16);
        _readDate.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        _readDate.AddThemeFontSizeOverride("font_size", 10);
        _readPanel.AddChild(_readDate);

        // Separator
        var sep = new HSeparator();
        sep.Position = new Vector2(8, 68);
        sep.Size = new Vector2(PanelW - 40, 2);
        _readPanel.AddChild(sep);

        _readBody = new RichTextLabel();
        _readBody.Position = new Vector2(8, 76);
        _readBody.Size = new Vector2(PanelW - 40, PanelH - 260);
        _readBody.BbcodeEnabled = false;
        _readBody.ScrollActive = true;
        _readPanel.AddChild(_readBody);

        // Attachment label
        _readAttachLabel = new Label();
        _readAttachLabel.Position = new Vector2(8, PanelH - 178);
        _readAttachLabel.Size = new Vector2(PanelW - 40, 18);
        _readAttachLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
        _readAttachLabel.AddThemeFontSizeOverride("font_size", 11);
        _readAttachLabel.Visible = false;
        _readPanel.AddChild(_readAttachLabel);

        // Button row
        float btnY = PanelH - 152;

        _readBackBtn = new Button { Text = "Volver" };
        _readBackBtn.Position = new Vector2(8, btnY);
        _readBackBtn.Size = new Vector2(70, 28);
        _readBackBtn.Pressed += OnBackFromRead;
        _readPanel.AddChild(_readBackBtn);

        _readReplyBtn = new Button { Text = "Responder" };
        _readReplyBtn.Position = new Vector2(86, btnY);
        _readReplyBtn.Size = new Vector2(90, 28);
        _readReplyBtn.Pressed += OnReply;
        _readPanel.AddChild(_readReplyBtn);

        _readExtractBtn = new Button { Text = "Retirar Adjunto" };
        _readExtractBtn.Position = new Vector2(184, btnY);
        _readExtractBtn.Size = new Vector2(120, 28);
        _readExtractBtn.Pressed += OnExtractAttachment;
        _readPanel.AddChild(_readExtractBtn);

        _readDeleteBtn = new Button { Text = "Eliminar" };
        _readDeleteBtn.Position = new Vector2(PanelW - 106, btnY);
        _readDeleteBtn.Size = new Vector2(80, 28);
        _readDeleteBtn.Pressed += OnDeleteMail;
        _readPanel.AddChild(_readDeleteBtn);
    }

    private void BuildComposeView()
    {
        _composePanel = new Panel();
        _composePanel.Position = new Vector2(8, 60);
        _composePanel.Size = new Vector2(PanelW - 16, PanelH - 106);
        _composePanel.Visible = false;
        AddChild(_composePanel);

        var recipLbl = new Label { Text = "Destinatario:" };
        recipLbl.Position = new Vector2(8, 8);
        _composePanel.AddChild(recipLbl);

        _composeRecipient = new LineEdit();
        _composeRecipient.Position = new Vector2(8, 28);
        _composeRecipient.Size = new Vector2(PanelW - 40, 28);
        _composeRecipient.MaxLength = 30;
        _composeRecipient.PlaceholderText = "Nombre del destinatario...";
        _composePanel.AddChild(_composeRecipient);

        var subjectLbl = new Label { Text = "Asunto:" };
        subjectLbl.Position = new Vector2(8, 62);
        _composePanel.AddChild(subjectLbl);

        _composeSubject = new LineEdit();
        _composeSubject.Position = new Vector2(8, 82);
        _composeSubject.Size = new Vector2(PanelW - 40, 28);
        _composeSubject.MaxLength = 100;
        _composeSubject.PlaceholderText = "Asunto del mensaje...";
        _composePanel.AddChild(_composeSubject);

        var bodyLbl = new Label { Text = "Mensaje:" };
        bodyLbl.Position = new Vector2(8, 116);
        _composePanel.AddChild(bodyLbl);

        _composeBody = new TextEdit();
        _composeBody.Position = new Vector2(8, 136);
        _composeBody.Size = new Vector2(PanelW - 40, PanelH - 280);
        _composeBody.PlaceholderText = "Escribe tu mensaje...";
        _composePanel.AddChild(_composeBody);

        float btnY = PanelH - 138;

        _composeSendBtn = new Button { Text = "Enviar" };
        _composeSendBtn.Position = new Vector2(PanelW - 210, btnY);
        _composeSendBtn.Size = new Vector2(80, 28);
        _composeSendBtn.Pressed += OnSendMail;
        _composePanel.AddChild(_composeSendBtn);

        _composeCancelBtn = new Button { Text = "Cancelar" };
        _composeCancelBtn.Position = new Vector2(PanelW - 120, btnY);
        _composeCancelBtn.Size = new Vector2(90, 28);
        _composeCancelBtn.Pressed += () => SwitchView(0);
        _composePanel.AddChild(_composeCancelBtn);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Rebuild inbox when dirty
        if (_state.MailInboxDirty && _currentView == 0)
        {
            _state.MailInboxDirty = false;
            BuildInboxList();
        }

        // Auto-show read view when MailCurrentMessage arrives
        if (_state.MailCurrentMessage != null && _currentView != 1)
        {
            ShowReadView(_state.MailCurrentMessage);
            _state.MailCurrentMessage = null;
        }
    }

    /// <summary>
    /// Open the mail panel to the inbox view.
    /// </summary>
    public void ShowPanel()
    {
        if (_state == null) return;
        SwitchView(0);
        BuildInboxList();
        Visible = true;
    }

    /// <summary>
    /// Toggle visibility.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
            OnClose();
        else
            ShowPanel();
    }

    private void SwitchView(int view)
    {
        _currentView = view;

        _tabInbox!.ButtonPressed = (view == 0);
        _tabCompose!.ButtonPressed = (view == 2);

        _inboxScroll!.Visible = (view == 0);
        _inboxCountLabel!.Visible = (view == 0);
        _readPanel!.Visible = (view == 1);
        _composePanel!.Visible = (view == 2);

        if (view == 0)
            BuildInboxList();
        else if (view == 2)
        {
            _composeRecipient!.Text = "";
            _composeSubject!.Text = "";
            _composeBody!.Text = "";
            _composeRecipient.GrabFocus();
        }
    }

    private void BuildInboxList()
    {
        if (_inboxListBox == null || _state == null) return;

        foreach (var child in _inboxListBox.GetChildren())
            child.QueueFree();

        var mails = _state.MailInbox;
        int unread = mails.Count(m => !m.Read);
        _inboxCountLabel!.Text = $"{mails.Count} mensajes ({unread} sin leer)";

        if (mails.Count == 0)
        {
            var emptyLbl = new Label { Text = "No tienes mensajes." };
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _inboxListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var mail in mails)
        {
            var row = new HBoxContainer();
            row.CustomMinimumSize = new Vector2(PanelW - 30, 34);

            // Unread indicator
            var indicator = new Label();
            indicator.Text = mail.Read ? "  " : "● ";
            indicator.CustomMinimumSize = new Vector2(20, 30);
            indicator.AddThemeColorOverride("font_color",
                mail.Read ? new Color(0.3f, 0.3f, 0.3f) : new Color(1.0f, 0.85f, 0.2f));
            indicator.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(indicator);

            // Mail info button (clickable to open)
            var captured = mail;
            var infoBtn = new Button();
            infoBtn.Text = $"{mail.Sender}: {mail.Subject}  ({mail.Date})";
            infoBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            infoBtn.Alignment = HorizontalAlignment.Left;
            infoBtn.ClipText = true;
            infoBtn.CustomMinimumSize = new Vector2(0, 30);

            if (!mail.Read)
                infoBtn.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 1.0f));
            else
                infoBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));

            infoBtn.Pressed += () => OnOpenMail(captured);
            row.AddChild(infoBtn);

            // Delete button
            var delBtn = new Button();
            delBtn.Text = "X";
            delBtn.CustomMinimumSize = new Vector2(28, 28);
            delBtn.TooltipText = "Eliminar mensaje";
            delBtn.Pressed += () => OnDeleteMailFromList(captured);
            row.AddChild(delBtn);

            _inboxListBox.AddChild(row);
        }
    }

    private void OnOpenMail(MailEntry mail)
    {
        // If we already have the body, show it directly
        if (!string.IsNullOrEmpty(mail.Body))
        {
            ShowReadView(mail);
            return;
        }

        // Otherwise request full content from server
        // For now, show what we have (the server should send MailContent in response)
        ShowReadView(mail);
    }

    private void ShowReadView(MailEntry mail)
    {
        _selectedMail = mail;
        _currentView = 1;

        _inboxScroll!.Visible = false;
        _inboxCountLabel!.Visible = false;
        _composePanel!.Visible = false;
        _readPanel!.Visible = true;

        _tabInbox!.ButtonPressed = false;
        _tabCompose!.ButtonPressed = false;

        _readSender!.Text = $"De: {mail.Sender}";
        _readSubject!.Text = mail.Subject;
        _readDate!.Text = mail.Date;
        _readBody!.Text = string.IsNullOrEmpty(mail.Body) ? "(Cargando contenido...)" : mail.Body;

        // Attachment info
        bool hasAttach = mail.AttachedGold > 0 || mail.AttachedItemId > 0;
        _readAttachLabel!.Visible = hasAttach;
        _readExtractBtn!.Visible = hasAttach;
        if (hasAttach)
        {
            string attachText = "";
            if (mail.AttachedGold > 0)
                attachText += $"Oro: {mail.AttachedGold}";
            if (mail.AttachedItemId > 0)
            {
                if (attachText.Length > 0) attachText += " | ";
                attachText += $"Item #{mail.AttachedItemId} x{mail.AttachedItemAmount}";
            }
            _readAttachLabel.Text = $"Adjunto: {attachText}";
        }
    }

    private void OnBackFromRead()
    {
        _selectedMail = null;
        SwitchView(0);
    }

    private void OnReply()
    {
        if (_selectedMail == null) return;

        SwitchView(2);
        _composeRecipient!.Text = _selectedMail.Sender;
        _composeSubject!.Text = _selectedMail.Subject.StartsWith("Re: ")
            ? _selectedMail.Subject
            : $"Re: {_selectedMail.Subject}";
        _composeBody!.Text = "";
        _composeBody.GrabFocus();
    }

    private void OnDeleteMail()
    {
        if (_tcp == null || _state == null || _selectedMail == null) return;

        _tcp.SendPacket(ClientPackets.WriteMailDelete(_selectedMail.Id));

        // Remove locally
        _state.MailInbox.RemoveAll(m => m.Id == _selectedMail.Id);
        _selectedMail = null;

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Mensaje eliminado.",
            Color = "FFAA00"
        });

        SwitchView(0);
    }

    private void OnDeleteMailFromList(MailEntry mail)
    {
        if (_tcp == null || _state == null) return;

        _tcp.SendPacket(ClientPackets.WriteMailDelete(mail.Id));

        _state.MailInbox.RemoveAll(m => m.Id == mail.Id);
        BuildInboxList();

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Mensaje eliminado.",
            Color = "FFAA00"
        });
    }

    private void OnExtractAttachment()
    {
        if (_tcp == null || _state == null || _selectedMail == null) return;

        _tcp.SendPacket(ClientPackets.WriteMailExtract(_selectedMail.Id));

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Adjunto retirado.",
            Color = "00FF00"
        });

        // Clear attachment display
        _selectedMail.AttachedGold = 0;
        _selectedMail.AttachedItemId = 0;
        _selectedMail.AttachedItemAmount = 0;
        _readAttachLabel!.Visible = false;
        _readExtractBtn!.Visible = false;
    }

    private void OnSendMail()
    {
        if (_tcp == null || _state == null) return;

        string recipient = _composeRecipient!.Text.Trim();
        string subject = _composeSubject!.Text.Trim();
        string body = _composeBody!.Text.Trim();

        if (string.IsNullOrEmpty(recipient))
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Ingresa el nombre del destinatario.",
                Color = "FF0000"
            });
            return;
        }

        if (string.IsNullOrEmpty(subject))
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Ingresa el asunto del mensaje.",
                Color = "FF0000"
            });
            return;
        }

        if (string.IsNullOrEmpty(body))
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "El mensaje no puede estar vacio.",
                Color = "FF0000"
            });
            return;
        }

        _tcp.SendPacket(ClientPackets.WriteMailSend(recipient, subject, body));

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Mensaje enviado a {recipient}.",
            Color = "00FF00"
        });

        // Return to inbox
        SwitchView(0);
    }

    private void OnClose()
    {
        Visible = false;
        _selectedMail = null;
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
