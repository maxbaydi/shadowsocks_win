using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeShadowsocks.App.ViewModels;

namespace VibeShadowsocks.App.Views;

public sealed partial class ServersPage : Page
{
    private readonly ServersViewModel _viewModel;
    private bool _isLoaded;

    public ServersPage()
    {
        InitializeComponent();
        _viewModel = App.GetService<ServersViewModel>();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await _viewModel.LoadAsync();
    }
}
