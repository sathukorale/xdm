using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Support.V4.Content;
using Android.Systems;

namespace xdm.utilities
{
    public class StorageUtils
    {
        public static List<string> GetAllStorageLocations(Context context)
        {
            var appsDirs = ContextCompat.GetExternalFilesDirs(context, null);
            return appsDirs.Select(file => file.ParentFile.ParentFile.ParentFile.ParentFile.AbsolutePath).ToList();
        }

        public static bool IsInRemovableStorage(Context context, string path, out string rootDirectory)
        {
            rootDirectory = null;

            if (path == null) return false;

            var storagePaths = GetAllStorageLocations(context);
            if (storagePaths.Count == 1) return false;

            path = path.ToLower();

            var removableStorageDevices = storagePaths.Skip(1).Select(i => i.ToLower());
            rootDirectory = removableStorageDevices.FirstOrDefault(i => path.StartsWith(i));

            return rootDirectory != null;
        }

        public static bool IsInAnyStorageDevices(Context context, string path, out string rootDirectory)
        {
            path = path.ToLower();

            var storagePaths = GetAllStorageLocations(context);
            rootDirectory = storagePaths.FirstOrDefault(i => path.StartsWith(i.ToLower()));
            return rootDirectory != null;
        }

        public static bool CheckSpaceAvailable(Context context, DownloadDetails.RootDirectoryDetails root, long neededSpace)
        {
            var descriptor = context.ContentResolver.OpenFileDescriptor(root.DocumentFile.Uri, "r");
            var stats = Os.Fstatvfs(descriptor.FileDescriptor);
            var availableSpace = (stats.FBavail * stats.FBsize);

            return (neededSpace <= availableSpace);
        }
    }
}