﻿using ArcGIS.Core.CIM;
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
using System.Linq;
using static ArcGIS.Desktop.Internal.Core.CancellableFileChecker;
using ArcGIS.Desktop.Framework.Threading.Tasks;

namespace GloVisIntegration
{
    internal class GloVisPaneViewModel : ViewStatePane
    {
        private const string _viewPaneID = "GloVisIntegration_GloVisPane";
        private const string _downloadFolderName = "EarthExplorerAddinDownloads";
        private string _downloadFolderPath;
        private const int _defaultBufferSize = 81920;

        private CoreWebView2 _webViewCore;
        private HttpClient _httpClient;

        private Regex _productNamePattern = new Regex(@"L[A-Z]\d\d_L\d[A-Z]{2}_\d+_\d{8}_\d{8}_\d\d_[A-Z\d]{2}");


        /// <summary>
        /// Consume the passed in CIMView. Call the base constructor to wire up the CIMView.
        /// </summary>
        public GloVisPaneViewModel(CIMView view) : base(view)
        {
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Create a new instance of the pane.
        /// </summary>
        internal static GloVisPaneViewModel Create()
        {
            var view = new CIMGenericView();
            view.ViewType = _viewPaneID;
            return FrameworkApplication.Panes.Create(_viewPaneID, new object[] { view }) as GloVisPaneViewModel;
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

        private async void OpenNewWindowHandler(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            var downloadUri = args.Uri;

            if (NotDownloadingLandsat && args.Uri.StartsWith("https://landsatlook.usgs.gov/gen-bundle"))
            {
                NotDownloadingLandsat = false;
                string bandsFolder = await DownloadLandsat(downloadUri);
                NotDownloadingLandsat = true;
                PopulateAndOpenCompositeBandTool(bandsFolder);
            }
            else if (NotDownloadingLandsat && args.Uri.StartsWith("https://landsatlook.usgs.gov/data/collection"))
            {
                NotDownloadingLandsat = false;
                string bandsFolder = await DownloadLandsat(downloadUri);
                NotDownloadingLandsat = true;
                PopulateAndOpenCompositeBandTool(bandsFolder);
            }
            else if (NotDownloadingLandsat && args.Uri.StartsWith("https://dds.cr.usgs.gov/download"))
            {
                NotDownloadingLandsat = false;
                string bandsFolder = await DownloadLandsat(downloadUri);
                NotDownloadingLandsat = true;
                PopulateAndOpenCompositeBandTool(bandsFolder);
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


            //Display progress dialog
            uint progressMax = 100;
            var progDialog = new ArcGIS.Desktop.Framework.Threading.Tasks.ProgressDialog($"Downloading {fileName}...", progressMax, false);
            var progSource = new ArcGIS.Desktop.Framework.Threading.Tasks.CancelableProgressorSource(progDialog);

            progSource.Max = progressMax;

            var productFolderPath = Path.Combine(_downloadFolderPath, productName);

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
                        while (bytesReadTotal >= contentLength || bytesRead != 0)
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


                //extract if necessary
                if (fileName.Substring(fileName.Length - 3) == "tar")
                {
                    await TarFile.ExtractToDirectoryAsync(downloadFilePath, productFolderPath, true);
                    File.Delete(downloadFilePath);
                }
            }, progSource.Progressor);


            

            return productFolderPath;
        }


        private Task DownloadProductBundle(string downloadUri, string downloadPath)
        {
            return Task.Run(async () =>
            {
                var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);

                var contentLength = response.Content.Headers.ContentLength;
                var downloadSize = response.Content.Headers.ContentLength;
                var fileName = response.Content.Headers.ContentDisposition.FileName.Replace("\"", "");
                var downloadType = response.Content.Headers.ContentType.MediaType;

                using (Stream downloadStream = await response.Content.ReadAsStreamAsync())
                {
                    using (var fileStream = new FileStream(downloadPath, FileMode.Create))
                    {
                        await downloadStream.CopyToAsync(fileStream);
                    }
                }
            });
        }

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
    internal class GloVisPane_OpenButton : Button
    {
        protected override void OnClick()
        {
            GloVisPaneViewModel.Create();
        }
    }
}
