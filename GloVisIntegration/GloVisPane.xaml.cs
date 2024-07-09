using System.Windows;
using System.Windows.Controls;


namespace GloVisIntegration
{
    /// <summary>
    /// Interaction logic for GloVisPaneView.xaml
    /// </summary>
    public partial class GloVisPaneView : UserControl
    {
        public GloVisPaneView()
        {
            InitializeComponent();

            Loaded += PaneLoaded;
        }

        private async void PaneLoaded(object sender, RoutedEventArgs e)
        {
            await webView.EnsureCoreWebView2Async();

            var viewModel = (GloVisPaneViewModel)this.DataContext;
            viewModel.SetWebViewCore(webView.CoreWebView2);
        }
    }
}
