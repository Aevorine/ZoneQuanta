using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ZoneQuanta.UI.Controls;

// Read-only field that records the next key combination pressed while focused.
public sealed class HotkeyBox : TextBox
{
    private readonly Func<KeyChord> _get;
    private readonly Func<KeyChord, string?> _trySet;
    private readonly bool _needsModifier;
    private readonly DispatcherTimer _revert = new() { Interval = TimeSpan.FromMilliseconds(1400) };

    // Raised while a shortcut is being recorded: global hotkeys must step aside, or the combination
    // being typed would trigger its current owner instead of reaching the field.
    public static event Action<bool>? Recording;

    public static void SetRecording(bool on) => Recording?.Invoke(on);

    // Recording starts only from a click. Focus that WPF hands back when the window is re-activated
    // is dropped, otherwise the next global shortcut the user presses would be recorded by accident.
    public static void Guard(TextBox box, Action onRecord, Action onIdle)
    {
        bool armed = false;
        box.PreviewMouseLeftButtonDown += (_, _) => armed = true;
        box.GotKeyboardFocus += (_, _) =>
        {
            if (!armed)
            {
                box.Dispatcher.BeginInvoke(new Action(() => Release(box)));
                return;
            }
            onRecord();
            SetRecording(true);
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (!armed) return;
            armed = false;
            onIdle();
            SetRecording(false);
        };
    }

    public static void Release(TextBox box)
    {
        if (FocusManager.GetFocusScope(box) is { } scope && FocusManager.GetFocusedElement(scope) == box)
            FocusManager.SetFocusedElement(scope, null);
        if (box.IsKeyboardFocused) Keyboard.ClearFocus();
    }

    // trySet returns null when accepted, otherwise the reason it was refused.
    public HotkeyBox(Func<KeyChord> get, Func<KeyChord, string?> trySet, bool needsModifier)
    {
        _get = get;
        _trySet = trySet;
        _needsModifier = needsModifier;
        IsReadOnly = true;
        Cursor = Cursors.Hand;
        IsUndoEnabled = false;
        SetResourceReference(StyleProperty, "Field");
        Show();
        PreviewKeyDown += OnKey;
        Guard(this, () => Text = "按下新的快捷键…", Show);
        IsVisibleChanged += (_, _) => { if (IsVisible) Show(); };
        _revert.Tick += (_, _) =>
        {
            _revert.Stop();
            ClearValue(ForegroundProperty);
            if (IsKeyboardFocused) Text = "按下新的快捷键…";
            else Show();
        };
    }

    public void Show() => Text = _get().Display;

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (KeyChord.IsModifier(key)) return;
        var mods = Keyboard.Modifiers;
        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            Release(this);
            return;
        }
        if (_needsModifier && (mods & ~ModifierKeys.Shift) == ModifierKeys.None) return;

        if (Apply(new KeyChord(mods, key))) Release(this);
    }

    public bool Apply(KeyChord chord)
    {
        string? refused = _trySet(chord);
        if (refused is null)
        {
            Show();
            return true;
        }
        Text = refused;
        SetResourceReference(ForegroundProperty, "Accent2Brush");
        _revert.Stop();
        _revert.Start();
        return false;
    }
}
