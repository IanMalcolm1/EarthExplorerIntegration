using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.KnowledgeGraph;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GloVisIntegration
{
    internal class LidarPaneViewModel : ViewStatePane
    {
        private const string _viewPaneID = "GloVisIntegration_LidarPane";
        private const string _downloadFolderName = "LidarExplorerAddinDownloads";
        private string _downloadFolderPath;
        private const int _defaultBufferSize = 81920;

        private CoreWebView2 _webViewCore;
        private HttpClient _httpClient;

        private List<string> _validURLDomains = new List<string> { "https://landsatlook.usgs.gov/gen-bundle", "https://landsatlook.usgs.gov/data/collection", "https://dds.cr.usgs.gov/download" };
        private Regex _productNamePattern = new Regex(@"L[A-Z]\d\d_L\d[A-Z]{2}_\d+_\d{8}_\d{8}_\d\d_[A-Z\d]{2}");

        /// <summary>
        /// Consume the passed in CIMView. Call the base constructor to wire up the CIMView.
        /// </summary>
        public LidarPaneViewModel(CIMView view)
          : base(view) { }

        /// <summary>
        /// Create a new instance of the pane.
        /// </summary>
        internal static LidarPaneViewModel Create()
        {
            var view = new CIMGenericView();
            view.ViewType = _viewPaneID;
            return FrameworkApplication.Panes.Create(_viewPaneID, new object[] { view }) as LidarPaneViewModel;
        }


        private bool _notDownloadingLandsat = true;
        public bool NotDownloadingLandsat
        {
            get => _notDownloadingLandsat;
            set
            {
                SetProperty(ref _notDownloadingLandsat, value);
            }
        }


        private const string StartUri = "https://apps.nationalmap.gov/lidar-explorer/#/";
        private Uri _sourceUri = new Uri(StartUri);
        /// <summary>
        /// SourceUri is used to interface with the WebViewBrowser control
        /// </summary>
        public Uri SourceUri
        {
            get { return _sourceUri; }
            set
            {
                SetProperty(ref _sourceUri, value, () => SourceUri);
                if (_sourceUri.AbsoluteUri != _navInput)
                {
                    _navInput = _sourceUri.AbsoluteUri;
                    NotifyPropertyChanged(() => NavInput);
                }
            }
        }

        private string _navInput = "https://apps.nationalmap.gov/lidar-explorer/#/";
        /// <summary>
        /// NavInput is used to provide a text input field for navigation in the UI
        /// </summary>
        public string NavInput
        {
            get { return _navInput; }
            set
            {
                SetProperty(ref _navInput, value, () => NavInput);
            }
        }



        public void SetWebViewCore(CoreWebView2 viewCore)
        {
            _webViewCore = viewCore;

            EnsureDownloadFolderExists();
            _webViewCore.Profile.DefaultDownloadFolderPath = _downloadFolderPath;
            _webViewCore.DownloadStarting += DownloadStartingHandler;
        }


        private void EnsureDownloadFolderExists()
        {
            _downloadFolderPath = Path.Combine(Project.Current.HomeFolderPath, _downloadFolderName);
            Directory.CreateDirectory(_downloadFolderPath);
        }


        private void DownloadStartingHandler(object sender, CoreWebView2DownloadStartingEventArgs args)
        {
            EnsureDownloadFolderExists();

            args.ResultFilePath = Path.Combine(_downloadFolderPath, Path.GetFileName(args.ResultFilePath));
            args.DownloadOperation.StateChanged += DownloadChangedHandler;
        }

        private async void DownloadChangedHandler(object sender, Object e)
        {
            var downloadOp = sender as CoreWebView2DownloadOperation;
            if (downloadOp.State == CoreWebView2DownloadState.Completed && downloadOp.ResultFilePath.EndsWith("laz"))
            {
                //remove handler lest it be fired multiple times
                downloadOp.StateChanged -= DownloadChangedHandler;

                //get path to laszip executable
                string laszipPath = Path.Combine( AddinAssemblyLocation(), "Libs", "laszip.exe");

                //basically copy-pasted from stack overflow (save for the actual command)
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/C {laszipPath} -i {downloadOp.ResultFilePath} -o {Path.ChangeExtension(downloadOp.ResultFilePath, "las")}";
                process.StartInfo = startInfo;
                if (process.Start())
                {
                    await process.WaitForExitAsync();
                }

                //delete laz file after extraction
                File.Delete(downloadOp.ResultFilePath);
            }
        }

        //Copied from the documentation, lol
        public static string AddinAssemblyLocation()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            return Path.GetDirectoryName(
                              Uri.UnescapeDataString(
                                      new Uri(asm.CodeBase).LocalPath));
        }

        #region Pane Overrides

        /// <summary>
        /// Must be overridden in child classes used to persist the state of the view to the CIM.
        /// </summary>
        public override CIMView ViewState
        {
            get
            {
                _cimView.InstanceID = (int)InstanceID;
                return _cimView;
            }
        }

        /// <summary>
        /// Called when the pane is initialized.
        /// </summary>
        protected async override Task InitializeAsync()
        {
            await base.InitializeAsync();
        }

        /// <summary>
        /// Called when the pane is uninitialized.
        /// </summary>
        protected async override Task UninitializeAsync()
        {
            await base.UninitializeAsync();
        }

        #endregion Pane Overrides
    }

    /// <summary>
    /// Button implementation to create a new instance of the pane and activate it.
    /// </summary>
    internal class LidarPane_OpenButton : Button
    {
        protected override void OnClick()
        {
            LidarPaneViewModel.Create();
        }
    }
}
