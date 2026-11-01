using Microsoft.UI.Xaml.Controls;

namespace Padmon.Controls;

/// <summary>Circle-and-crosshair plot of one stick, like the L STICK / R STICK graphs on hardwaretester.com.</summary>
public sealed partial class StickGraph : UserControl
{
    private const double Center = 65;
    private const double Radius = 60;

    public StickGraph()
    {
        InitializeComponent();
    }

    /// <summary>Plots a stick position; x and y run -1..1 with +y pointing down.</summary>
    public void SetPosition(double x, double y)
    {
        var px = Center + (Math.Clamp(x, -1.0, 1.0) * Radius);
        var py = Center + (Math.Clamp(y, -1.0, 1.0) * Radius);

        Needle.X2 = px;
        Needle.Y2 = py;
        Canvas.SetLeft(Dot, px - 5);
        Canvas.SetTop(Dot, py - 5);
    }
}
