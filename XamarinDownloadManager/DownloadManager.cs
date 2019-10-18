using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using xdm.utilities;
using DocumentFile = Android.Support.V4.Provider.DocumentFile;

namespace xdm
{
    public class DownloadManagerConfiguration
    {
        public readonly Context Context;
        public readonly StoragePermissionsHandler StoragePermissionsHandler;

        public bool IsNotificationsEnabled { get; internal set; }
        public NotificationConfiguration NotificationSettings { get; internal set; }

        private DownloadManagerConfiguration(Context context, StoragePermissionsHandler.ICallback storagePermissionsHandler)
        {
            Context = context;
            StoragePermissionsHandler = new StoragePermissionsHandler(storagePermissionsHandler);
        }

        public DownloadManagerConfiguration EnableNotifications(NotificationConfiguration notificationSettings)
        {
            IsNotificationsEnabled = true;
            NotificationSettings = notificationSettings.SetContext(Context);

            return this;
        }

        public DownloadManagerConfiguration DisableNotifications()
        {
            IsNotificationsEnabled = false;
            NotificationSettings = null;

            return this;
        }

        public static DownloadManagerConfiguration Create(Context context, StoragePermissionsHandler.ICallback storagePermissionsHandler)
        {
            return new DownloadManagerConfiguration(context, storagePermissionsHandler);
        }
    }

    public class StoragePermissionsHandler
    {
        public class StoragePermissionsDetail
        {
            public readonly string StorageLocation;
            private readonly TaskCompletionSource<DocumentFile> _request;

            public class Request
            {
                public readonly string StorageLocation;
                private readonly TaskCompletionSource<DocumentFile> _request;

                internal Request(string storageLocation, TaskCompletionSource<DocumentFile> request)
                {
                    StorageLocation = storageLocation;
                    _request = request;
                }

                public void AcceptRequest(DocumentFile documentFile)
                {
                    _request.TrySetResult(documentFile);
                }

                public void RejectRequest()
                {
                    _request.TrySetCanceled();
                }
            }

            public StoragePermissionsDetail(string storageLocation)
            {
                StorageLocation = storageLocation;
                _request = new TaskCompletionSource<DocumentFile>();
            }

            public async Task<DocumentFile> WaitForResult()
            {
                var result = await _request.Task;
                return result;
            }

            public Request CreateRequest()
            {
                return new Request(StorageLocation, _request);
            }
        }

        private readonly ICallback _permissionRequestCallback;

        public StoragePermissionsHandler(ICallback requestRequestCallback)
        {
            _permissionRequestCallback = requestRequestCallback;
        }

        public DocumentFile RequestStoragePermission(string storageLocation)
        {
            var details = new StoragePermissionsDetail(storageLocation);
            _permissionRequestCallback.OnStoragePermissionsRequested(details.CreateRequest());

            var task = details.WaitForResult();
            return task.Result;
        }

        public interface ICallback
        {
            void OnStoragePermissionsRequested(StoragePermissionsDetail.Request request);
        }
    }

    public class DownloadManager
    {
        public class Exception : System.Exception
        {
            public enum Type
            {
                InvalidConfiguration,
                FileNotFound,
                InsufficientSpace,
                UserCancelled,
                StreamEndedBeforeCompletion,
                Unknown
            }

            public readonly Type ReasonType;

            public Exception(Type type, System.Exception innerException) : base(null, innerException)
            {
                ReasonType = type;
            }

            public Exception(Type type, string message = null, System.Exception innerException = null) : base(message, innerException)
            {
                ReasonType = type;
            }
        }

        private static DownloadManager _instance;
        public static DownloadManager Instance => _instance ?? (_instance = new DownloadManager());

        public delegate void ErrorOccurredEventHandler(object sender, DownloadDetails downloadDetails, Exception errorDetails);
        public delegate void DownloadStartedEventHandler(object sender, DownloadDetails downloadDetails, long fileSize);
        public delegate void ProgressChangedEventHandler(object sender, DownloadDetails downloadDetails, long downloadedSize, long totalSize);

        public event EventHandler<DownloadDetails> OnDownloadAdded;
        public event DownloadStartedEventHandler OnDownloadStarted;
        public event EventHandler<DownloadDetails> OnDownloadCancelled;
        public event ErrorOccurredEventHandler OnDownloadErrorOccurred;
        public event ProgressChangedEventHandler OnDownloadProgressChanged;
        public event EventHandler<DownloadDetails> OnDownloadCompleted;
        public event EventHandler<DownloadDetails> OnDownloadStopped;
        public event EventHandler<DownloadDetails> OnDownloadResumed;
        public event EventHandler<DownloadDetails> OnDownloadRemoved;
        public event EventHandler<System.Exception> OnOpenRequestError; 

        private DownloadManagerConfiguration _configuration;

        private DownloadManager() { }

        public void Initialize(DownloadManagerConfiguration configuration)
        {
            _configuration = configuration;

            NotificationChannelManager.Instance.Configure(_configuration.Context);
            DownloadDetailsCache.Instance.Configure(configuration);
            DownloadDetailsCache.Instance.Restore();
        }

        public int Download(string url, string downloadLocation, string fileName, string mimeType)
        {
            CheckWhetherDownloadManagerIsConfigured();

            var downloadDetails = DownloadDetailsCache.Instance.AddDownloadDetails(url, downloadLocation, fileName, mimeType);
            downloadDetails.UpdateStatus(DownloadDetails.Status.Pending);

            DownloadTaskCache.Instance.AddDownloadTask(_configuration, downloadDetails);

            OnDownloadAdded?.Invoke(this, downloadDetails);

            return downloadDetails.DownloadId ?? 0;
        }

        public void Pause(int downloadId)
        {
            CheckWhetherDownloadManagerIsConfigured();

            var downloadDetails = DownloadDetailsCache.Instance.FindDownloadDetails(downloadId);
            if (downloadDetails != null)
            {
                downloadDetails.UpdateStatus(DownloadDetails.Status.Paused);
                DownloadTaskCache.Instance.StopDownload(downloadDetails);

                OnDownloadStopped?.Invoke(this, downloadDetails);
            }
        }

        public void Resume(int downloadId)
        {
            CheckWhetherDownloadManagerIsConfigured();

            var downloadDetails = DownloadDetailsCache.Instance.FindDownloadDetails(downloadId);
            if (downloadDetails != null)
            {
                downloadDetails.UpdateStatus(DownloadDetails.Status.Pending);
                DownloadTaskCache.Instance.ResumeDownloadTask(_configuration, downloadDetails);

                OnDownloadResumed?.Invoke(this, downloadDetails);
            }
        }

        public void Remove(int downloadId)
        {
            CheckWhetherDownloadManagerIsConfigured();

            var downloadDetails = DownloadDetailsCache.Instance.FindDownloadDetails(downloadId);
            if (downloadDetails != null)
            {
                DownloadTaskCache.Instance.StopDownload(downloadDetails);
                DownloadDetailsCache.Instance.RemoveDownloadDetails(downloadId);

                OnDownloadRemoved?.Invoke(this, downloadDetails);
            }
        }

        public Dictionary<int, DownloadDetails> GetAllDownloads()
        {
            CheckWhetherDownloadManagerIsConfigured();
            return DownloadDetailsCache.Instance.GetAllDownloads();
        }

        public void OpenFile(DownloadDetails details)
        {
            CheckWhetherDownloadManagerIsConfigured();

            var task = new Task<DocumentFile>(() =>
            {
                if (StorageUtils.IsInAnyStorageDevices(_configuration.Context, details.DownloadDirectory, out string rootDirectory) == false)
                {
                    throw new System.Exception($"Could not locate the root directory for the download location, '{details.DownloadDirectory}'.");
                }

                var rootDirectoryDocument = _configuration.StoragePermissionsHandler.RequestStoragePermission(rootDirectory);
                var rootDetails = new DownloadDetails.RootDirectoryDetails(rootDirectory, rootDirectoryDocument);

                return details.FindFile(rootDetails);
            });

            task.ContinueWith(result =>
            {
                try
                {
                    var foundFile = result.Result;
                    if (foundFile == null) throw new FileNotFoundException($"The file '{details.FileName}' could not be located. Perhaps it was moved or deleted.");

                    TriggerOpenFileIntent(_configuration.Context, foundFile.Uri, details.MimeType);
                }
                catch (ActivityNotFoundException ex) { OnOpenRequestError?.Invoke(this, ex); }
                catch (FileNotFoundException ex) { OnOpenRequestError?.Invoke(this, ex); }
                catch { /* IGNORED */ }
            });

            task.Start();
        }

        private static void TriggerOpenFileIntent(Context context, Android.Net.Uri file, string mimeType)
        {
            var intent = new Intent();

            intent.SetAction(Intent.ActionView);
            intent.SetDataAndType(file, mimeType);
            intent.SetFlags(ActivityFlags.GrantReadUriPermission);

            context.StartActivity(intent);
        }

        private void CheckWhetherDownloadManagerIsConfigured()
        {
            if (_configuration == null) throw new System.Exception("The DownloadManager should be configured by using the 'Configure' method.");
        }

        #region "Internal Callback Triggers"
        internal void TriggerDownloadStartedEvent(DownloadDetails details, long fileSize)
        {
            details.UpdateStatus(DownloadDetails.Status.Downloading);
            OnDownloadStarted?.Invoke(this, details, fileSize);
        }

        internal void TriggerDownloadCancelledEvent(DownloadDetails details)
        {
            details.UpdateStatus(DownloadDetails.Status.Paused);
            OnDownloadCancelled?.Invoke(this, details);
        }

        internal void TriggerDownloadErrorOccurredEvent(DownloadDetails details, Exception errorDetails)
        {
            details.UpdateStatus(DownloadDetails.Status.Error);
            OnDownloadErrorOccurred?.Invoke(this, details, errorDetails);
        }

        internal void TriggerDownloadProgressChangedEvent(DownloadDetails details, long downloadedSize, long totalSize)
        {
            OnDownloadProgressChanged?.Invoke(this, details, downloadedSize, totalSize);
        }

        internal void TriggerDownloadCompletedEvent(DownloadDetails details)
        {
            details.UpdateStatus(DownloadDetails.Status.Downloaded);
            OnDownloadCompleted?.Invoke(this, details);
        }
        #endregion
    }
}
