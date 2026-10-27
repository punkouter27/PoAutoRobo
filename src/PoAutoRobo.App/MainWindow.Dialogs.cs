using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PoAutoRobo.App;

// The questions the view model asks the user, and Settings, opened from the header into the side panel.
public sealed partial class MainWindow
{
    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Opened += (opened, _) => Ui.PopIn(opened.Content as UIElement ?? opened);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // A job that costs money: asked as any other, with the month's spending against the budget said under it when there is one.
    private Task<bool> ConfirmSpendAsync(string title, string message, string action, decimal dollars)
    {
        var (spent, budget) = ViewModel.MonthSpend();
        var adds = dollars > 0 ? $"; this adds about {SpendLog.Money(dollars)}" : "";
        var month = budget > 0 ? $"\n\nThis month so far: {SpendLog.Money(spent)} of {SpendLog.Money(budget)}{adds}." : "";
        return ConfirmAsync(title, message + month, action);
    }

    private async Task<string?> AskAsync(string title, string current)
    {
        var box = new TextBox { Text = current, SelectionStart = 0, SelectionLength = current.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        // Nothing to save while the box is blank, and the button says so by being unavailable.
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;
        dialog.IsPrimaryButtonEnabled = current.Trim().Length > 0;
        dialog.Opened += (opened, _) => Ui.PopIn(opened.Content as UIElement ?? opened);
        return await dialog.ShowAsync() == ContentDialogResult.Primary && box.Text.Trim() is { Length: > 0 } text ? text : null;
    }

    // Settings is long and asks nothing that must be answered, so it opens in the panel at the right, beside the page,
    // not as a dialog over it. A question about money or publishing stays a dialog: it must be answered before going on.
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        SidePanel.Pane = new Views.SettingsPanel(ViewModel, () => SidePanel.IsPaneOpen = false);
        SidePanel.Visibility = Visibility.Visible;
        SidePanel.IsPaneOpen = true;
    }

    // Emptied once put away, so the panel stops listening and opens fresh, with the spending worked out again; and
    // hidden, so nothing of it can stand between the pointer and the page.
    private void OnSidePanelClosed(SplitView sender, object args)
    {
        SidePanel.Pane = null;
        SidePanel.Visibility = Visibility.Collapsed;
    }
}
