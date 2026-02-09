using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VibeShadowsocks.App.ViewModels;

namespace VibeShadowsocks.App.Views;

public sealed partial class RoutingPage : Page
{
    private readonly RoutingViewModel _viewModel;
    private bool _isLoaded;

    public RoutingPage()
    {
        InitializeComponent();
        _viewModel = App.GetService<RoutingViewModel>();
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

    private void OnProxyDomainKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _viewModel.AddProxyDomainCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDirectDomainKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _viewModel.AddDirectDomainCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRemoveProxyDomain(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string domain })
        {
            _viewModel.RemoveProxyDomainCommand.Execute(domain);
        }
    }

    private void OnRemoveDirectDomain(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string domain })
        {
            _viewModel.RemoveDirectDomainCommand.Execute(domain);
        }
    }
}
