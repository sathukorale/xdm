using SQLite;

namespace xdm.utilities
{
    public class DatabaseConnectionManager
    {
        public static readonly string DatabaseFileLocation = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "download_details.sqlite");

        public static SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(DatabaseFileLocation, SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache, true);
        }
    }
}