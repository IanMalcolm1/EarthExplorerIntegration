using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;


namespace GloVisIntegration
{
    /// <summary>
    /// Interaction logic for LidarPaneView.xaml
    /// </summary>
    public partial class LidarPaneView : UserControl
    {
        public LidarPaneView()
        {
            InitializeComponent();

            Loaded += PaneLoaded;
        }

        private async void PaneLoaded(object sender, RoutedEventArgs e)
        {
            await webView.EnsureCoreWebView2Async();

            var viewModel = (LidarPaneViewModel)this.DataContext;
            viewModel.SetWebViewCore(webView.CoreWebView2);
        }
    }
}
