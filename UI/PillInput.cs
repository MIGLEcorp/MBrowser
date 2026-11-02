using System.Drawing.Drawing2D;

namespace MBrowser;

/// <summary>A capsule with a vertically centered native textbox and a matching opaque fill.</summary>
internal sealed class PillInput : Panel
{
    private readonly TextBox _input;
    private Button? _trailing;
    private float _focus;
    internal PillInput(TextBox input)
    {
        _input = input;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        input.Dock = DockStyle.None; input.Anchor = AnchorStyles.None; input.BorderStyle = BorderStyle.None;
        Controls.Add(input);
        input.GotFocus += (_, _) => AnimateFocus(1);
        input.LostFocus += (_, _) => AnimateFocus(0);
        input.FontChanged += (_, _) => CenterInput();
    }
    internal void SetTrailingButton(Button button)
    {
        if (_trailing is not null) Controls.Remove(_trailing);
        _trailing = button;
        button.BackColor = Color.Transparent;
        button.Margin = Padding.Empty;
        Controls.Add(button);
        button.BringToFront();
        CenterInput();
    }
    private void AnimateFocus(float target)
    { float start = _focus; UiMotion.Start(this, 150, t => { _focus = start + (target - start) * t; Invalidate(); }); }
    private void CenterInput()
    {
        if (_input is null) return;
        int inset = Math.Max(16, Height / 2);
        int trailing = _trailing is null ? inset : Math.Max(37, Height - 4) + 5;
        _input.SetBounds(inset, Math.Max(0, (Height - _input.PreferredHeight) / 2),
            Math.Max(1, Width - inset - trailing), _input.PreferredHeight);
        if (_trailing is not null)
        {
            int size = Math.Max(25, Height - 6);
            _trailing.SetBounds(Width - size - 3, (Height - size) / 2, size, size);
        }
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); CenterInput(); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 3 || Height < 3) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = TabStrip.RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), (Height - 3) / 2);
        using var fill = new SolidBrush(_input.BackColor); e.Graphics.FillPath(fill, shape);
        using var border = new Pen(UiMotion.Blend(Color.FromArgb(70, Palette.Muted), Palette.Accent, _focus), 1 + _focus);
        e.Graphics.DrawPath(border, shape);
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _input.Focus(); }
}