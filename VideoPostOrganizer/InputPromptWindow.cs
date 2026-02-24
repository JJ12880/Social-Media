using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Interactivity;

namespace VideoPostOrganizer;

public sealed class InputPromptWindow : Window
{
    private readonly TextBox _input;

    public InputPromptWindow(string title, string message, string initialValue)
    {
        Title = title;
        Width = 420;
        Height = 180;

        _input = new TextBox { Text = initialValue };

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        new Button { Content = "Cancel", Width = 80 },
                        new Button { Content = "OK", Width = 80 }
                    }
                }
            }
        };

        var buttonPanel = (StackPanel)((StackPanel)Content).Children[2];
        ((Button)buttonPanel.Children[0]).Click += (_, _) => Close(null);
        ((Button)buttonPanel.Children[1]).Click += OnOk;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Close(_input.Text?.Trim());
    }
}
