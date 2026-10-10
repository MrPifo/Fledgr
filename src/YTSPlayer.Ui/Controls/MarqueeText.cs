using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace YTSPlayer.Ui.Controls;

/// <summary>
/// Text in einer Zeile. Passt er nicht in die Breite, läuft er wie eine Laufschrift von rechts nach links.
/// Am Ende springt er zurück zum Anfang, bleibt kurz stehen und läuft wieder los. Der ganze Text steht im Tooltip.
/// Die Schrift kommt über Classes wie bei TextBlock (z. B. Classes="heading", "muted"). Der Takt läuft nur, wenn der Text überläuft.
/// </summary>
public class MarqueeText : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MarqueeText, string?>(nameof(Text));

    /// <summary>Pause am Anfang, bevor der Text losläuft (ms).</summary>
    const int StartPauseMs = 2000;

    /// <summary>Pause am Ende, bevor er zum Anfang springt (ms).</summary>
    const int EndPauseMs = 800;

    /// <summary>Tempo in Pixel pro Sekunde.</summary>
    const double Speed = 45;

    /// <summary>Abstand nach dem letzten Zeichen, bevor das Ende als erreicht gilt.</summary>
    const double EndGap = 12;

    readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    readonly TranslateTransform _shift = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch _clock = new();
    double _offset;
    double _waitMs;
    double _maxOffset;
    long _lastTick;
    bool _atEnd;

    static MarqueeText()
    {
        TextProperty.Changed.AddClassHandler<MarqueeText>((m, _) => m.OnTextChanged());
        AffectsMeasure<MarqueeText>(TextProperty);
    }

    public MarqueeText()
    {
        ClipToBounds = true;
        _text.RenderTransform = _shift;
        VisualChildren.Add(_text);
        LogicalChildren.Add(_text);
        _timer.Tick += (_, _) => Tick();
        Classes.CollectionChanged += (_, _) => SyncClasses();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncClasses();
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Die Classes dieses Elements (z. B. „heading muted“) gelten auch für den inneren Text.</summary>
    void SyncClasses()
    {
        _text.Classes.Clear();
        foreach (var c in Classes.Where(c => !c.StartsWith(':'))) _text.Classes.Add(c);
    }

    void OnTextChanged()
    {
        _text.Text = Text;
        ToolTip.SetTip(this, string.IsNullOrEmpty(Text) ? null : Text);
        Restart();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(Size.Infinity);
        var w = _text.DesiredSize.Width;
        var width = double.IsInfinity(availableSize.Width) ? w : Math.Min(w, availableSize.Width);
        return new Size(width, _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _text.Arrange(new Rect(0, 0, Math.Max(_text.DesiredSize.Width, finalSize.Width), finalSize.Height));
        UpdateScrolling(finalSize.Width);
        return finalSize;
    }

    /// <summary>Startet oder stoppt den Takt, je nachdem ob der Text breiter ist als der sichtbare Bereich.</summary>
    void UpdateScrolling(double viewWidth)
    {
        _maxOffset = Math.Max(0, _text.DesiredSize.Width - viewWidth + EndGap);
        var overflow = viewWidth > 0 && _text.DesiredSize.Width > viewWidth;
        if (overflow && VisualRoot != null)
        {
            if (!_timer.IsEnabled)
            {
                _lastTick = 0;
                _clock.Restart();
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
            SetOffset(0);
        }
    }

    /// <summary>Beginnt wieder am Anfang mit der Pause (neuer Text oder neu angezeigt).</summary>
    void Restart()
    {
        _atEnd = false;
        _waitMs = StartPauseMs;
        SetOffset(0);
        InvalidateMeasure();
    }

    void Tick()
    {
        var now = _clock.ElapsedMilliseconds;
        var dt = _lastTick == 0 ? 0 : now - _lastTick;
        _lastTick = now;
        if (_waitMs > 0)
        {
            _waitMs -= dt;
            return;
        }
        if (_atEnd)
        {
            // Am Ende kurz gewartet: zurück zum Anfang und dort wieder warten.
            _atEnd = false;
            _waitMs = StartPauseMs;
            SetOffset(0);
            return;
        }
        SetOffset(_offset + Speed * dt / 1000.0);
        if (_offset >= _maxOffset)
        {
            SetOffset(_maxOffset);
            _atEnd = true;
            _waitMs = EndPauseMs;
        }
    }

    void SetOffset(double offset)
    {
        _offset = offset;
        _shift.X = -offset;
    }
}
