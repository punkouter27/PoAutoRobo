using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace PoAutoRobo.App;

/// <summary>
/// Small questions the pages ask of a value: is it there, is it this one. Asked in the page with a function binding,
/// so the view models need no second property that only repeats a first one as a yes or no. The value asked about
/// must never be null: a page is not told when a value becomes null, and would go on showing the answer it had.
/// </summary>
public static class Ui
{
    public static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool on) => Show(!on);

    /// <summary>Shown only when both are so: a control that is advanced and also needs something else, such as a film.</summary>
    public static Visibility ShowBoth(bool first, bool second) => Show(first && second);

    /// <summary>Shown when either is so: an advanced control, shown too while what it sets is not the standard value, so nothing hidden changes what is made.</summary>
    public static Visibility ShowEither(bool first, bool second) => Show(first || second);

    /// <summary>The opposite, for a control that may be used only while something is not so.</summary>
    public static bool Not(bool on) => !on;

    public static Visibility ShowText(string? text) => Show(!string.IsNullOrEmpty(text));

    public static Visibility HideText(string? text) => Show(string.IsNullOrEmpty(text));

    /// <summary>Shown when a choice is on a particular entry: a step, a tab.</summary>
    public static Visibility ShowIf(int value, int wanted) => Show(value == wanted);

    /// <summary>The look of the one button on a page that is the thing to do next; a plain button when it is not that now.</summary>
    public static Style Primary(bool on) => (Style)Application.Current.Resources[on ? "AccentButtonStyle" : "DefaultButtonStyle"];

    // The bar under the header: the one item to do next stands out, and what is done or to come stays quieter.

    public static Windows.UI.Text.FontWeight Strong(bool next) => next ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public static double Faint(bool next) => next ? 1 : 0.75;

    /// <summary>The accent colour for an item done or to do next; the quieter text colour for the rest.</summary>
    public static Brush Lit(bool done, bool next) => (Brush)Application.Current.Resources[done || next ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

    // ---- Movement shared by the pages ----

    /// <summary>Windows' own switch for showing animation, read once. Every movement in the app follows it.</summary>
    public static bool Animated { get; } = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Something that has just appeared grows the last little way to its size and settles with a slight bounce.</summary>
    public static void PopIn(UIElement element)
    {
        if (!Animated) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0);
        visual.Scale = new Vector3(0.94f);
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.6f;
        spring.Period = TimeSpan.FromMilliseconds(60);
        visual.StartAnimation("Scale", spring);
    }
}
