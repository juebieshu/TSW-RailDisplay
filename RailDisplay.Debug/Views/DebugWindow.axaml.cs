using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using RailDisplay.Debug.ViewModels;

namespace RailDisplay.Debug.Views;

public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnDebugKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDebugKeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is DebugViewModel model && model.HandleKey(args.Key))
            args.Handled = true;
    }
}
