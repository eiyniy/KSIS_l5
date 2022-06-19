using System;
using System.Net;
using System.Text;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;

public class HttpFilesHandler
{
    private ConcurrentDictionary<string, Action<HttpListenerContext>> _handlers;
    private ConcurrentDictionary<int, string> _httpStatusCodes;
    private HttpListener _listener;


    public string RemoteUrl { get; private set; }

    public string LocalFilePath { get; private set; }

    public bool IsActive { get; private set; }


    public HttpFilesHandler(string url, string localPath)
    {
        LocalFilePath = localPath;
        RemoteUrl = url;

        _listener = new HttpListener();
        _listener.Prefixes.Add(url);

        _handlers = new ConcurrentDictionary<string, Action<HttpListenerContext>>();
        _handlers.TryAdd("GET", GetHandler);
        _handlers.TryAdd("PUT", PutHandler);
        _handlers.TryAdd("HEAD", HeadHandler);
        _handlers.TryAdd("DELETE", DeleteHandler);

        _httpStatusCodes = new ConcurrentDictionary<int, string>();
        _httpStatusCodes.TryAdd(500, "Internal server error");
        _httpStatusCodes.TryAdd(201, "Created");
        _httpStatusCodes.TryAdd(200, "OK");
        _httpStatusCodes.TryAdd(404, "Not found");
        _httpStatusCodes.TryAdd(403, "Forbidden");

        IsActive = false;
    }


    public void Start()
    {
        Task.Run(() =>
        {
            IsActive = true;
            _listener.Start();

            while (IsActive)
            {
                var context = _listener.GetContext();
                ProcessContext(context);
            }

            _listener.Close();
            _listener.Stop();
        });
    }

    public void Stop() => IsActive = false;

    private void ProcessContext(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        string method = request.HttpMethod;

        if (_handlers.TryGetValue(method, out Action<HttpListenerContext> handler))
            handler.Invoke(context);
        else
            ErrorHandler(context);
    }

    private void GetHandler(HttpListenerContext context)
    {
        string path = LocalFilePath + context.Request.Url.LocalPath.Split(' ')[0].Remove(0, 1);

        try
        {
            FileAttributes attr = File.GetAttributes(path);
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
            {
                string[] filesInfo = Directory.GetFiles(path);
                for (int i = 0; i < filesInfo.Length; ++i)
                    filesInfo[i] = filesInfo[i].Remove(0, LocalFilePath.Length);

                var serializer = new DataContractJsonSerializer(filesInfo.GetType());
                MemoryStream ms = new MemoryStream();
                serializer.WriteObject(ms, filesInfo);
                byte[] bytes = ms.ToArray();

                context.Response.ContentEncoding = Encoding.ASCII;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.LongLength;

                using (var stream = context.Response.OutputStream)
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                Console.WriteLine("{0}: {1} {2}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode));
            }
            else
            {
                FileInfo file = new FileInfo(path);

                context.Response.ContentType = "application/force-download";
                context.Response.Headers.Add("Content-Transfer-Encoding", "binary");
                context.Response.Headers.Add("Content-Disposition", string.Format("attachment; filename={0}", file.Name));

                using (var fileStream = file.OpenRead())
                using (var stream = context.Response.OutputStream)
                {
                    fileStream.CopyTo(stream);
                }

                context.Response.Close();

                Console.WriteLine("{0}: {1} {2}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode));
            }
        }
        catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException)
        {
            ErrorHandler(context, 404);
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
        catch (Exception e)
        {
            ErrorHandler(context);
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
    }

    private void PutHandler(HttpListenerContext context)
    {
        try
        {
            string path = LocalFilePath + context.Request.Url.LocalPath.Split(' ')[0].Remove(0, 1);
            int pos = path.LastIndexOf('/');
            string dirPath = path.Remove(pos);

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            using (var fileStream = File.Create(path))
            using (var stream = context.Request.InputStream)
            {
                stream.CopyTo(fileStream);
            }

            FillResponse(context.Response, 201);

            using (var stream = context.Response.OutputStream)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(GetStatusString(201));
                stream.Write(bytes, 0, bytes.Length);
            }

            context.Response.Close();

            Console.WriteLine("{0}: {1} {2}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode));
        }
        catch (Exception e)
        {
            ErrorHandler(context);
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
    }

    private void HeadHandler(HttpListenerContext context)
    {
        string path = LocalFilePath + context.Request.Url.LocalPath.Split(' ')[0].Remove(0, 1);

        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException();

            FileInfo info = new FileInfo(path);

            context.Response.ContentType = info.Extension;
            context.Response.ContentLength64 = info.Length;
            context.Response.Headers.Add("Last-Modified", info.LastWriteTime.ToString());

            context.Response.Close();

            Console.WriteLine("{0}: {1} {2}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode));
        }
        catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException)
        {
            context.Response.StatusCode = 404;
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
        catch (Exception e)
        {
            context.Response.StatusCode = 500;
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private void DeleteHandler(HttpListenerContext context)
    {
        string path = LocalFilePath + context.Request.Url.LocalPath.Split(' ')[0].Remove(0, 1);

        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException();

            File.Delete(path);

            using (var stream = context.Response.OutputStream)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(GetStatusString(200));
                stream.Write(bytes, 0, bytes.Length);
            }

            context.Response.Close();

            Console.WriteLine("{0}: {1} {2}", DateTime.Now, context.Request.HttpMethod, GetStatusString(200));
        }
        catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException)
        {
            ErrorHandler(context, 404);
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
        catch (Exception e)
        {
            ErrorHandler(context);
            Console.WriteLine("{0}: {1} {2} {3}", DateTime.Now, context.Request.HttpMethod, GetStatusString(context.Response.StatusCode), e.Message);
        }
    }

    private void ErrorHandler(HttpListenerContext context, int code = 500)
    {
        context.Response.StatusCode = code;
        using (var stream = context.Response.OutputStream)
        {
            byte[] message = Encoding.ASCII.GetBytes(GetStatusString(code));
            context.Response.ContentLength64 = message.Length;
            stream.Write(message, 0, message.Length);
        }

        context.Response.Close();
    }

    private void FillResponse(HttpListenerResponse response, int code)
    {
        if (_httpStatusCodes.TryGetValue(code, out string description))
        {
            response.StatusCode = code;
            response.StatusDescription = description;
        }
        else
            throw new ArgumentOutOfRangeException(nameof(FillResponse));
    }

    private string GetStatusString(int code)
    {
        if (_httpStatusCodes.TryGetValue(code, out string description))
            return string.Format("{0} {1}", code, description);
        else
            throw new ArgumentOutOfRangeException(nameof(GetStatusString));
    }
}
