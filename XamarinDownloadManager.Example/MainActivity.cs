using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.OS.Storage;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V4.Provider;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Java.IO;
using xdm;
using xdm.utilities;
using DownloadManager = xdm.DownloadManager;

namespace XamarinDownloadManager.Example
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity, StoragePermissionsHandler.ICallback
    {
        private StoragePermissionsHandler.StoragePermissionsDetail.Request _currentRequest;

        private TextView _lblFileName;
        private TextView _lblProgress;
        private Button _btnStartStop;
        private Button _btnCancel;
        private ProgressBar _progress;

        private int _previousDownloadId = -1;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            _progress = FindViewById<ProgressBar>(Resource.Id.prgStatus);
            _lblFileName = FindViewById<TextView>(Resource.Id.lblFileName);
            _lblProgress = FindViewById<TextView>(Resource.Id.lblProgress);
            _btnStartStop = FindViewById<Button>(Resource.Id.btnStart);
            _btnCancel = FindViewById<Button>(Resource.Id.btnCancel);

            _btnStartStop.Click += BtnStartStop_OnClick;

            var configuration = DownloadManagerConfiguration.Create(this, this);
            DownloadManager.Instance.Initialize(configuration);

            DownloadManager.Instance.OnDownloadStarted += DownloadManager_OnDownloadStarted;
            DownloadManager.Instance.OnDownloadStopped += DownloadManager_OnDownloadStopped;
            DownloadManager.Instance.OnDownloadProgressChanged += DownloadManager_OnDownloadProgressChanged;
            DownloadManager.Instance.OnDownloadErrorOccurred += DownloadManager_OnDownloadErrorOccurred;
            DownloadManager.Instance.OnDownloadCompleted += DownloadManager_OnDownloadCompleted;

            _btnCancel.Visibility = ViewStates.Gone;
        }

        private void DownloadManager_OnDownloadStarted(object sender, DownloadDetails downloadDetails, long fileSize)
        {
            RunOnUiThread(() =>
            {
                _lblFileName.Text = downloadDetails.FileName;
                _lblProgress.Text = "";
                _btnStartStop.Enabled = true;
                _btnCancel.Visibility = ViewStates.Visible;
                _progress.Indeterminate = false;
                _progress.Progress = 0;
                _btnStartStop.Text = "STOP";
            });
        }

        private void DownloadManager_OnDownloadStopped(object sender, DownloadDetails e)
        {
            RunOnUiThread(() =>
            {
                _btnStartStop.Text = "START";
                _btnStartStop.Enabled = true;
                _btnCancel.Visibility = ViewStates.Gone;
            });
        }

        private void DownloadManager_OnDownloadProgressChanged(object sender, DownloadDetails downloadDetails, long downloadedSize, long totalSize)
        {
            RunOnUiThread(() =>
            {
                _lblFileName.Text = downloadDetails.FileName;
                _lblProgress.Text = (int)downloadDetails.CurrentProgress.GetProgress() + "%";
                _progress.Progress = (int)downloadDetails.CurrentProgress.GetProgress();
                _progress.Indeterminate = false;
            });
        }

        private void DownloadManager_OnDownloadErrorOccurred(object sender, DownloadDetails downloadDetails, DownloadManager.Exception errorDetails)
        {
            RunOnUiThread(() =>
            {
                _btnStartStop.Enabled = true;
                _btnStartStop.Text = "START";
                _btnCancel.Visibility = ViewStates.Gone;
            });
        }

        private void DownloadManager_OnDownloadCompleted(object sender, DownloadDetails downloadDetails)
        {
            RunOnUiThread(() =>
            {
                _lblFileName.Text = downloadDetails.FileName;
                _lblProgress.Text = "100%";
                _progress.Progress = 100;

                _btnStartStop.Enabled = true;
                _btnStartStop.Text = "START";
                _btnCancel.Visibility = ViewStates.Gone;
            });
        }

        private void BtnStartStop_OnClick(object sender, EventArgs e)
        {
            if (_btnStartStop.Text == "START")
            {
                var url = "https://file-examples.com/wp-content/uploads/2017/04/file_example_MP4_1280_10MG.mp4";
                var fileName = "file_example_MP4_1280_10MG.mp4";
                var mimeType = "video/mp4";
                var downloadLocation = StorageUtils.GetAllStorageLocations(this)[1];

                _lblFileName.Text = fileName;
                _lblProgress.Text = "";
                _btnStartStop.Enabled = false;
                _progress.Indeterminate = true;

                _previousDownloadId = DownloadManager.Instance.Download(url, downloadLocation, fileName, mimeType);
            }
            else
            {
                DownloadManager.Instance.Pause(_previousDownloadId);
            }
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        public void OnStoragePermissionsRequested(StoragePermissionsHandler.StoragePermissionsDetail.Request request)
        {
            RunOnUiThread(() =>
            {
                _currentRequest?.RejectRequest();
                _currentRequest = request;

                var storageLocation = new File(request.StorageLocation);
                var storageManager = (StorageManager)GetSystemService(StorageService);
                var storageVolume = storageManager.GetStorageVolume(storageLocation);
                var intent = storageVolume?.CreateAccessIntent(null);

                if (intent != null) StartActivityForResult(intent, 10001);
                else _currentRequest.RejectRequest();
            });
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            if (requestCode == 10001 && _currentRequest != null)
            {
                if (resultCode == Result.Ok)
                {
                    ContentResolver.TakePersistableUriPermission(data.Data, ActivityFlags.GrantWriteUriPermission);

                    var documentFile = DocumentFile.FromTreeUri(this, data.Data);
                    _currentRequest.AcceptRequest(documentFile);
                }
                else
                {
                    _currentRequest.RejectRequest();
                }

                _currentRequest = null;
            }
        }
    }
}

