using Godot;
using System;

namespace ArgentumNextgen.UI;

/// <summary>
/// Simple error/message dialog. Extends RpgBaseForm for guaranteed input handling.
/// Shows a message with an "Aceptar" button and X close button.
/// </summary>
public partial class MensajeForm : RpgBaseForm
{
    public Label? MessageLabel { get; private set; }
    public Action? OnAccept;

    public MensajeForm()
        : base("Mensaje", new Vector2(380, 200), "v1")
    {
        Draggable = true;
        ShowCloseButton = true;
        CloseOnEscape = true;
    }

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        root.SizeFlagsVertical = SizeFlags.ExpandFill;
        ContentContainer.AddChild(root);

        // Message text — expands to fill
        MessageLabel = RpgTheme.CreateInfoLabel("", 13);
        MessageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        MessageLabel.VerticalAlignment = VerticalAlignment.Center;
        MessageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        MessageLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(MessageLabel);

        // Aceptar button — centered
        var btnRow = RpgTheme.CreateRow();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(btnRow);

        var acceptBtn = RpgTheme.CreateRpgButton("Aceptar", true, 14);
        acceptBtn.CustomMinimumSize = new Vector2(130, 36);
        acceptBtn.MouseDefaultCursorShape = CursorShape.PointingHand;
        acceptBtn.Pressed += () => OnAccept?.Invoke();
        btnRow.AddChild(acceptBtn);
    }
}
