using System.Windows.Controls;
using System.Windows.Input;
using Akeno.ColorVision.Core.Hotkeys;

namespace Akeno.ColorVision.App.Controls;

/// <summary>
/// A text box that records a keyboard shortcut instead of text: focus it and press the keys.
/// Escape cancels, Tab still moves focus. Bind <c>Text</c> with <c>UpdateSourceTrigger=Explicit</c>.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ToolTip = "Click here, then press the new key combination (for example Ctrl + Alt + Shift + F).";
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = "Press the new shortcut…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        GetBindingExpression(TextProperty)?.UpdateTarget();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

        if (key == Key.Tab && modifiers == ModifierKeys.None)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            GetBindingExpression(TextProperty)?.UpdateTarget();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        var hotkeyModifiers = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            hotkeyModifiers |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            hotkeyModifiers |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            hotkeyModifiers |= HotkeyModifiers.Shift;
        }

        if (win || modifiers.HasFlag(ModifierKeys.Windows))
        {
            hotkeyModifiers |= HotkeyModifiers.Windows;
        }

        var name = KeyName(key);
        if (name is null)
        {
            // Only modifiers so far: show progress.
            Text = hotkeyModifiers == HotkeyModifiers.None ? "Press the new shortcut…" : new HotkeyGesture(hotkeyModifiers, "…").ToString();
            return;
        }

        Text = new HotkeyGesture(hotkeyModifiers, name).ToString();
        GetBindingExpression(TextProperty)?.UpdateSource();
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private static string? KeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return key.ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return key.ToString();
        }

        return key switch
        {
            Key.Back => "Backspace",
            Key.Return => "Enter",
            Key.Space => "Space",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Pause => "Pause",
            Key.Multiply => "Multiply",
            Key.Add => "Add",
            Key.Subtract => "Subtract",
            Key.Divide => "Divide",
            Key.Decimal => "Decimal",
            _ => null,
        };
    }
}
