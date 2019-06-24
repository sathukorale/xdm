using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Android.Preferences;
using Android.Support.V4.Provider;
using SQLite;
using xdm.utilities;
using Uri = Android.Net.Uri;

namespace xdm
{
    public class DownloadDetails
    {
        public class Progress
        {
            public long DownloadedSize { get; internal set; }
            public long TotalSize { get; internal set; }

            public Progress()
            {
                Update(0, 0);
            }

            public Progress(long downloadedSize, long totalSize)
            {
                Update(downloadedSize, totalSize);
            }

            public void Update(long downloadedSize, long totalSize)
            {
                DownloadedSize = downloadedSize;
                TotalSize = totalSize;
            }

            public void Update(Progress currentProgress)
            {
                DownloadedSize = currentProgress.DownloadedSize;
                TotalSize = currentProgress.TotalSize;
            }

            public double GetProgress()
            {
                if (TotalSize == 0) return 0;
                return (DownloadedSize * 100.0) / TotalSize;
            }
        }

        public enum Status
        {
            Pending = 0,
            Paused,
            Downloading,
            Downloaded,
            Error
        }

        [PrimaryKey, AutoIncrement]
        public int? DownloadId { get; set; }
        public long AddedTime { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string DownloadDirectory { get; set; }
        public string MimeType { get; set; }
        public int DownloadStatus { get; set; }

        [Ignore]
        public Progress CurrentProgress { get; internal set; }

        [Ignore]
        public Uri ContentUri { get; set; }

        public DownloadDetails()
        {
            DownloadStatus = (int)Status.Pending;
            CurrentProgress = new Progress();
        }

        public DownloadDetails(int downloadId, string url, string fileName, string downloadDirectory, string mimeType)
        {
            DownloadId = downloadId;
            AddedTime = DateTime.Now.Ticks;
            Url = url;
            FileName = fileName.Trim();
            DownloadDirectory = downloadDirectory;
            MimeType = mimeType;
            DownloadStatus = (int)Status.Pending;
            CurrentProgress = new Progress();
        }

        public override int GetHashCode()
        {
            return DownloadId ?? 0;
        }

        public override bool Equals(object obj)
        {
            if (obj is DownloadDetails given)
            {
                return GetHashCode() == given.GetHashCode();
            }

            return false;
        }

        public DocumentFile CreateFile(RootDirectoryDetails root)
        {
            var parentDirectory = FindOrCreateParentDirectory(root);
            if (parentDirectory == null) return null;

            var file = parentDirectory.FindFile(FileName);
            if (file == null || file.IsFile == false)
                file = parentDirectory.CreateFile(MimeType, FileName);

            FileName = file.Name;

            return file;
        }

        private DocumentFile FindOrCreateParentDirectory(RootDirectoryDetails root)
        {
            var relativePath = GetRelativePath(root.RootDirectory, DownloadDirectory);
            var segments = relativePath.Split('/').Where(i => string.IsNullOrWhiteSpace(i) == false).ToArray();
            var parentDirectory = root.DocumentFile;

            foreach (var segment in segments)
            {
                var tmpParentDirectory = parentDirectory.FindFile(segment);
                if (tmpParentDirectory == null || tmpParentDirectory.IsDirectory == false) tmpParentDirectory = parentDirectory.CreateDirectory(segment);

                parentDirectory = tmpParentDirectory;
            }

            return parentDirectory;
        }

        public DocumentFile FindDirectory(RootDirectoryDetails root)
        {
            var relativePath = GetRelativePath(root.RootDirectory, DownloadDirectory);
            var segments = relativePath.Split('/').Where(i => string.IsNullOrWhiteSpace(i) == false).ToArray();
            var parentDirectory = root.DocumentFile;

            foreach (var segment in segments)
            {
                parentDirectory = parentDirectory.FindFile(segment);
                if (parentDirectory == null || parentDirectory.IsDirectory == false) return null;
            }

            return parentDirectory;
        }

        public DocumentFile FindFile(RootDirectoryDetails root)
        {
            var parentDirectory = FindDirectory(root);
            return parentDirectory?.FindFile(FileName);
        }

        public bool DoesFileExist(RootDirectoryDetails root, out DocumentFile foundFile)
        {
            return (foundFile = FindFile(root)) != null;
        }

        public DocumentFile FindOrCreateFile(RootDirectoryDetails root)
        {
            if (DoesFileExist(root, out var file) == false) file = CreateFile(root);
            return file;
        }

        public DocumentFile CreateNewFile(RootDirectoryDetails root)
        {
            var parentDirectory = FindOrCreateParentDirectory(root);
            if (parentDirectory.FindFile(FileName) == null) return CreateFile(root);

            for (var i = 1; i <= 10000; i++)
            {
                var modifierSegment = $"({i})";
                var fileName = FileName;

                if (fileName.Contains("."))
                    fileName = fileName.Substring(0, fileName.LastIndexOf(".", StringComparison.CurrentCulture)) + modifierSegment + fileName.Substring(fileName.LastIndexOf(".", StringComparison.CurrentCulture));
                else
                    fileName = fileName + modifierSegment;

                var existingFile = parentDirectory.FindFile(fileName);
                if (existingFile == null || existingFile.IsFile == false)
                {
                    var file = parentDirectory.CreateFile(MimeType, fileName);
                    FileName = file.Name;
                    return file;
                }
            }

            return null;
        }

        private string GetRelativePath(string rootDirectory, string fullPath)
        {
            rootDirectory = GetNormalizedPath(rootDirectory);
            fullPath = GetNormalizedPath(fullPath);

            return rootDirectory.Length == fullPath.Length ? "" : fullPath.Substring(rootDirectory.Length + 1);
        }

        private string GetNormalizedPath(string path)
        {
            path = path.Trim();
            if (path.EndsWith("/")) path = path.Substring(0, path.Length - 1);

            return path;
        }

        public void Update(DownloadDetails downloadDetails)
        {
            DownloadStatus = downloadDetails.DownloadStatus;
            ContentUri = ContentUri ?? downloadDetails.ContentUri;

            if (DownloadStatus == (int)Status.Downloaded) CurrentProgress.Update(0, 0);

            CurrentProgress.Update(downloadDetails.CurrentProgress);
        }

        public void UpdateStatus(Status status)
        {
            DownloadStatus = (int)status;
            if (status == Status.Downloaded) CurrentProgress.Update(0, 0);

            DownloadDetailsCache.Instance.UpdateDownloadDetails(this);
        }

        public class RootDirectoryDetails
        {
            public readonly string RootDirectory;
            public readonly DocumentFile DocumentFile;

            public RootDirectoryDetails(string rootDirectory, DocumentFile documentFile)
            {
                RootDirectory = rootDirectory;
                DocumentFile = documentFile;
            }
        }
    }

    internal class DownloadDetailsCache
    {
        private static DownloadDetailsCache _instance;
        internal static DownloadDetailsCache Instance => _instance ?? (_instance = new DownloadDetailsCache());

        private const string PreferencesLastDownloadId = "downloadmanager.preferences.lastdownloadid";
        private ConcurrentDictionary<int, DownloadDetails> _downloadDetails = new ConcurrentDictionary<int, DownloadDetails>();

        private readonly object _lockLastDownloadId = new object();
        private int _lastDownloadId = 0;
        private DownloadManagerConfiguration _configuration = null;

        private DownloadDetailsCache() { }

        internal void Configure(DownloadManagerConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Restore()
        {
            _downloadDetails = new ConcurrentDictionary<int, DownloadDetails>();
            var connection = DatabaseConnectionManager.GetConnection();

            connection.CreateTable<DownloadDetails>(CreateFlags.ImplicitPK | CreateFlags.AutoIncPK);

            var downloadDetails = connection.Query<DownloadDetails>("SELECT * FROM DownloadDetails");
            foreach (var downloadDetail in downloadDetails)
            {
                downloadDetail.DownloadStatus = (int) GetInitialDownloadStatus((DownloadDetails.Status)downloadDetail.DownloadStatus);
                if (downloadDetail.DownloadId == null) continue;

                _downloadDetails.AddOrUpdate(downloadDetail.DownloadId ?? 0, downloadDetail, (previous, current) => downloadDetail);
            }

            RestoreLastDownloadId();
        }

        private void RestoreLastDownloadId()
        {
            CheckConfigurationDone();

            var preferences = PreferenceManager.GetDefaultSharedPreferences(_configuration.Context);
            var lastDownloadId = preferences.GetInt(PreferencesLastDownloadId, -1);

            if (lastDownloadId >= 0)
            {
                _lastDownloadId = lastDownloadId;
            }
            else
            {
                var predictedLastDownloadId = 0;
                if (_downloadDetails.Any()) predictedLastDownloadId = _downloadDetails.Max(i => i.Key);

                predictedLastDownloadId = Math.Max(0, predictedLastDownloadId);
                UpdateLastDownloadId(predictedLastDownloadId);
            }
        }

        private DownloadDetails.Status GetInitialDownloadStatus(DownloadDetails.Status status)
        {
            switch (status)
            {
                case DownloadDetails.Status.Downloading: return DownloadDetails.Status.Paused;
                case DownloadDetails.Status.Pending: return DownloadDetails.Status.Paused;
            }

            return status;
        }

        private void UpdateLastDownloadId(int lastDownloadId)
        {
            CheckConfigurationDone();

            var preferences = PreferenceManager.GetDefaultSharedPreferences(_configuration.Context);
            preferences.Edit().PutInt(PreferencesLastDownloadId, _lastDownloadId = lastDownloadId);
        }

        private void CheckConfigurationDone()
        {
            if (_configuration?.Context == null) 
                throw new Exception("The DownloadDetailsCache should be configured before using this method.");
        }

        public Dictionary<int, DownloadDetails> GetAllDownloads()
        {
            var dictionary = new Dictionary<int, DownloadDetails>();

            foreach (var downloadDetail in _downloadDetails)
            {
                dictionary.Add(downloadDetail.Key, downloadDetail.Value);
            }

            return dictionary;
        }

        private int GetNextDownloadId()
        {
            lock (_lockLastDownloadId)
            {
                UpdateLastDownloadId(++_lastDownloadId);
                return _lastDownloadId;
            }
        }

        public DownloadDetails AddDownloadDetails(string url, string downloadLocation, string fileName, string mimeType)
        {
            var downloadId = GetNextDownloadId();
            var downloadDetails = new DownloadDetails(downloadId, url, fileName, downloadLocation, mimeType);

            return AddDownloadDetails(downloadDetails);
        }

        public DownloadDetails FindDownloadDetails(int downloadId)
        {
            _downloadDetails.TryGetValue(downloadId, out var downloadDetails);
            return downloadDetails;
        }

        public void RemoveDownloadDetails(int downloadId)
        {
            if (_downloadDetails.ContainsKey(downloadId))
            {
                _downloadDetails.TryRemove(downloadId, out var downloadDetail);

                var connection = DatabaseConnectionManager.GetConnection();
                connection.Delete(downloadDetail);
                connection.Commit();
            }
        }

        private DownloadDetails AddDownloadDetails(DownloadDetails downloadDetails)
        {
            _downloadDetails.AddOrUpdate(downloadDetails.DownloadId ?? Int32.MaxValue, downloadDetails, (previous, current) => downloadDetails);

            var connection = DatabaseConnectionManager.GetConnection();
            connection.InsertOrReplace(downloadDetails);
            connection.Commit();

            return downloadDetails;
        }

        public DownloadDetails UpdateDownloadDetails(DownloadDetails downloadDetails)
        {
            return AddDownloadDetails(downloadDetails);
        }
    }
}