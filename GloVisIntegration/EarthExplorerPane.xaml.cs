using System.Windows;
using System.Windows.Controls;


namespace GloVisIntegration
{
    /// <summary>
    /// Interaction logic for GloVisPaneView.xaml
    /// </summary>
    public partial class EarthExplorerPaneView : UserControl
    {
        public EarthExplorerPaneView()
        {
            InitializeComponent();

            Loaded += PaneLoaded;
        }

        private async void PaneLoaded(object sender, RoutedEventArgs e)
        {
            await webView.EnsureCoreWebView2Async();

            var viewModel = (EarthExplorerPaneViewModel)this.DataContext;
            viewModel.SetWebViewCore(webView.CoreWebView2);
        }
    }
}
