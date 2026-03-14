using Godot;
using System.Linq;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Mail panel — inbox list, read view, and compose view.
/// 3 views toggled via visibility: inbox (0), read (1), compose (2).
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class MailPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Current view: 0=inbox, 1=read, 2=compose
    private int _currentView;

    // Tab bar
    private HBoxContainer? _tabBar;

    // Views
    private VBoxContainer? _inboxView;
    private VBoxContainer? _readView;
    private VBoxContainer? _composeView;

    // Inbox controls
    private VBoxContainer? _inboxListBox;
    private Label? _inboxCountLabel;

    // Read controls
    private Label? _readSender;
    private Label? _readSubject;
    private Label? _readDate;
    private TextEdit? _readBody;
    private Label? _readAttachLabel;
    private TextureButton? _readExtractBtn;

    // Compose controls
    private LineEdit? _composeRecipient;
    private LineEdit? _composeSubject;
    private TextEdit? _composeBody;

    // Currently selected mail for read view
    private MailEntry? _selectedMail;

    public MailPanel() : base("Correo", new Vector2(520, 480), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        // Clear selected mail when the form is hidden (close button, Hide(), etc.)
        VisibilityChanged += () =>
        {
            if (!Visible) _selectedMail = null;
        };

        var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(root);

        // Tab bar
        _tabBar = RpgTheme.CreateTabBar(
            new[] { "Bandeja de Entrada", "Escribir" },
            OnTabChanged
        );
        root.AddChild(_tabBar);

        // ── Inbox View ──────────────────────────────────────────
        _inboxView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _inboxView.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(_inboxView);

        _inboxCountLabel = RpgTheme.CreateInfoLabel("0 mensajes", 11);
        _inboxCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _inboxCountLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        _inboxView.AddChild(_inboxCountLabel);

        _inboxListBox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _inboxListBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        _inboxView.AddChild(_inboxListBox);

        // ── Read View ───────────────────────────────────────────
        _readView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _readView.SizeFlagsVertical = SizeFlags.ExpandFill;
        _readView.Visible = false;
        root.AddChild(_readView);

        _readSender = RpgTheme.CreateInfoLabel("", 12);
        _readSender.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1.0f));
        _readView.AddChild(_readSender);

        _readSubject = RpgTheme.CreateTitleLabel("", 14);
        _readSubject.HorizontalAlignment = HorizontalAlignment.Left;
        _readView.AddChild(_readSubject);

        _readDate = RpgTheme.CreateInfoLabel("", 10);
        _readDate.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        _readView.AddChild(_readDate);

        _readView.AddChild(RpgTheme.CreateSeparator());

        _readBody = RpgTheme.CreateRpgTextEdit("", 0, 180, readOnly: true);
        _readView.AddChild(_readBody);

        // Attachment label
        _readAttachLabel = RpgTheme.CreateInfoLabel("", 11);
        _readAttachLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
        _readAttachLabel.Visible = false;
        _readView.AddChild(_readAttachLabel);

        // Read view buttons
        var readBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _readView.AddChild(readBtnRow);

        var readBackBtn = RpgTheme.CreateRpgButton("Volver", false, 12);
        readBackBtn.CustomMinimumSize = new Vector2(70, 28);
        readBackBtn.Pressed += OnBackFromRead;
        readBtnRow.AddChild(readBackBtn);

        var readReplyBtn = RpgTheme.CreateRpgButton("Responder", false, 12);
        readReplyBtn.CustomMinimumSize = new Vector2(90, 28);
        readReplyBtn.Pressed += OnReply;
        readBtnRow.AddChild(readReplyBtn);

        _readExtractBtn = RpgTheme.CreateRpgButton("Retirar Adjunto", false, 11);
        _readExtractBtn.CustomMinimumSize = new Vector2(120, 28);
        _readExtractBtn.Pressed += OnExtractAttachment;
        _readExtractBtn.Visible = false;
        readBtnRow.AddChild(_readExtractBtn);

        var readDeleteBtn = RpgTheme.CreateRpgButton("Eliminar", false, 12);
        readDeleteBtn.CustomMinimumSize = new Vector2(80, 28);
        readDeleteBtn.Pressed += OnDeleteMail;
        readBtnRow.AddChild(readDeleteBtn);

        // ── Compose View ────────────────────────────────────────
        _composeView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _composeView.SizeFlagsVertical = SizeFlags.ExpandFill;
        _composeView.Visible = false;
        root.AddChild(_composeView);

        _composeView.AddChild(RpgTheme.CreateInfoLabel("Destinatario:", 12));

        _composeRecipient = RpgTheme.CreateRpgInput("Nombre del destinatario...");
        _composeRecipient.MaxLength = 30;
        _composeView.AddChild(_composeRecipient);

        _composeView.AddChild(RpgTheme.CreateInfoLabel("Asunto:", 12));

        _composeSubject = RpgTheme.CreateRpgInput("Asunto del mensaje...");
        _composeSubject.MaxLength = 100;
        _composeView.AddChild(_composeSubject);

        _composeView.AddChild(RpgTheme.CreateInfoLabel("Mensaje:", 12));

        _composeBody = RpgTheme.CreateRpgTextEdit("Escribe tu mensaje...", 0, 160);
        _composeView.AddChild(_composeBody);

        var composeBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _composeView.AddChild(composeBtnRow);

        var composeSendBtn = RpgTheme.CreateRpgButton("Enviar", false, 12);
        composeSendBtn.CustomMinimumSize = new Vector2(80, 28);
        composeSendBtn.Pressed += OnSendMail;
        composeBtnRow.AddChild(composeSendBtn);

        var composeCancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 12);
        composeCancelBtn.CustomMinimumSize = new Vector2(90, 28);
        composeCancelBtn.Pressed += () => SwitchView(0);
        composeBtnRow.AddChild(composeCancelBtn);
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
        ShowForm();
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

    private void OnTabChanged(int tab)
    {
        // Tab 0 = Inbox, Tab 1 = Compose
        if (tab == 0)
            SwitchView(0);
        else if (tab == 1)
            SwitchView(2);
    }

    private void SwitchView(int view)
    {
        _currentView = view;

        // Update tab bar (inbox=tab0, compose=tab1, read=no tab)
        if (view == 0)
            RpgTheme.SetTabBarActive(_tabBar!, 0);
        else if (view == 2)
            RpgTheme.SetTabBarActive(_tabBar!, 1);

        _inboxView!.Visible = (view == 0);
        _readView!.Visible = (view == 1);
        _composeView!.Visible = (view == 2);

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
            var emptyLbl = RpgTheme.CreateInfoLabel("No tienes mensajes.", 11);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _inboxListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var mail in mails)
        {
            var row = RpgTheme.CreateRow(RpgTheme.SpacingSm);
            row.CustomMinimumSize = new Vector2(0, 32);

            // Unread indicator
            var indicator = RpgTheme.CreateInfoLabel(mail.Read ? "  " : "* ", 12);
            indicator.CustomMinimumSize = new Vector2(20, 28);
            indicator.AddThemeColorOverride("font_color",
                mail.Read ? new Color(0.3f, 0.3f, 0.3f) : new Color(1.0f, 0.85f, 0.2f));
            row.AddChild(indicator);

            // Mail info button (clickable to open)
            var captured = mail;
            var infoBtn = RpgTheme.CreateRpgButton("", true, 11);
            infoBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            infoBtn.CustomMinimumSize = new Vector2(0, 28);

            if (infoBtn.GetChildCount() > 0 && infoBtn.GetChild(0) is Label lbl)
            {
                lbl.Text = $"{mail.Sender}: {mail.Subject}  ({mail.Date})";
                lbl.HorizontalAlignment = HorizontalAlignment.Left;
                if (!mail.Read)
                    lbl.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 1.0f));
                else
                    lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            }

            infoBtn.Pressed += () => OnOpenMail(captured);
            row.AddChild(infoBtn);

            // Delete button
            var delBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(24, 24));
            delBtn.TooltipText = "Eliminar mensaje";
            delBtn.Pressed += () => OnDeleteMailFromList(captured);
            row.AddChild(delBtn);

            _inboxListBox.AddChild(row);
        }
    }

    private void OnOpenMail(MailEntry mail)
    {
        // If we already have the body, show it directly
        // Otherwise request full content from server (the server should send MailContent in response)
        ShowReadView(mail);
    }

    private void ShowReadView(MailEntry mail)
    {
        _selectedMail = mail;
        _currentView = 1;

        _inboxView!.Visible = false;
        _composeView!.Visible = false;
        _readView!.Visible = true;

        // No tab is active when reading
        RpgTheme.SetTabBarActive(_tabBar!, -1);

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
        HideForm();
        _selectedMail = null;
    }
}
