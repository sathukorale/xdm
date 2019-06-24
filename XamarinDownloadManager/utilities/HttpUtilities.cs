using System.IO;
using Java.Net;

namespace xdm.utilities
{
    public class HttpUtilities
    {
        public static Stream GetDownloadStream(string url, long streamOffset, out long contentSize)
        {
            while (true)
            {
                var conn = (HttpURLConnection) (new URL(url).OpenConnection());
                conn.ConnectTimeout = 10000;
                conn.ReadTimeout = 10000;
                conn.InstanceFollowRedirects = false;
                conn.RequestMethod = "GET";
                conn.SetRequestProperty("Range", $"bytes={streamOffset}-");

                conn.Connect();

                if (conn.ResponseCode == HttpStatus.MovedPerm ||
                    conn.ResponseCode == HttpStatus.MovedTemp || 
                    conn.ResponseCode == HttpStatus.SeeOther || 
                    conn.ResponseCode == HttpStatus.MultChoice)
                {
                    url = conn.GetHeaderField("Location");
                    url = URLDecoder.Decode(url, "UTF-8");
                    continue;
                }

                if (conn.ResponseCode == HttpStatus.Ok ||
                    conn.ResponseCode == HttpStatus.Accepted ||
                    conn.ResponseCode == HttpStatus.Partial)
                {
                    contentSize = streamOffset + conn.ContentLengthLong;
                    return conn.InputStream;
                }

                if ((int) conn.ResponseCode == 416 /* Request Range not Satisfiable */)
                {
                    contentSize = streamOffset;
                    return null;
                }

                contentSize = 0;
                return null;
            }
        }
    }
}