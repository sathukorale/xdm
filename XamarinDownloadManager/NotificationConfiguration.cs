using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Support.V4.App;
using Android.Views;
using Android.Widget;

namespace xdm
{
    public class NotificationConfiguration
    {
        public Context Context { get; internal set; }
        public int? NotificationLayoutId { get; internal set; }
        public int? NotificationTitleViewId { get; internal set; }
        public int? NotficationProgressTextViewId { get; internal set; }
        public int? NotificationProgressBarViewId { get; internal set; }
        public int? NotificationTotalFileSizeViewId { get; internal set; }
        public int? NotificationDownloadedFileSizeViewId { get; internal set; }
        public NotificationChannel NotificationChannel { get; internal set; }

        public NotificationConfiguration(Context context, int notificationLayoutId)
        {
            Context = context;
            NotificationLayoutId = notificationLayoutId;
            NotificationChannel = new NotificationChannel("xdm.downloadmanager.service", "Download Service", NotificationImportance.Low);
        }

        public NotificationConfiguration SetTitleViewId(int titleViewId)
        {
            NotificationTitleViewId = titleViewId;
            return this;
        }

        public NotificationConfiguration SetProgressTextViewId(int progressTextViewId)
        {
            NotficationProgressTextViewId = progressTextViewId;
            return this;
        }

        public NotificationConfiguration SetProgressBarViewId(int progressBarViewId)
        {
            NotificationProgressBarViewId = progressBarViewId;
            return this;
        }

        public NotificationConfiguration SetTotalFileSizeViewId(int totalFileSizeViewId)
        {
            NotificationTotalFileSizeViewId = totalFileSizeViewId;
            return this;
        }

        public NotificationConfiguration SetDownloadedFileSizeViewId(int downloadedSizeViewId)
        {
            NotificationDownloadedFileSizeViewId = downloadedSizeViewId;
            return this;
        }

        public NotificationConfiguration SetNotificationChannelDetails(string channelId, string channelName, NotificationImportance notificationImportance)
        {
            NotificationChannel = new NotificationChannel(channelId, channelName, notificationImportance);
            return this;
        }

        public static NotificationConfiguration GetDefaultConfiguration(Context context)
        {
            var configuration = new NotificationConfiguration(context, Resource.Layout.NotificationDownloadProgress);

            configuration.SetTitleViewId(Resource.Id.lblDownloadNotificationTitle);
            configuration.SetTotalFileSizeViewId(Resource.Id.lblDownloadNotificationTotalSize);
            configuration.SetProgressBarViewId(Resource.Id.prgDownloadNotificationStatus);
            configuration.SetProgressTextViewId(Resource.Id.lblDownloadNotificationAdditionalDetails);

            return configuration;
        }
    }

    internal class NotificationHelper
    {
        private NotificationConfiguration _configuration;
        private Notification _notification;
        private RemoteViews _notificationRemoteViews;
        private int? _notificationId;

        private static int _currentNotificationId = 0;

        public NotificationHelper(NotificationConfiguration configuration)
        {
            _configuration = configuration;
            _notification = null;
            _notificationId = null;
        }

        private Notification GetNotification()
        {
            if (_notification == null || _notificationRemoteViews == null) CreateNotification();
            return _notification;
        }

        private RemoteViews GetNotificationRemoteViews()
        {
            if (_notification == null || _notificationRemoteViews == null) CreateNotification();
            return _notificationRemoteViews;
        }

        private void CreateNotification()
        {
            if (_configuration.NotificationLayoutId == null) throw new Exception("The notification layout hasn't been setup.");

            var context = _configuration.Context;
            var notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService);
            var notificationRemoteView = new RemoteViews(context.PackageName, _configuration.NotificationLayoutId ?? 0);
            var notificationBuilder = new NotificationCompat.Builder(context, _configuration.NotificationChannel.Id);

            notificationBuilder.SetCustomContentView(notificationRemoteView);
            notificationBuilder.SetSmallIcon(Resource.Drawable.ImgDownloadService);

            _notificationRemoteViews = notificationRemoteView;
            _notification = notificationBuilder.Build();
            _notification.Flags |= NotificationFlags.NoClear | NotificationFlags.OngoingEvent;
        }

        /* Should be used when download details are still pending */
        public void UpdateNotification(string fileName)
        {
            var notificationManager = (NotificationManager)_configuration.Context.GetSystemService(Context.NotificationService);
            var notification = GetNotification();
            var notificationRemoteViews = GetNotificationRemoteViews();

            if (_configuration.NotificationTitleViewId != null)
                notificationRemoteViews.SetTextViewText(_configuration.NotificationTitleViewId.Value, $"Downloading '{fileName}'.");

            if (_configuration.NotificationProgressBarViewId != null)
                notificationRemoteViews.SetProgressBar(_configuration.NotificationProgressBarViewId.Value, 100, 0, false);

            _notificationId = _notificationId ?? GetNextNotificationId();
            notificationManager.Notify(_notificationId ?? 0, notification);
        }

        public void UpdateNotification(string fileName, long downloadedSize, long totalSize)
        {
            var notificationManager = (NotificationManager)_configuration.Context.GetSystemService(Context.NotificationService);
            var notification = GetNotification();
            var notificationRemoteViews = GetNotificationRemoteViews();
            var progress = (downloadedSize * 100.0) / Math.Max(1, totalSize);

            if (_configuration.NotificationTitleViewId != null)
                notificationRemoteViews.SetTextViewText(_configuration.NotificationTitleViewId.Value, $"Downloading '{fileName}'.");

            if (_configuration.NotficationProgressTextViewId != null)
                notificationRemoteViews.SetTextViewText(_configuration.NotficationProgressTextViewId.Value, $"{(int)progress} %");

            if (_configuration.NotificationProgressBarViewId != null)
                notificationRemoteViews.SetProgressBar(_configuration.NotificationProgressBarViewId.Value, 100, (int)progress, false);

            if (_configuration.NotificationTotalFileSizeViewId != null && totalSize > 0)
                notificationRemoteViews.SetTextViewText(_configuration.NotificationTotalFileSizeViewId.Value, ToHumanReadableSize(downloadedSize));

            if (_configuration.NotificationDownloadedFileSizeViewId != null && totalSize > 0)
                notificationRemoteViews.SetTextViewText(_configuration.NotificationDownloadedFileSizeViewId.Value, ToHumanReadableSize(totalSize));

            _notificationId = _notificationId ?? GetNextNotificationId();
            notificationManager.Notify(_notificationId ?? 0, notification);
        }

        public void RemoveNotification()
        {
            var notificationManager = (NotificationManager)_configuration.Context.GetSystemService(Context.NotificationService);

            notificationManager.Cancel(_notificationId ?? 0);
            _notificationId = null;
            _notification = null;
            _notificationRemoteViews = null;
        }

        private static int GetNextNotificationId()
        {
            return Interlocked.Increment(ref _currentNotificationId);
        }

        private static string ToHumanReadableSize(long size, bool showDecimal = true)
        {
            var suffixes = new[] { "B", "KB", "MB", "GB", "TB" };
            var index = 0;
            var doubleSize = 0.0;
            while (size >= 1024 && index < suffixes.Length - 1)
            {
                doubleSize = size / 1024.0;
                size = size / 1024;
                index++;
            }

            return (showDecimal ? doubleSize.ToString("F2") : size.ToString()) + " " + suffixes[index];
        }
    }
}