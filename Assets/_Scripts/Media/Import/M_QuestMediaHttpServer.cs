/*
 * Script Name: M_QuestMediaHttpServer.cs
 * Description: Quest-hosted local HTTP upload endpoint for phone browsers.
 * Project Role: Replaces phone mirroring/WebRTC for import. Phones push media into the Quest's private library.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class M_QuestMediaHttpServer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M_PairingCodeProvider pairingCodeProvider;
    [SerializeField] private M_MediaImportController importController;
    [SerializeField] private M_MediaLibrary mediaLibrary;

    [Header("Server")]
    [SerializeField] private int port = 29100;
    [SerializeField] private bool autoStart = true;
    [SerializeField] private int maxRequestBytes = 160 * 1024 * 1024;
    [SerializeField] private int mainThreadTimeoutMs = 120000;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private TcpListener _listener;
    private Thread _listenerThread;
    private volatile bool _running;
    private string _localIp = "0.0.0.0";
    private string _lastStatus = "Upload server stopped.";
    private string _currentPairingCode = string.Empty;

    public event Action<string> StatusChanged;
    public event Action<string> UploadUrlChanged;

    public string UploadUrl => $"http://{_localIp}:{port}";
    public string LastStatus => _lastStatus;
    public bool IsRunning => _running;
    public int Port => port;

    private void Awake()
    {
        ResolveReferences();
        CachePairingCode();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (pairingCodeProvider != null)
        {
            pairingCodeProvider.OnCodeChanged -= HandlePairingCodeChanged;
            pairingCodeProvider.OnCodeChanged += HandlePairingCodeChanged;
            CachePairingCode();
        }
    }

    private void OnDisable()
    {
        if (pairingCodeProvider != null)
            pairingCodeProvider.OnCodeChanged -= HandlePairingCodeChanged;
    }

    private void Start()
    {
        if (autoStart)
            StartServer();
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out Action action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[M_QuestMediaHttpServer] Main-thread action failed: {e}");
            }
        }
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private void OnApplicationQuit()
    {
        StopServer();
    }

    [ContextMenu("Start Upload Server")]
    public void StartServer()
    {
        if (_running)
            return;

        ResolveReferences();
        CachePairingCode();
        _localIp = ResolveLocalIp();

        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _running = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "M_QuestMediaHttpServer"
            };
            _listenerThread.Start();

            SetStatus($"Upload server running at {UploadUrl}");
            UploadUrlChanged?.Invoke(UploadUrl);
        }
        catch (Exception e)
        {
            _running = false;
            SetStatus($"Could not start upload server on port {port}: {e.Message}");
            Debug.LogError($"[M_QuestMediaHttpServer] Start failed: {e}");
        }
    }

    [ContextMenu("Stop Upload Server")]
    public void StopServer()
    {
        if (!_running && _listener == null)
            return;

        _running = false;

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Stop closes the socket and intentionally interrupts AcceptTcpClient.
        }

        _listener = null;
        SetStatus("Upload server stopped.");
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                TcpClient client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException)
            {
                if (_running)
                    QueueStatus("Upload server socket interrupted.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                if (_running)
                    QueueStatus($"Upload server error: {e.Message}");
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 30000;

                using NetworkStream stream = client.GetStream();
                HttpRequest request = ReadRequest(stream);
                if (request == null)
                {
                    WriteText(stream, 400, "Bad Request", "Could not read the HTTP request.");
                    return;
                }

                RouteRequest(stream, request);
            }
            catch (Exception e)
            {
                if (verboseLogging)
                    Debug.LogWarning($"[M_QuestMediaHttpServer] Client request failed: {e.Message}");
            }
        }
    }

    private void RouteRequest(NetworkStream stream, HttpRequest request)
    {
        string path = request.PathOnly;

        if (request.Method == "GET" && path == "/")
        {
            WriteHtml(stream, 200, "OK", BuildUploadPage());
            return;
        }

        if (request.Method == "GET" && path == "/status")
        {
            string json = "{\"running\":" + (_running ? "true" : "false")
                          + ",\"url\":\"" + JsonEscape(UploadUrl)
                          + "\",\"status\":\"" + JsonEscape(_lastStatus)
                          + "\"}";
            WriteBytes(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
            return;
        }

        if (request.Method == "GET" && path.StartsWith("/thumb/", StringComparison.OrdinalIgnoreCase))
        {
            HandleThumbnailRequest(stream, request);
            return;
        }

        if (request.Method == "POST" && path == "/upload")
        {
            HandleUpload(stream, request);
            return;
        }

        WriteText(stream, 404, "Not Found", "That upload route does not exist.");
    }

    private void HandleUpload(NetworkStream stream, HttpRequest request)
    {
        string submittedCode = request.GetQuery("code");
        if (!ValidatePairingCode(submittedCode))
        {
            QueueStatus("Phone upload rejected: bad pairing code.");
            WriteText(stream, 403, "Forbidden", "The pairing code did not match. Please check the code shown in VR and try again.");
            return;
        }

        if (!request.Headers.TryGetValue("content-type", out string contentType)
            || contentType.IndexOf("multipart/form-data", StringComparison.OrdinalIgnoreCase) < 0)
        {
            WriteText(stream, 415, "Unsupported Media Type", "Upload must use multipart/form-data.");
            return;
        }

        string boundary = ExtractBoundary(contentType);
        if (string.IsNullOrWhiteSpace(boundary))
        {
            WriteText(stream, 400, "Bad Request", "Upload boundary was missing.");
            return;
        }

        MultipartFile file = ParseMultipartFile(request.Body, boundary);
        if (file == null || file.Bytes == null || file.Bytes.Length == 0)
        {
            WriteText(stream, 400, "Bad Request", "No media file was found in the upload.");
            return;
        }

        QueueStatus($"Receiving {M_MediaPaths.SafeDisplayName(file.FileName)}...");
        M_MediaImportResult result = RunOnMainThread(() => importController.ImportMediaBytes(file.Bytes, file.ContentType, file.FileName), mainThreadTimeoutMs);

        if (result == null)
        {
            QueueStatus("Upload timed out while importing.");
            WriteText(stream, 500, "Import Timed Out", "The Quest did not finish importing the media file. Please try again.");
            return;
        }

        QueueStatus(result.Message);

        if (!result.Success)
        {
            WriteText(stream, 422, "Import Failed", result.Message);
            return;
        }

        string noun = result.Record != null ? M_MediaTypeUtility.DisplayNoun(result.Record.kind) : "media file";
        string response = result.Duplicate
            ? $"This {noun} was already in your Quest gallery."
            : $"{char.ToUpperInvariant(noun[0])}{noun.Substring(1)} imported into your Quest gallery.";
        WriteText(stream, 200, "OK", response);
    }

    private void HandleThumbnailRequest(NetworkStream stream, HttpRequest request)
    {
        string submittedCode = request.GetQuery("code");
        if (!ValidatePairingCode(submittedCode))
        {
            WriteText(stream, 403, "Forbidden", "The pairing code did not match.");
            return;
        }

        string mediaId = request.PathOnly.Substring("/thumb/".Length);
        mediaId = Uri.UnescapeDataString(mediaId);

        byte[] bytes = RunOnMainThread(() =>
        {
            if (mediaLibrary == null)
                return null;

            M_MediaRecord record = mediaLibrary.GetByMediaId(mediaId);
            string thumbPath = mediaLibrary.ResolveThumbnailPath(record);
            if (string.IsNullOrWhiteSpace(thumbPath) || !File.Exists(thumbPath))
                return null;

            return File.ReadAllBytes(thumbPath);
        }, 5000);

        if (bytes == null || bytes.Length == 0)
        {
            WriteText(stream, 404, "Not Found", "Thumbnail was not found.");
            return;
        }

        WriteBytes(stream, 200, "OK", "image/jpeg", bytes);
    }

    private HttpRequest ReadRequest(NetworkStream stream)
    {
        using MemoryStream buffer = new MemoryStream();
        byte[] readBuffer = new byte[8192];
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            int read = stream.Read(readBuffer, 0, readBuffer.Length);
            if (read <= 0)
                return null;

            buffer.Write(readBuffer, 0, read);
            if (buffer.Length > maxRequestBytes)
                throw new InvalidOperationException("HTTP request exceeded configured maximum size.");

            headerEnd = FindHeaderEnd(buffer.GetBuffer(), (int)buffer.Length);
        }

        byte[] allBytes = buffer.ToArray();
        string headerText = Encoding.UTF8.GetString(allBytes, 0, headerEnd);
        string[] headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (headerLines.Length == 0)
            return null;

        string[] requestLine = headerLines[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        HttpRequest request = new HttpRequest
        {
            Method = requestLine[0].Trim().ToUpperInvariant(),
            RawTarget = requestLine[1].Trim(),
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        for (int i = 1; i < headerLines.Length; i++)
        {
            string line = headerLines[i];
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            request.Headers[line.Substring(0, colon).Trim().ToLowerInvariant()] = line.Substring(colon + 1).Trim();
        }

        int contentLength = 0;
        if (request.Headers.TryGetValue("content-length", out string lengthText))
            int.TryParse(lengthText, out contentLength);

        if (contentLength > maxRequestBytes)
            throw new InvalidOperationException("Upload exceeded configured maximum size.");

        int bodyStart = headerEnd + 4;
        using MemoryStream body = new MemoryStream();
        if (allBytes.Length > bodyStart)
            body.Write(allBytes, bodyStart, allBytes.Length - bodyStart);

        while (body.Length < contentLength)
        {
            int remaining = contentLength - (int)body.Length;
            int read = stream.Read(readBuffer, 0, Math.Min(readBuffer.Length, remaining));
            if (read <= 0)
                break;

            body.Write(readBuffer, 0, read);
        }

        request.Body = body.ToArray();
        request.ParseTarget();
        return request;
    }

    private T RunOnMainThread<T>(Func<T> action, int timeoutMs)
    {
        if (!_running)
            return default;

        using ManualResetEventSlim done = new ManualResetEventSlim(false);
        T result = default;
        Exception exception = null;

        _mainThreadActions.Enqueue(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception e)
            {
                exception = e;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(timeoutMs))
            return default;

        if (exception != null)
        {
            Debug.LogError($"[M_QuestMediaHttpServer] Main-thread request failed: {exception}");
            return default;
        }

        return result;
    }

    private bool ValidatePairingCode(string submittedCode)
    {
        if (string.IsNullOrWhiteSpace(_currentPairingCode))
            return false;

        return !string.IsNullOrWhiteSpace(submittedCode)
               && string.Equals(submittedCode.Trim(), _currentPairingCode, StringComparison.Ordinal);
    }

    private void ResolveReferences()
    {
        if (pairingCodeProvider == null)
            pairingCodeProvider = GetComponent<M_PairingCodeProvider>();

        if (pairingCodeProvider == null)
            pairingCodeProvider = FindFirstObjectByType<M_PairingCodeProvider>();

        if (importController == null)
            importController = GetComponent<M_MediaImportController>();

        if (importController == null)
            importController = FindFirstObjectByType<M_MediaImportController>();

        if (mediaLibrary == null && importController != null)
            mediaLibrary = importController.Library;

        if (mediaLibrary == null)
            mediaLibrary = GetComponent<M_MediaLibrary>();

        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();
    }

    private void CachePairingCode()
    {
        _currentPairingCode = pairingCodeProvider != null ? pairingCodeProvider.PairingCode : string.Empty;
    }

    private void HandlePairingCodeChanged(string code)
    {
        _currentPairingCode = code ?? string.Empty;
    }

    private string BuildUploadPage()
    {
        string code = pairingCodeProvider != null ? pairingCodeProvider.PairingCode : string.Empty;
        return @"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>MURPM Media Upload</title>
  <style>
    body{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,sans-serif;margin:0;padding:24px;background:#f8fafc;color:#111827;}
    main{max-width:560px;margin:0 auto;}
    label{display:block;font-weight:700;margin-top:18px;}
    input,button{box-sizing:border-box;width:100%;font-size:18px;padding:12px;margin-top:8px;border-radius:8px;border:1px solid #9ca3af;}
    button{background:#14532d;color:white;border:0;font-weight:800;}
    #msg{margin-top:18px;white-space:pre-wrap;font-weight:700;}
  </style>
</head>
<body>
<main>
  <h1>Upload media to Quest</h1>
  <label>Pairing code shown in VR</label>
  <input id=""code"" inputmode=""numeric"" autocomplete=""one-time-code"" value=""" + HtmlEscape(code) + @""">
  <label>Photo or video</label>
  <input id=""file"" type=""file"" accept=""image/*,video/mp4,video/quicktime,video/x-m4v,video/webm,video/*"">
  <button id=""upload"">Upload</button>
  <div id=""msg""></div>
</main>
<script>
const msg=document.getElementById('msg');
document.getElementById('upload').addEventListener('click', async () => {
  const file=document.getElementById('file').files[0];
  const code=document.getElementById('code').value.trim();
  if(!file || !code){ msg.textContent='Choose a file and enter the pairing code.'; return; }
  msg.textContent='Preparing upload...';
  let uploadFile=file;
  if(file.type.startsWith('image/')){
    uploadFile=await resizeImage(file,1600,0.82).catch(()=>file);
  }
  const form=new FormData();
  form.append('file', uploadFile, file.name);
  msg.textContent='Uploading to Quest... keep this page open.';
  try{
    const response=await fetch('/upload?code='+encodeURIComponent(code),{method:'POST',body:form});
    const text=await response.text();
    msg.textContent=response.ok ? text : ('Upload failed: '+text);
  }catch(e){ msg.textContent='Upload failed. Make sure the phone is on the same Wi-Fi as the Quest.'; }
});
function resizeImage(file,maxEdge,quality){
  return new Promise((resolve,reject)=>{
    const img=new Image();
    const url=URL.createObjectURL(file);
    img.onload=()=>{
      URL.revokeObjectURL(url);
      const longEdge=Math.max(img.width,img.height);
      if(longEdge<=maxEdge){ resolve(file); return; }
      const scale=maxEdge/longEdge;
      const canvas=document.createElement('canvas');
      canvas.width=Math.max(1,Math.round(img.width*scale));
      canvas.height=Math.max(1,Math.round(img.height*scale));
      canvas.getContext('2d').drawImage(img,0,0,canvas.width,canvas.height);
      canvas.toBlob(blob=> blob ? resolve(new File([blob],file.name.replace(/\.[^.]+$/,'.jpg'),{type:'image/jpeg'})) : resolve(file), 'image/jpeg', quality);
    };
    img.onerror=reject;
    img.src=url;
  });
}
</script>
</body>
</html>";
    }

    private void SetStatus(string status)
    {
        _lastStatus = status;
        if (verboseLogging)
            Debug.Log($"[M_QuestMediaHttpServer] {status}");

        StatusChanged?.Invoke(status);
    }

    private void QueueStatus(string status)
    {
        _mainThreadActions.Enqueue(() => SetStatus(status));
    }

    private static MultipartFile ParseMultipartFile(byte[] body, string boundary)
    {
        byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
        int position = 0;

        while (position < body.Length)
        {
            int boundaryStart = IndexOf(body, boundaryBytes, position);
            if (boundaryStart < 0)
                return null;

            int partStart = boundaryStart + boundaryBytes.Length;
            if (partStart + 1 < body.Length && body[partStart] == '-' && body[partStart + 1] == '-')
                return null;

            if (partStart + 1 < body.Length && body[partStart] == '\r' && body[partStart + 1] == '\n')
                partStart += 2;

            int headerEnd = IndexOf(body, Encoding.UTF8.GetBytes("\r\n\r\n"), partStart);
            if (headerEnd < 0)
                return null;

            string headers = Encoding.UTF8.GetString(body, partStart, headerEnd - partStart);
            int dataStart = headerEnd + 4;
            int nextBoundary = IndexOf(body, boundaryBytes, dataStart);
            if (nextBoundary < 0)
                return null;

            int dataEnd = nextBoundary;
            if (dataEnd >= 2 && body[dataEnd - 2] == '\r' && body[dataEnd - 1] == '\n')
                dataEnd -= 2;

            string disposition = FindHeader(headers, "Content-Disposition");
            string fileName = ParseHeaderParameter(disposition, "filename");
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                string contentType = FindHeader(headers, "Content-Type");
                int length = Math.Max(0, dataEnd - dataStart);
                byte[] bytes = new byte[length];
                Buffer.BlockCopy(body, dataStart, bytes, 0, length);
                return new MultipartFile
                {
                    FileName = Path.GetFileName(fileName),
                    ContentType = contentType,
                    Bytes = bytes
                };
            }

            position = nextBoundary + boundaryBytes.Length;
        }

        return null;
    }

    private static string ExtractBoundary(string contentType)
    {
        string[] parts = contentType.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("boundary=".Length).Trim('"');
        }

        return null;
    }

    private static string FindHeader(string headerBlock, string headerName)
    {
        string[] lines = headerBlock.Split(new[] { "\r\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0)
                continue;

            if (string.Equals(lines[i].Substring(0, colon).Trim(), headerName, StringComparison.OrdinalIgnoreCase))
                return lines[i].Substring(colon + 1).Trim();
        }

        return string.Empty;
    }

    private static string ParseHeaderParameter(string header, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(header))
            return string.Empty;

        string[] parts = header.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            int equals = part.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = part.Substring(0, equals).Trim();
            if (!string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase))
                continue;

            return part.Substring(equals + 1).Trim().Trim('"');
        }

        return string.Empty;
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (int i = 0; i <= length - 4; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        if (haystack == null || needle == null || needle.Length == 0)
            return -1;

        for (int i = Math.Max(0, start); i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    private static void WriteHtml(NetworkStream stream, int code, string reason, string html)
    {
        WriteBytes(stream, code, reason, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    private static void WriteText(NetworkStream stream, int code, string reason, string text)
    {
        WriteBytes(stream, code, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text ?? string.Empty));
    }

    private static void WriteBytes(NetworkStream stream, int code, string reason, string contentType, byte[] bytes)
    {
        bytes ??= Array.Empty<byte>();
        string header = $"HTTP/1.1 {code} {reason}\r\n"
                        + $"Content-Type: {contentType}\r\n"
                        + $"Content-Length: {bytes.Length}\r\n"
                        + "Connection: close\r\n"
                        + "Cache-Control: no-store\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ResolveLocalIp()
    {
        string androidIp = M_AndroidIpUtil.GetLocalWifiIp();
        if (!string.IsNullOrWhiteSpace(androidIp) && androidIp != "0.0.0.0")
            return androidIp;

        try
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress address in host.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    return address.ToString();
            }
        }
        catch
        {
            // Editor fallback only. Quest should use M_AndroidIpUtil.
        }

        return "127.0.0.1";
    }

    private static string HtmlEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private sealed class MultipartFile
    {
        public string FileName;
        public string ContentType;
        public byte[] Bytes;
    }

    private sealed class HttpRequest
    {
        public string Method;
        public string RawTarget;
        public string PathOnly;
        public byte[] Body;
        public Dictionary<string, string> Headers;
        private Dictionary<string, string> _query;

        public void ParseTarget()
        {
            _query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string target = string.IsNullOrWhiteSpace(RawTarget) ? "/" : RawTarget;
            int question = target.IndexOf('?');
            PathOnly = question >= 0 ? target.Substring(0, question) : target;
            if (string.IsNullOrWhiteSpace(PathOnly))
                PathOnly = "/";

            if (question < 0 || question + 1 >= target.Length)
                return;

            string[] pairs = target.Substring(question + 1).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(pairs[i]))
                    continue;

                int equals = pairs[i].IndexOf('=');
                string key = equals >= 0 ? pairs[i].Substring(0, equals) : pairs[i];
                string value = equals >= 0 ? pairs[i].Substring(equals + 1) : string.Empty;
                _query[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", " "));
            }
        }

        public string GetQuery(string key)
        {
            if (_query != null && _query.TryGetValue(key, out string value))
                return value;

            return string.Empty;
        }
    }
}
