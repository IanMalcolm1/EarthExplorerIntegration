using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using System.Net.Http;
using System.IO;
using System.Formats.Tar;
using ArcGIS.Desktop.Core.Geoprocessing;
using System.Text.RegularExpressions;
using ArcGIS.Desktop.Framework.Threading.Tasks;

namespace GloVisIntegration
{
    internal class EarthExplorerPaneViewModel : ViewStatePane
    {
        private const string _viewPaneID = "EarthExplorerPane";
        private const string _downloadFolderName = "EarthExplorerAddinDownloads";
        private string _downloadFolderPath;
        private const int _defaultBufferSize = 81920;

        private CoreWebView2 _webViewCore;
        private HttpClient _httpClient;

        private List<string> _validURLDomains = new List<string> { "https://landsatlook.usgs.gov/gen-bundle", "https://landsatlook.usgs.gov/data/collection", "https://dds.cr.usgs.gov/download" };
        private Regex _productNamePattern = new Regex(@"L[A-Z]\d\d_L\d[A-Z]{2}_\d+_\d{8}_\d{8}_\d\d_[A-Z\d]{2}");


        /// <summary>
        /// Consume the passed in CIMView. Call the base constructor to wire up the CIMView.
        /// </summary>
        public EarthExplorerPaneViewModel(CIMView view) : base(view)
        {
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Create a new instance of the pane.
        /// </summary>
        internal static EarthExplorerPaneViewModel Create()
        {
            var view = new CIMGenericView();
            view.ViewType = _viewPaneID;
            return FrameworkApplication.Panes.Create(_viewPaneID, new object[] { view }) as EarthExplorerPaneViewModel;
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


        private const string StartUri = "https://earthexplorer.usgs.gov/";
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

        private string _navInput = "https://earthexplorer.usgs.gov/";
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
            _webViewCore.NewWindowRequested += OpenNewWindowHandler;
        }


        private void EnsureDownloadFolderExists()
        {
            _downloadFolderPath = Path.Combine(Project.Current.HomeFolderPath, _downloadFolderName);
            Directory.CreateDirectory(_downloadFolderPath);
        }


        /// <summary>
        /// Handles attempt to open new window. No windows will be allowed to open, but valid download URLs will be
        /// intercepted and processed manually.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void OpenNewWindowHandler(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            var downloadUri = args.Uri;

            foreach (string domain in _validURLDomains)
            {
                if (downloadUri.StartsWith(domain))
                {
                    NotDownloadingLandsat = false;
                    string bandsFolder = await DownloadLandsat(downloadUri);
                    NotDownloadingLandsat = true;
                    PopulateAndOpenCompositeBandTool(bandsFolder);
                }
            }
        }


        /// <summary>
        /// Downloads then extracts the product bundle tar file.
        /// </summary>
        /// <param name="downloadUri"></param>
        /// <returns>Path to directory to which tar file was extracted</returns>
        public async Task<string> DownloadLandsat(string downloadUri)
        {
            EnsureDownloadFolderExists();


            //get initial response information
            var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);

            var contentLength = response.Content.Headers.ContentLength;
            var downloadSize = response.Content.Headers.ContentLength;
            var fileName = response.Content.Headers.ContentDisposition.FileName.Replace("\"", "");
            var downloadType = response.Content.Headers.ContentType.MediaType;

            String productName;
            var productNameMatch = _productNamePattern.Match(fileName);
            if (productNameMatch.Success)
            {
                productName = productNameMatch.Value;
            }
            else
            {
                return _downloadFolderPath;
            }


            //set up process dialog
            uint progressMax = 100;
            var progDialog = new ProgressDialog($"Downloading {fileName}...", "Cancel", progressMax, false);
            var progSource = new CancelableProgressorSource(progDialog);

            progSource.Max = progressMax;

            var productFolderPath = Path.Combine(_downloadFolderPath, productName);


            //do download (showing dialog)
            await QueuedTask.Run(async () =>
            {
                //create download directory
                var productFolderPath = Path.Combine(_downloadFolderPath, productName);
                var downloadFilePath = Path.Combine(productFolderPath, fileName);

                Directory.CreateDirectory(productFolderPath);


                //download to file
                using (Stream downloadStream = await response.Content.ReadAsStreamAsync())
                {
                    using (var fileStream = new FileStream(downloadFilePath, FileMode.Create))
                    {
                        int bytesRead = -1;
                        long bytesReadTotal = 0;
                        while (!progSource.Progressor.CancellationToken.IsCancellationRequested
                            && (bytesReadTotal <= contentLength || bytesRead != 0))
                        {
                            byte[] buffer = new byte[_defaultBufferSize];
                            bytesRead = await downloadStream.ReadAsync(buffer, 0, _defaultBufferSize);
                            if (bytesRead == 0)
                            {
                                break;
                            }
                            bytesReadTotal += bytesRead;
                            progSource.Progressor.Value = (uint)(bytesReadTotal * 100 / contentLength);
                            progSource.Progressor.Status = $"{progSource.Progressor.Value}% Completed";
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                        }
                    }
                }


                //if cancellation was requested, clean up and return
                if (progSource.Progressor.CancellationToken.IsCancellationRequested)
                {
                    progSource.Progressor.Status = "Canceled. Deleting download file...";
                    File.Delete(downloadFilePath);
                    return;
                }


                //extract if necessary
                if (fileName.Substring(fileName.Length - 3) == "tar")
                {
                    progSource.Progressor.Status = "Extracting tarball...";
                    await TarFile.ExtractToDirectoryAsync(downloadFilePath, productFolderPath, true);
                    File.Delete(downloadFilePath);
                }

            }, progSource.Progressor);


            

            return productFolderPath;
        }


        /// <summary>
        /// Opens Composite Band Tool and populates the bands parameter with bands found in a folder containing
        /// Landsat bands with default names.
        /// </summary>
        /// <param name="bandsFolder"></param>
        private void PopulateAndOpenCompositeBandTool(string bandsFolder)
        {
            Regex bandNamePattern = new Regex(@".*B(\d+)\.(TIF|tif)$");

            List<string> desiredBands = new List<string>();
            foreach (string filename in Directory.GetFiles(bandsFolder))
            {
                var matchInfo = bandNamePattern.Match(filename);
                if ( matchInfo.Success && int.Parse(matchInfo.Groups[1].Value) < 10 )
                {
                    desiredBands.Add(filename);
                }
            }


            var parameters = Geoprocessing.MakeValueArray(desiredBands);
            Geoprocessing.OpenToolDialog("management.CompositeBands", parameters);
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
    internal class EarthExplorerPane_OpenButton : Button
    {
        protected override void OnClick()
        {
            EarthExplorerPaneViewModel.Create();
        }
    }
}
