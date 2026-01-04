using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteKM.Client;

internal sealed class TcpSender : IDisposable
{
    private string _host;
    private int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private BinaryWriter? _writer;
    private BinaryReader? _reader;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private readonly object _lock = new();
    private int _connectionLostNotified;
    private int _connectionState;
    private readonly object _fileListLock = new();
    private PendingFileList? _localFileList;
    private string? _remoteFileToken;
    private PendingReceive? _pendingReceive;
    private int _transferInProgress;

    internal event Action<ControlMessage, int>? ControlReceived;
    internal event Action<string>? ClipboardReceived;
    internal event Action<FileTransferProgress>? FileTransferProgress;
    internal event Action<string>? FileTransferReceived;
    internal event Action? ConnectionEstablished;
    internal event Action? ConnectionLost;

    internal TcpSender(string host, int port)
    {
        _host = host;
        _port = port;
    }

    private int TransferPort => _port + 1;

    internal bool IsConnected => Volatile.Read(ref _connectionState) == 1;

    internal bool HasRemoteFileList
    {
        get
        {
            lock (_fileListLock)
            {
                return _remoteFileToken != null;
            }
        }
    }

    internal void ClearRemoteFileList()
    {
        lock (_fileListLock)
        {
            _remoteFileToken = null;
            _pendingReceive = null;
        }
    }

    internal void ClearLocalFileList()
    {
        lock (_fileListLock)
        {
            _localFileList = null;
        }
    }

    internal void UpdateEndpoint(string host, int port)
    {
        lock (_lock)
        {
            _host = host;
            _port = port;
            ResetConnectionLocked();
        }
    }

    internal bool TryConnect()
    {
        lock (_lock)
        {
            return TryEnsureConnected();
        }
    }

    internal void EnsureConnected()
    {
        if (_client is { Connected: true })
        {
            return;
        }

        _client?.Dispose();
        _client = new TcpClient();
        _client.NoDelay = true;
        _client.Connect(_host, _port);
        _stream = _client.GetStream();
        _writer = new BinaryWriter(_stream);
        _reader = new BinaryReader(_stream);
        Interlocked.Exchange(ref _connectionLostNotified, 0);
        NotifyConnectionEstablished();
        StartListening();
    }

    internal bool SendKeyboard(KeyboardEvent evt)
    {
        var notifyLost = false;
        var success = false;
        lock (_lock)
        {
            if (!TryEnsureConnected())
            {
                notifyLost = true;
            }
            else
            {
                try
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send keyboard: msg={evt.Message} vk={evt.VkCode} scan={evt.ScanCode} flags={evt.Flags}");
                    }
                    _writer!.Write((byte)MessageKind.Keyboard);
                    _writer.Write(evt.Message);
                    _writer.Write(evt.VkCode);
                    _writer.Write(evt.ScanCode);
                    _writer.Write(evt.Flags);
                    _writer.Flush();
                    Interlocked.Exchange(ref _connectionLostNotified, 0);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send keyboard failed: {ex.Message}");
                    }
                    ResetConnectionLocked();
                    notifyLost = true;
                }
            }
        }

        if (notifyLost)
        {
            NotifyConnectionLost();
        }

        return success;
    }

    internal bool SendMouse(MouseEvent evt)
    {
        var notifyLost = false;
        var success = false;
        lock (_lock)
        {
            if (!TryEnsureConnected())
            {
                notifyLost = true;
            }
            else
            {
                try
                {
                    if (Program.VerboseFlag)
                    {
                        //Console.WriteLine($"Send mouse: msg={evt.Message} x={evt.X} y={evt.Y} data={evt.MouseData} flags={evt.Flags}");
                    }
                    _writer!.Write((byte)MessageKind.Mouse);
                    _writer.Write(evt.Message);
                    _writer.Write(evt.X);
                    _writer.Write(evt.Y);
                    _writer.Write(evt.MouseData);
                    _writer.Write(evt.Flags);
                    _writer.Flush();
                    Interlocked.Exchange(ref _connectionLostNotified, 0);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send mouse failed: {ex.Message}");
                    }
                    ResetConnectionLocked();
                    notifyLost = true;
                }
            }
        }

        if (notifyLost)
        {
            NotifyConnectionLost();
        }

        return success;
    }

    internal bool SendClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        ClearLocalFileList();

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > ClipboardConstants.MaxBytes)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Clipboard text too large: {bytes.Length} bytes");
            }
            return false;
        }

        var notifyLost = false;
        var success = false;
        lock (_lock)
        {
            if (!TryEnsureConnected())
            {
                notifyLost = true;
            }
            else
            {
                try
                {
                    _writer!.Write((byte)MessageKind.Clipboard);
                    _writer.Write((int)ClipboardContentKind.Text);
                    _writer.Write(bytes.Length);
                    _writer.Write(bytes);
                    _writer.Flush();
                    Interlocked.Exchange(ref _connectionLostNotified, 0);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send clipboard failed: {ex.Message}");
                    }
                    ResetConnectionLocked();
                    notifyLost = true;
                }
            }
        }

        if (notifyLost)
        {
            NotifyConnectionLost();
        }

        return success;
    }

    internal bool SendClipboardFileList(IReadOnlyList<string> paths)
    {
        var pending = BuildPendingFileList(paths);
        if (pending == null)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Clipboard file list ignored: no valid files.");
            }
            return false;
        }

        lock (_fileListLock)
        {
            _localFileList = pending;
        }

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Clipboard file list queued: items={pending.Items.Count} token={pending.Token}");
        }

        var notifyLost = false;
        var success = false;
        lock (_lock)
        {
            if (!TryEnsureConnected())
            {
                notifyLost = true;
            }
            else
            {
                try
                {
                    _writer!.Write((byte)MessageKind.Clipboard);
                    _writer.Write((int)ClipboardContentKind.FileList);
                    WriteString(_writer, pending.Token);
                    _writer.Write(pending.Items.Count);
                    foreach (var item in pending.Items)
                    {
                        _writer.Write((byte)item.Kind);
                        WriteString(_writer, item.DisplayName);
                    }

                    _writer.Flush();
                    Interlocked.Exchange(ref _connectionLostNotified, 0);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send clipboard file list failed: {ex.Message}");
                    }
                    ResetConnectionLocked();
                    notifyLost = true;
                }
            }
        }

        if (notifyLost)
        {
            NotifyConnectionLost();
        }

        return success;
    }

    internal bool SendCaptureStart(CaptureEdge edge)
    {
        return SendControl(ControlMessage.CaptureStart, (int)edge);
    }

    internal bool SendCaptureStop()
    {
        return SendControl(ControlMessage.CaptureStop, 0);
    }

    internal bool TryRequestFileTransfer(string? destinationPath)
    {
        string? token;
        lock (_fileListLock)
        {
            if (_transferInProgress != 0)
            {
                if (Program.VerboseFlag)
                {
                    Console.WriteLine("Paste ignored: transfer already in progress.");
                }
                return false;
            }

            token = _remoteFileToken;
            if (token == null)
            {
                if (Program.VerboseFlag)
                {
                    Console.WriteLine("Paste ignored: no remote file list available.");
                }
                return false;
            }

            _pendingReceive = new PendingReceive(token);
        }

        var notifyLost = false;
        var success = false;
        lock (_lock)
        {
            if (!TryEnsureConnected())
            {
                notifyLost = true;
            }
            else
            {
                try
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Requesting file transfer: token={token}");
                    }
                    _writer!.Write((byte)MessageKind.Clipboard);
                    _writer.Write((int)ClipboardContentKind.FileTransferRequest);
                    WriteString(_writer, token);
                    WriteString(_writer, string.Empty);
                    _writer.Flush();
                    Interlocked.Exchange(ref _connectionLostNotified, 0);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send file transfer request failed: {ex.Message}");
                    }
                    ResetConnectionLocked();
                    notifyLost = true;
                }
            }
        }

        if (!success)
        {
            lock (_fileListLock)
            {
                _pendingReceive = null;
            }
        }
        else
        {
            _ = Task.Run(() => RequestFileTransferOnTransferChannel(token));
        }

        if (notifyLost)
        {
            NotifyConnectionLost();
        }

        return success;
    }

    private bool TryEnsureConnected()
    {
        try
        {
            if (Program.VerboseFlag)
            {
                //Console.WriteLine($"EnsureConnected: host={_host} port={_port} connected={_client?.Connected ?? false}");
            }
            EnsureConnected();
            StartListening();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
            ResetConnectionLocked();
            return false;
        }
    }

    private bool SendControl(ControlMessage message, int value)
    {
        var notifyLost = false;
        var success = false;
        lock (_lock)
        {
            if (!TryEnsureConnected())
            {
                notifyLost = true;
            }
            else
            {
                try
                {
                    _writer!.Write((byte)MessageKind.Control);
                    _writer.Write((int)message);
                    _writer.Write(value);
                    _writer.Flush();
                    Interlocked.Exchange(ref _connectionLostNotified, 0);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send control failed: {ex.Message}");
                    }
                    ResetConnectionLocked();
                    notifyLost = true;
                }
            }
        }

        if (notifyLost)
        {
            NotifyConnectionLost();
        }

        return success;
    }

    private void StartListening()
    {
        if (_listenTask != null && !_listenTask.IsCompleted)
        {
            return;
        }

        if (_reader == null)
        {
            return;
        }

        _listenCts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_listenCts.Token));
    }

    private void ListenLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var kind = (MessageKind)_reader!.ReadByte();
                if (kind == MessageKind.Control)
                {
                    var message = (ControlMessage)_reader.ReadInt32();
                    var value = _reader.ReadInt32();
                    ControlReceived?.Invoke(message, value);
                    continue;
                }

                if (kind == MessageKind.Clipboard)
                {
                    var contentKind = (ClipboardContentKind)_reader.ReadInt32();
                    if (contentKind == ClipboardContentKind.Text)
                    {
                        var length = _reader.ReadInt32();
                        if (length < 0 || length > ClipboardConstants.MaxBytes)
                        {
                            NotifyConnectionLost();
                            ResetConnection();
                            return;
                        }

                        var bytes = _reader.ReadBytes(length);
                        if (bytes.Length != length)
                        {
                            NotifyConnectionLost();
                            ResetConnection();
                            return;
                        }

                        var text = Encoding.UTF8.GetString(bytes);
                        ClipboardReceived?.Invoke(text);
                        continue;
                    }

                    if (contentKind == ClipboardContentKind.FileList)
                    {
                        if (!HandleFileListAnnouncement(_reader))
                        {
                            NotifyConnectionLost();
                            ResetConnection();
                            return;
                        }

                        continue;
                    }

                    if (contentKind == ClipboardContentKind.FileTransferRequest)
                    {
                        if (!HandleFileTransferRequest(_reader))
                        {
                            NotifyConnectionLost();
                            ResetConnection();
                            return;
                        }

                        continue;
                    }

                    if (contentKind == ClipboardContentKind.FileTransferData)
                    {
                        if (!HandleFileTransferData(_reader))
                        {
                            NotifyConnectionLost();
                            ResetConnection();
                            return;
                        }

                        continue;
                    }

                    NotifyConnectionLost();
                    ResetConnection();
                    return;
                }

                NotifyConnectionLost();
                ResetConnection();
                return;
            }
        }
        catch (EndOfStreamException)
        {
            NotifyConnectionLost();
            ResetConnection();
        }
        catch (IOException ex)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Control listen error: {ex.Message}");
            }
            NotifyConnectionLost();
            ResetConnection();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void StopListening()
    {
        if (_listenCts == null)
        {
            return;
        }

        _listenCts.Cancel();
        _listenCts.Dispose();
        _listenCts = null;
        _listenTask = null;
    }

    private void ResetConnection()
    {
        lock (_lock)
        {
            ResetConnectionLocked();
        }
    }

    private void ResetConnectionLocked()
    {
        StopListening();
        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _stream = null;
        _client = null;
        Interlocked.Exchange(ref _connectionState, 0);
        ClearRemoteFileList();
        ClearLocalFileList();
    }

    private void NotifyConnectionEstablished()
    {
        if (Interlocked.Exchange(ref _connectionState, 1) == 0)
        {
            ConnectionEstablished?.Invoke();
        }
    }

    private void NotifyConnectionLost()
    {
        Interlocked.Exchange(ref _connectionState, 0);
        if (Interlocked.Exchange(ref _connectionLostNotified, 1) == 0)
        {
            ConnectionLost?.Invoke();
        }
    }

    private sealed record ClipboardListItem(string SourcePath, ClipboardFileEntryKind Kind, string DisplayName);

    private sealed record PendingFileList(string Token, IReadOnlyList<ClipboardListItem> Items);

    private sealed record PendingReceive(string Token);

    private sealed record TransferStats(int EntryCount, int FileCount, long TotalBytes);

    private bool HandleFileListAnnouncement(BinaryReader reader)
    {
        var token = ReadString(reader);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var itemCount = reader.ReadInt32();
        if (itemCount < 0)
        {
            return false;
        }

        for (var i = 0; i < itemCount; i++)
        {
            var kindValue = reader.ReadByte();
            if (!Enum.IsDefined(typeof(ClipboardFileEntryKind), kindValue))
            {
                return false;
            }

            var name = ReadString(reader);
            if (name == null)
            {
                return false;
            }
        }

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Received file list: items={itemCount} token={token}");
        }

        lock (_fileListLock)
        {
            _remoteFileToken = token;
            _pendingReceive = null;
        }

        return true;
    }

    private bool HandleFileTransferRequest(BinaryReader reader)
    {
        var token = ReadString(reader);
        var destination = ReadString(reader);
        if (string.IsNullOrWhiteSpace(token) || destination == null)
        {
            return false;
        }

        PendingFileList? local;
        lock (_fileListLock)
        {
            local = _localFileList;
        }

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Received transfer request: token={token} destination={destination} hasLocal={local != null}");
        }

        if (local == null || !string.Equals(local.Token, token, StringComparison.Ordinal))
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Transfer request token mismatch; sending empty payload.");
            }
            _ = Task.Run(() => SendEmptyTransferOnTransferChannel(token));
            return true;
        }

        _ = Task.Run(() => SendFileTransferDataOnTransferChannel(local));
        return true;
    }

    private void SendFileTransferDataOnTransferChannel(PendingFileList list)
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(_host, TransferPort);
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream);
            SendFileTransferData(list, writer);
        }
        catch (Exception ex)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Transfer channel send failed: {ex.Message}");
            }
        }
    }

    private void SendEmptyTransferOnTransferChannel(string token)
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(_host, TransferPort);
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream);
            SendEmptyFileTransfer(token, writer);
        }
        catch (Exception ex)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Transfer channel send empty failed: {ex.Message}");
            }
        }
    }

    private void RequestFileTransferOnTransferChannel(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(_host, TransferPort);
            using var stream = client.GetStream();
            using var reader = new BinaryReader(stream);
            using var writer = new BinaryWriter(stream);

            writer.Write((byte)MessageKind.Clipboard);
            writer.Write((int)ClipboardContentKind.FileTransferRequest);
            WriteString(writer, token);
            WriteString(writer, string.Empty);
            writer.Flush();

            var kind = (MessageKind)reader.ReadByte();
            if (kind != MessageKind.Clipboard)
            {
                return;
            }

            var contentKind = (ClipboardContentKind)reader.ReadInt32();
            if (contentKind != ClipboardContentKind.FileTransferData)
            {
                return;
            }

            HandleFileTransferData(reader);
        }
        catch (Exception ex)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Transfer channel request failed: {ex.Message}");
            }
        }
    }

    private bool HandleFileTransferData(BinaryReader reader)
    {
        var token = ReadString(reader);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var fileCount = reader.ReadInt32();
        var totalBytes = reader.ReadInt64();
        var entryCount = reader.ReadInt32();
        if (fileCount < 0 || totalBytes < 0 || entryCount < 0)
        {
            return false;
        }

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Received transfer data header: token={token} files={fileCount} entries={entryCount} bytes={totalBytes}");
        }

        PendingReceive? pending;
        lock (_fileListLock)
        {
            pending = _pendingReceive;
            if (pending == null || !string.Equals(pending.Token, token, StringComparison.Ordinal))
            {
                pending = null;
            }
            else
            {
                _pendingReceive = null;
            }
        }

        if (pending == null)
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Transfer data ignored: no pending receive.");
            }
            return SkipFileTransferData(reader, entryCount);
        }

        var tempRoot = PrepareReceiveTempRoot(token);
        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Receiving files into temp: {tempRoot}");
        }

        var success = ReceiveFileTransferData(reader, tempRoot, entryCount, fileCount);
        if (!success)
        {
            return false;
        }

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Transfer complete; dispatching temp payload: {tempRoot}");
        }

        FileTransferReceived?.Invoke(tempRoot);
        return true;
    }

    private static PendingFileList? BuildPendingFileList(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        var items = new List<ClipboardListItem>();
        var topLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (File.Exists(path))
            {
                var name = MakeUniqueName(GetItemName(path), topLevels);
                items.Add(new ClipboardListItem(path, ClipboardFileEntryKind.File, name));
                continue;
            }

            if (Directory.Exists(path))
            {
                var name = MakeUniqueName(GetItemName(path), topLevels);
                items.Add(new ClipboardListItem(path, ClipboardFileEntryKind.Directory, name));
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        return new PendingFileList(Guid.NewGuid().ToString("N"), items);
    }

    private bool SendEmptyFileTransfer(string token, BinaryWriter writer)
    {
        if (!TryBeginTransfer())
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Send empty transfer skipped: transfer already in progress.");
            }
            return false;
        }

        var success = false;
        try
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Sending empty transfer payload: token={token}");
            }
            writer.Write((byte)MessageKind.Clipboard);
            writer.Write((int)ClipboardContentKind.FileTransferData);
            WriteString(writer, token);
            writer.Write(0);
            writer.Write(0L);
            writer.Write(0);
            writer.Flush();
            success = true;
        }
        catch
        {
            success = false;
        }
        finally
        {
            EndTransfer();
        }

        return success;
    }

    private bool SendFileTransferData(PendingFileList list, BinaryWriter writer)
    {
        if (!TryBeginTransfer())
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Send transfer skipped: transfer already in progress.");
            }
            return false;
        }

        var stats = ComputeTransferStats(list);
        var currentFile = 0;
        var currentFileBytes = 0L;
        var currentFileTotal = 0L;

        try
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Sending transfer data: token={list.Token} entries={stats.EntryCount} files={stats.FileCount} bytes={stats.TotalBytes}");
            }
            writer.Write((byte)MessageKind.Clipboard);
            writer.Write((int)ClipboardContentKind.FileTransferData);
            WriteString(writer, list.Token);
            writer.Write(stats.FileCount);
            writer.Write(stats.TotalBytes);
            writer.Write(stats.EntryCount);
            writer.Flush();

            RaiseProgress(FileTransferDirection.Sending, currentFile, stats.FileCount, completed: false, currentFileBytes, currentFileTotal);

            EnumerateEntries(list, (kind, relativePath, sourcePath, size) =>
            {
                writer.Write((byte)kind);
                WriteString(writer, relativePath);
                if (kind == ClipboardFileEntryKind.Directory)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Send directory: {relativePath}");
                    }
                    return;
                }

                if (Program.VerboseFlag)
                {
                    Console.WriteLine($"Send file: {relativePath} ({size} bytes)");
                }
                writer.Write(size);
                currentFile++;
                currentFileBytes = 0;
                currentFileTotal = size;
                RaiseProgress(FileTransferDirection.Sending, currentFile, stats.FileCount, completed: false, currentFileBytes, currentFileTotal);
                using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!CopyStream(stream, writer, size, bytes =>
                    {
                        currentFileBytes += bytes;
                        RaiseProgress(FileTransferDirection.Sending, currentFile, stats.FileCount, completed: false, currentFileBytes, currentFileTotal);
                    }))
                {
                    throw new IOException("File transfer failed.");
                }
                RaiseProgress(FileTransferDirection.Sending, currentFile, stats.FileCount, completed: false, currentFileTotal, currentFileTotal);
            });

            writer.Flush();
            RaiseProgress(FileTransferDirection.Sending, stats.FileCount, stats.FileCount, completed: true, currentFileTotal, currentFileTotal);
            EndTransfer();
            return true;
        }
        catch
        {
            EndTransfer();
            return false;
        }
    }

    private static string GetItemName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return SanitizeFileName(name);
        }

        var root = Path.GetPathRoot(trimmed);
        name = string.IsNullOrWhiteSpace(root) ? "Item" : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        name = name.Replace(':', '_');
        return SanitizeFileName(name);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (Array.IndexOf(invalid, ch) >= 0)
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        var result = builder.ToString();
        return string.IsNullOrWhiteSpace(result) ? "Item" : result;
    }

    private static string MakeUniqueName(string name, HashSet<string> existing)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "Item" : name;
        var candidate = baseName;
        var index = 2;
        while (existing.Contains(candidate))
        {
            candidate = $"{baseName} ({index})";
            index++;
        }

        existing.Add(candidate);
        return candidate;
    }

    private static bool CopyStream(Stream source, BinaryWriter writer, long length, Action<int>? onChunk = null)
    {
        var buffer = new byte[ClipboardConstants.FileChunkSize];
        long remaining = length;
        while (remaining > 0)
        {
            var readSize = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, readSize);
            if (read <= 0)
            {
                return false;
            }

            writer.Write(buffer, 0, read);
            onChunk?.Invoke(read);
            remaining -= read;
        }

        return true;
    }

    private static TransferStats ComputeTransferStats(PendingFileList list)
    {
        var entryCount = 0;
        var fileCount = 0;
        var totalBytes = 0L;

        EnumerateEntries(list, (kind, _, __, size) =>
        {
            entryCount++;
            if (kind == ClipboardFileEntryKind.File)
            {
                fileCount++;
                totalBytes += size;
            }
        });

        return new TransferStats(entryCount, fileCount, totalBytes);
    }

    private static void EnumerateEntries(PendingFileList list, Action<ClipboardFileEntryKind, string, string, long> onEntry)
    {
        foreach (var item in list.Items)
        {
            if (item.Kind == ClipboardFileEntryKind.File)
            {
                if (!TryGetFileSize(item.SourcePath, out var size))
                {
                    continue;
                }

                onEntry(ClipboardFileEntryKind.File, item.DisplayName, item.SourcePath, size);
                continue;
            }

            if (item.Kind != ClipboardFileEntryKind.Directory)
            {
                continue;
            }

            onEntry(ClipboardFileEntryKind.Directory, item.DisplayName, item.SourcePath, 0);
            EnumerateDirectoryEntries(item.SourcePath, item.DisplayName, onEntry);
        }
    }

    private static void EnumerateDirectoryEntries(string rootPath, string rootName, Action<ClipboardFileEntryKind, string, string, long> onEntry)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var relative = Path.GetRelativePath(rootPath, directory);
                var relativePath = Path.Combine(rootName, relative);
                onEntry(ClipboardFileEntryKind.Directory, relativePath, directory, 0);
                stack.Push(directory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!TryGetFileSize(file, out var size))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(rootPath, file);
                var relativePath = Path.Combine(rootName, relative);
                onEntry(ClipboardFileEntryKind.File, relativePath, file, size);
            }
        }
    }

    private static bool TryGetFileSize(string path, out long size)
    {
        size = 0;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            size = info.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ReceiveFileTransferData(BinaryReader reader, string destinationRoot, int entryCount, int fileCount)
    {
        if (!TryBeginTransfer())
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Receive transfer skipped: transfer already in progress.");
            }
            return false;
        }

        var currentFile = 0;
        var success = false;
        RaiseProgress(FileTransferDirection.Receiving, currentFile, fileCount, completed: false, currentFileBytes: 0, totalFileBytes: 0);

        try
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Receiving transfer data: destination={destinationRoot} entries={entryCount} files={fileCount}");
            }
            for (var i = 0; i < entryCount; i++)
            {
                var kindValue = reader.ReadByte();
                if (!Enum.IsDefined(typeof(ClipboardFileEntryKind), kindValue))
                {
                    return false;
                }

                var kind = (ClipboardFileEntryKind)kindValue;
                var relativePath = ReadString(reader);
                if (relativePath == null)
                {
                    return false;
                }

                relativePath = NormalizeRelativePath(relativePath);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    return false;
                }

                if (!TryGetDestinationPath(destinationRoot, relativePath, out var destinationPath))
                {
                    return false;
                }

                if (kind == ClipboardFileEntryKind.Directory)
                {
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine($"Create directory: {destinationPath}");
                    }
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (kind != ClipboardFileEntryKind.File)
                {
                    return false;
                }

                var size = reader.ReadInt64();
                if (size < 0)
                {
                    return false;
                }

                if (Program.VerboseFlag)
                {
                    Console.WriteLine($"Receive file: {destinationPath} ({size} bytes)");
                }
                var parent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                currentFile++;
                var currentFileBytes = 0L;
                RaiseProgress(FileTransferDirection.Receiving, currentFile, fileCount, completed: false, currentFileBytes, size);
                if (!CopyFromReader(reader, fileStream, size, bytes =>
                    {
                        currentFileBytes += bytes;
                        RaiseProgress(FileTransferDirection.Receiving, currentFile, fileCount, completed: false, currentFileBytes, size);
                    }))
                {
                    return false;
                }

                RaiseProgress(FileTransferDirection.Receiving, currentFile, fileCount, completed: false, size, size);
            }

            RaiseProgress(FileTransferDirection.Receiving, fileCount, fileCount, completed: true, currentFileBytes: 0, totalFileBytes: 0);
            success = true;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            EndTransfer();
            if (!success)
            {
                RaiseProgress(FileTransferDirection.Receiving, currentFile, fileCount, completed: true, currentFileBytes: 0, totalFileBytes: 0);
            }
        }
    }

    private string PrepareReceiveTempRoot(string token)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "RemoteKM");
        var tempRoot = Path.Combine(basePath, token);
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
        catch
        {
        }

        try
        {
            Directory.CreateDirectory(tempRoot);
        }
        catch
        {
        }

        return tempRoot;
    }

    private bool SkipFileTransferData(BinaryReader reader, int entryCount)
    {
        try
        {
            for (var i = 0; i < entryCount; i++)
            {
                var kindValue = reader.ReadByte();
                if (!Enum.IsDefined(typeof(ClipboardFileEntryKind), kindValue))
                {
                    return false;
                }

                var kind = (ClipboardFileEntryKind)kindValue;
                var relativePath = ReadString(reader);
                if (relativePath == null)
                {
                    return false;
                }

                if (kind == ClipboardFileEntryKind.Directory)
                {
                    continue;
                }

                if (kind != ClipboardFileEntryKind.File)
                {
                    return false;
                }

                var size = reader.ReadInt64();
                if (size < 0)
                {
                    return false;
                }

                if (!SkipBytes(reader, size))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SkipBytes(BinaryReader reader, long length)
    {
        var buffer = new byte[ClipboardConstants.FileChunkSize];
        long remaining = length;
        while (remaining > 0)
        {
            var readSize = (int)Math.Min(buffer.Length, remaining);
            var read = reader.Read(buffer, 0, readSize);
            if (read <= 0)
            {
                return false;
            }

            remaining -= read;
        }

        return true;
    }

    private static bool CopyFromReader(BinaryReader reader, Stream target, long length, Action<int>? onChunk = null)
    {
        var buffer = new byte[ClipboardConstants.FileChunkSize];
        long remaining = length;
        while (remaining > 0)
        {
            var readSize = (int)Math.Min(buffer.Length, remaining);
            var read = reader.Read(buffer, 0, readSize);
            if (read <= 0)
            {
                return false;
            }

            target.Write(buffer, 0, read);
            onChunk?.Invoke(read);
            remaining -= read;
        }

        return true;
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        normalized = normalized.TrimStart(Path.DirectorySeparatorChar);
        return normalized.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool TryGetDestinationPath(string root, string relativePath, out string destinationPath)
    {
        destinationPath = string.Empty;
        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
        {
            rootFull += Path.DirectorySeparatorChar;
        }

        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        destinationPath = combined;
        return true;
    }

    private static string? ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            return null;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        if (bytes.Length > 0)
        {
            writer.Write(bytes);
        }
    }

    private bool TryBeginTransfer()
    {
        return Interlocked.CompareExchange(ref _transferInProgress, 1, 0) == 0;
    }

    private void EndTransfer()
    {
        Interlocked.Exchange(ref _transferInProgress, 0);
    }

    private void RaiseProgress(FileTransferDirection direction, int current, int total, bool completed, long currentFileBytes, long totalFileBytes)
    {
        FileTransferProgress?.Invoke(new FileTransferProgress(direction, current, total, completed, currentFileBytes, totalFileBytes));
    }

    public void Dispose()
    {
        ResetConnection();
    }
}
