using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace xdm
{
    internal class NotificationChannelDetails
    {
        public readonly string ChannelId;
        public readonly string ChannelName;
        public readonly NotificationImportance Importance;

        private NotificationChannel _notificationChannel;

        public NotificationChannelDetails(String channelId, String channelName, NotificationImportance importance)
        {
            ChannelId = channelId;
            ChannelName = channelName;
            Importance = importance;
        }

        public override int GetHashCode()
        {
            return ChannelId.GetHashCode();
        }

        public NotificationChannel GetNotificationChannel()
        {
            return _notificationChannel ?? (_notificationChannel = new NotificationChannel(ChannelId, ChannelName, Importance));
        }
    }

    internal class NotificationChannelManager
    {
        private static NotificationChannelManager _instance;
        public static NotificationChannelManager Instance => _instance ?? (_instance = new NotificationChannelManager());

        private Context _context;
        private readonly Dictionary<NotificationChannelDetails, NotificationChannel> _channels;

        private NotificationChannelManager()
        {
            _channels = new Dictionary<NotificationChannelDetails, NotificationChannel>();
        }

        public void Configure(Context context)
        {
            _context = context;
        }

        public string GetNotificationChannel(NotificationChannelDetails channelDetails)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O) return null;
            if (_channels.ContainsKey(channelDetails)) return channelDetails.ChannelId;

            var channel = channelDetails.GetNotificationChannel();
            _channels.Add(channelDetails, channel);

            var notificationManager = GetNotificationManager();
            notificationManager.CreateNotificationChannel(channel);

            return channelDetails.ChannelId;
        }

        public NotificationManager GetNotificationManager()
        {
            return (NotificationManager) _context.GetSystemService(Context.NotificationService);
        }
    }
}