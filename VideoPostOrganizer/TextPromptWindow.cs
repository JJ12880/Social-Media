using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace VideoPostOrganizer;

public sealed class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, bool okOnly)
    {
        Title = title;
        Width = 420;
        Height = 160;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        if (!okOnly)
        {
            var cancelButton = new Button { Content = "Cancel", Width = 80 };
            cancelButton.Click += (_, _) => Close(false);
            buttons.Children.Add(cancelButton);
        }

        var okButton = new Button { Content = "OK", Width = 80 };
        okButton.Click += (_, _) => Close(true);
        buttons.Children.Add(okButton);

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
    }
}
