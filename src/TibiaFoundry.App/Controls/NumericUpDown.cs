using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TibiaFoundry.App.Controls;

/// <summary>
/// Integer spin box (WPF has no built-in NumericUpDown). Typing is validated on commit
/// (Enter/lost focus); out-of-range values are clamped; the mouse wheel and arrow keys step.
/// </summary>
[TemplatePart(Name = "PART_Text", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Up", Type = typeof(RepeatButton))]
[TemplatePart(Name = "PART_Down", Type = typeof(RepeatButton))]
public sealed class NumericUpDown : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int), typeof(NumericUpDown),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumericUpDown)d).SyncText(), CoerceValue));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumericUpDown),
        new PropertyMetadata(0, (d, _) => d.CoerceValue(ValueProperty)));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumericUpDown),
        new PropertyMetadata(int.MaxValue, (d, _) => d.CoerceValue(ValueProperty)));
    public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(nameof(Increment), typeof(int), typeof(NumericUpDown),
        new PropertyMetadata(1));

    private TextBox? _text;

    static NumericUpDown() => DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericUpDown), new FrameworkPropertyMetadata(typeof(NumericUpDown)));

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Increment { get => (int)GetValue(IncrementProperty); set => SetValue(IncrementProperty, value); }

    private static object CoerceValue(DependencyObject d, object value)
    {
        var c = (NumericUpDown)d;
        return Math.Clamp((int)value, c.Minimum, Math.Max(c.Minimum, c.Maximum));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_text != null)
        {
            _text.LostKeyboardFocus -= OnTextLostFocus;
            _text.PreviewKeyDown -= OnTextKeyDown;
        }
        _text = GetTemplateChild("PART_Text") as TextBox;
        if (_text != null)
        {
            _text.LostKeyboardFocus += OnTextLostFocus;
            _text.PreviewKeyDown += OnTextKeyDown;
            System.Windows.Automation.AutomationProperties.SetName(_text, System.Windows.Automation.AutomationProperties.GetName(this));
        }
        if (GetTemplateChild("PART_Up") is RepeatButton up) up.Click += (_, _) => Step(1);
        if (GetTemplateChild("PART_Down") is RepeatButton down) down.Click += (_, _) => Step(-1);
        SyncText();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsKeyboardFocusWithin) return;
        Step(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void Step(int direction)
    {
        Commit();
        Value = (int)Math.Clamp((long)Value + direction * (long)Increment, Minimum, Maximum);
    }

    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Step(1); e.Handled = true; break;
            case Key.Down: Step(-1); e.Handled = true; break;
            case Key.Enter: Commit(); e.Handled = true; break;
        }
    }

    private void OnTextLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void Commit()
    {
        if (_text == null) return;
        if (int.TryParse(_text.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsed)) Value = parsed;
        SyncText();
    }

    private void SyncText()
    {
        if (_text != null) _text.Text = Value.ToString(CultureInfo.CurrentCulture);
    }
}
