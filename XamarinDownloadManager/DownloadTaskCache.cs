using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using AndroidX.DocumentFile.Provider;
using xdm.utilities;

namespace xdm
{
    class DocumentFileStream
    {
        private readonly Stream _stream;

        private DocumentFileStream(DownloadManagerConfiguration configuration, DocumentFile file)
        {
            _stream = CreateStream(configuration, file);
        }

        private Stream CreateStream(DownloadManagerConfiguration configuration, DocumentFile file)
        {
             // The file is appended thus no need to use an offset.
            return configuration.Context.ContentResolver.OpenOutputStream(file.Uri, "wa");
        }

        public void Write(byte[] data, int length)
        {
            _stream.Write(data, 0, length);
        }

        public void Close()
        {
            _stream.Flush();
            _stream.Close();
        }

        public static DocumentFileStream Create(DownloadManagerConfiguration configuration, DocumentFile file)
        {
            return new DocumentFileStream(configuration, file);
        }
    }

    internal class DownloadTaskCache
    {
        private static readonly object LockFile = new object();
        private static readonly object PermissionRequestSynchronizer = new object();

        private static DownloadTaskCache _instance;
        public static DownloadTaskCache Instance => _instance ?? (_instance = new DownloadTaskCache());

        private readonly ConcurrentDictionary<DownloadDetails, Thread> _downloadTasks;

        private DownloadTaskCache()
        {
            _downloadTasks = new ConcurrentDictionary<DownloadDetails, Thread>();
        }

        public void AddDownloadTask(DownloadManagerConfiguration configuration, DownloadDetails downloadDetails)
        {
            if (_downloadTasks.TryRemove(downloadDetails, out var downloadTask)) downloadTask.Abort();

            downloadTask = new Thread(OnDownloadTaskStarted);
            _downloadTasks.AddOrUpdate(downloadDetails, downloadTask, (details, thread) => downloadTask);

            downloadTask.Start(new object[] { configuration, downloadDetails, false });
        }

        public void ResumeDownloadTask(DownloadManagerConfiguration configuration, DownloadDetails downloadDetails)
        {
            if (_downloadTasks.TryRemove(downloadDetails, out var downloadTask)) downloadTask.Abort();

            downloadTask = new Thread(OnDownloadTaskStarted);
            _downloadTasks.AddOrUpdate(downloadDetails, downloadTask, (details, thread) => downloadTask);

            downloadTask.Start(new object[] { configuration, downloadDetails, true });
        }

        public void StopDownload(DownloadDetails downloadDetails)
        {
            if (_downloadTasks.TryRemove(downloadDetails, out var downloadTask))
            {
                downloadTask.Abort();
            }

            DownloadManager.Instance.TriggerDownloadCancelledEvent(downloadDetails);
        }

        private DocumentFile CreateFile(DownloadDetails details, DownloadDetails.RootDirectoryDetails root, bool useExistingFile)
        {
            lock (LockFile)
            {
                return useExistingFile ? details.FindOrCreateFile(root) : details.CreateNewFile(root);
            }
        }

        private DocumentFile RequestStoragePermission(DownloadManagerConfiguration configuration, string rootDirectory)
        {
            lock (PermissionRequestSynchronizer)
            {
                return configuration.StoragePermissionsHandler.RequestStoragePermission(rootDirectory);
            }
        }

        private void OnDownloadTaskStarted(Object objDetails)
        {
            if (!(objDetails is object[] objects) || objects.Length < 3)
            {
                DownloadManager.Instance.TriggerDownloadErrorOccurredEvent(null, new DownloadManager.Exception(DownloadManager.Exception.Type.InvalidConfiguration, "Invalid details provided."));
                return;
            }

            var configuration = objects[0] as DownloadManagerConfiguration;
            var downloadDetails = objects[1] as DownloadDetails;
            var useExistingFile = (bool)objects[2];

            if (configuration == null || downloadDetails == null)
            {
                DownloadManager.Instance.TriggerDownloadErrorOccurredEvent(null, new DownloadManager.Exception(DownloadManager.Exception.Type.InvalidConfiguration, "Invalid details provided."));
                return;
            }

            try
            {
                if (StorageUtils.IsInAnyStorageDevices(configuration.Context, downloadDetails.DownloadDirectory, out string rootDirectory) == false)
                {
                    throw new DownloadManager.Exception(DownloadManager.Exception.Type.FileNotFound, $"Could not locate the root directory for the download location, '{downloadDetails.DownloadDirectory}'.");
                }

                var rootDirectoryDocument = RequestStoragePermission(configuration, rootDirectory);
                var rootDetails = new DownloadDetails.RootDirectoryDetails(rootDirectory, rootDirectoryDocument);
                var downloadingFile = CreateFile(downloadDetails, rootDetails, useExistingFile);
                var downloadedSize = downloadingFile.Length();

                var stream = HttpUtilities.GetDownloadStream(downloadDetails.Url, downloadedSize, out var totalFileSize);

                downloadDetails.ContentUri = downloadingFile.Uri;
                downloadDetails.CurrentProgress.Update(downloadedSize, totalFileSize);

                DownloadManager.Instance.TriggerDownloadStartedEvent(downloadDetails, totalFileSize);

                if (StorageUtils.CheckSpaceAvailable(configuration.Context, rootDetails, totalFileSize - downloadedSize) == false)
                {
                    throw new DownloadManager.Exception(DownloadManager.Exception.Type.InsufficientSpace);
                }

                if (downloadedSize == totalFileSize)
                {
                    DownloadManager.Instance.TriggerDownloadCompletedEvent(downloadDetails);
                    return;
                }

                var fileStream = DocumentFileStream.Create(configuration, downloadingFile);
                var reportingProgress = 0;
                var reportingThreshold = 512 * 1024;

                while (stream.CanRead)
                {
                    byte[] data = new byte[8192];
                    var readLength = stream.Read(data, 0, data.Length);

                    if (readLength <= 0) break;

                    fileStream.Write(data, readLength);
                    downloadedSize += readLength;
                    reportingProgress += readLength;

                    downloadDetails.CurrentProgress.Update(downloadedSize, totalFileSize);

                    if (reportingProgress >= reportingThreshold)
                    {
                        reportingProgress = 0;
                        DownloadManager.Instance.TriggerDownloadProgressChangedEvent(downloadDetails, downloadedSize, totalFileSize);
                    }
                }

                try
                {
                    fileStream.Close();
                    stream.Close();
                }
                catch { /* IGNORED */ }

                downloadDetails.CurrentProgress.Update(downloadedSize, totalFileSize);
                DownloadManager.Instance.TriggerDownloadProgressChangedEvent(downloadDetails, downloadedSize, totalFileSize);

                if (downloadedSize == totalFileSize)
                    DownloadManager.Instance.TriggerDownloadCompletedEvent(downloadDetails);
                else
                    DownloadManager.Instance.TriggerDownloadErrorOccurredEvent(downloadDetails, new DownloadManager.Exception(DownloadManager.Exception.Type.StreamEndedBeforeCompletion, "The data stream ended before the entire file was downloaded"));
            }
            catch (ThreadAbortException)
            {
                DownloadManager.Instance.TriggerDownloadErrorOccurredEvent(downloadDetails, new DownloadManager.Exception(DownloadManager.Exception.Type.UserCancelled));
            }
            catch (ThreadInterruptedException)
            {
                DownloadManager.Instance.TriggerDownloadErrorOccurredEvent(downloadDetails, new DownloadManager.Exception(DownloadManager.Exception.Type.UserCancelled));
            }
            catch (Exception e)
            {
                DownloadManager.Instance.TriggerDownloadErrorOccurredEvent(downloadDetails, new DownloadManager.Exception(DownloadManager.Exception.Type.Unknown, e));
            }
        }
    }
}