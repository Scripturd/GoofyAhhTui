using System.Drawing;
using System.Reflection;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

public sealed class CoalescingAnsiOutput : AnsiOutput
{
    private static readonly PropertyInfo? ScreenGetter =
        typeof (AnsiOutput).GetProperty ("AppScreenGetter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly StringBuilder _pending = new ();
    private bool _batching;
    private Point? _want;
    private Point? _emitted;

    public override void Write (IOutputBuffer buffer)
    {
        _batching = true;
        _want = null;
        _emitted = null;
        _pending.Clear ();

        try
        {
            base.Write (buffer);
        }
        finally
        {
            _batching = false;

            if (_pending.Length > 0)
            {
                Write (_pending);
                _pending.Clear ();
            }
        }
    }

    protected override void Write (StringBuilder output)
    {
        if (!_batching)
        {
            base.Write (output);

            return;
        }

        if (_want is { } w && w != _emitted)
        {
            _pending.Append (EscSeqUtils.CSI_SetCursorPosition (w.Y + 1 + ScreenOffsetY (), w.X + 1));
            _emitted = w;
        }

        _pending.Append (output);
    }

    protected override bool SetCursorPositionImpl (int col, int row)
    {
        if (!_batching)
        {
            return base.SetCursorPositionImpl (col, row);
        }

        _want = new Point (col, row);

        return true;
    }

    private int ScreenOffsetY ()
    {
        try
        {
            if (ScreenGetter?.GetValue (this) is Delegate d && d.DynamicInvoke () is { } screen)
            {
                return (int) (screen.GetType ().GetProperty ("Y")?.GetValue (screen) ?? 0);
            }
        }
        catch
        {
        }

        return 0;
    }
}

public static class FastApplication
{
    public static IApplication Create ()
    {
        try
        {
            Type impl = typeof (Application).Assembly.GetType ("Terminal.Gui.App.ApplicationImpl")
                        ?? throw new MissingMemberException ("ApplicationImpl");

            impl.GetMethod ("MarkInstanceBasedModelUsed", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke (null, null);

            ConstructorInfo ctor = impl.GetConstructor (
                                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                                       null,
                                       [typeof (IComponentFactory)],
                                       null)
                                   ?? throw new MissingMemberException ("ApplicationImpl(IComponentFactory)");

            var app = (IApplication) ctor.Invoke ([new AnsiComponentFactory (output: new CoalescingAnsiOutput ())]);

            typeof (Application).GetMethod ("RaiseInstanceCreated", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                                ?.Invoke (null, [app]);

            return app;
        }
        catch
        {
            return Application.Create ();
        }
    }
}