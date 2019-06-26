# XDM ([![Build status](https://build.appcenter.ms/v0.1/apps/6d2f04d7-432b-4359-b59c-b5bc37394be5/branches/master/badge)](https://appcenter.ms))
A Storage Access Framework compatible download manager for Xamarin Android

## Using the Library
First off, you need to configure the download manager. You can do so by creating a `DownloadManagerConfiguration` and passing that value to the `DownloadManager.Instance.Initialize` method.

```csharp
var configuration = DownloadManagerConfiguration.Create(this, this);
DownloadManager.Instance.Initialize(configuration);
```

The parameters provided to the `DownloadManagerConfiguration.Create` method are as follows :
- 1st parameter - the _Context_ object
- 2nd parameter - the instance that implements the `StoragePermissionsHandler.ICallback` callback. Please see the next section for more details.

The rest is pretty easy. Use the following methods to control downloads
- `DownloadManager.Instance.Download(url, downloadLocation /* Absolute path to directory*/, fileName, mimeType)` returns the `DownloadId` which can be used on the rest of the methods to control downloads. If a file by the same name exists, will create a new file by a new name.
- `DownloadManager.Instance.Pause` - Self explanatory.
- `DownloadManager.Instance.Resume` - If the file exists on the recorded location, attempts to resume. Otherwise will create a new file and restart.
- `DownloadManager.Instance.Remove` - Self explanatory.
- `DownloadManager.Instance.GetAllDownloads` returns all the download records available.
- `DownloadManager.Instance.OpenFile`. Thought I should include this as well, since there is a high chance that you might need this as well.

## StoragePermissionsHandler.ICallback
When dealing with the [Storage Access Framework](http://www.androiddocs.com/guide/topics/providers/document-provider.html), the developer is expected to prompt the user for approval when accessing external files. As such, we have to do the same. However doing so within the library feels wrong, and since there is no obvious way of retrieving the approval result, we expect the developer implement this part of the task. Perhaps this code may go into the library itself someday, but for the moment you have to do this. The following code segments include a simple example.

```csharp
public void OnStoragePermissionsRequested(StoragePermissionsHandler.StoragePermissionsDetail.Request request)
{
    RunOnUiThread(() =>
    {
        var sdCard = new File(request.StorageLocation);
        var storageManager = (StorageManager) GetSystemService(Context.StorageService);
        var storageVolume = storageManager.GetStorageVolume(sdCard);
        var intent = storageVolume.CreateAccessIntent(null);

        StartActivityForResult(intent, 10001);
    });
}

protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
{
    if (requestCode == 10001)
    {
        if (resultCode == Result.Ok)
        {
            ContentResolver.TakePersistableUriPermission(data.Data, ActivityFlags.GrantWriteUriPermission);

            var documentFile = DocumentFile.FromTreeUri(this, data.Data);
            request.AcceptRequest(documentFile);
        }
        else
        {
            request.RejectRequest();
        }
    }
}
```
