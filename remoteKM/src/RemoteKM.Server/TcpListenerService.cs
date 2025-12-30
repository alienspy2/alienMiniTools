using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RemoteKM.Server;

internal sealed class TcpListenerService
{
    private readonly IPAddress _ip;
    private readonly int _port;
    private readonly InputPlayer _player;
    private TcpListener? _listener;
    private TcpListener? _transferListener;
    private BinaryWriter? _writer;
    private readonly object _writeLock = new();
    private readonly object _fileListLock = new();
    private PendingFileList? _localFileList;
    private string? _remoteFileToken;
    private PendingReceive? _pendingReceive;
    private int _transferInProgress;

    internal event Action<IPEndPoint?>? ClientConnected;
    internal event Action<string>? ClipboardReceived;
    internal event Action<FileTransferProgress>? FileTransferProgress;
    internal event Action<string>? FileTransferReceived;

    internal TcpListenerService(IPAddress ip, int port, InputPlayer player)
    {
        _ip = ip;
        _port = port;
        _player = player;
    }

    private int TransferPort => _port + 1;

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

    internal async Task RunAsync(CancellationToken token)
    {
        _listener = new TcpListener(_ip, _port);
        _listener.Start();
        Console.WriteLine($"Listening on {_ip}:{_port}");

        _transferListener = new TcpListener(_ip, TransferPort);
        _transferListener.Start();
        Console.WriteLine($"Listening for transfers on {_ip}:{TransferPort}");
        _ = Task.Run(() => RunTransferListenerAsync(token));

        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }

            Console.WriteLine("Client connected.");
            ClientConnected?.Invoke(client.Client.RemoteEndPoint as IPEndPoint);
            client.NoDelay = true;
            await HandleClientAsync(client, token);
        }
    }

    internal void Stop()
    {
        _listener?.Stop();
        _transferListener?.Stop();
    }

    private async Task RunTransferListenerAsync(CancellationToken token)
    {
        if (_transferListener == null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _transferListener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }

            client.NoDelay = true;
            _ = Task.Run(() => HandleTransferClientAsync(client, token));
        }
    }

    private Task HandleTransferClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (token.Register(() => client.Close()))
        using (var stream = client.GetStream())
        using (var reader = new BinaryReader(stream))
        using (var writer = new BinaryWriter(stream))
        {
            try
            {
                var kind = (MessageKind)reader.ReadByte();
                if (kind != MessageKind.Clipboard)
                {
                    return Task.CompletedTask;
                }

                var contentKind = (ClipboardContentKind)reader.ReadInt32();
                if (contentKind == ClipboardContentKind.FileTransferRequest)
                {
                    HandleFileTransferRequestTransfer(reader, writer);
                    return Task.CompletedTask;
                }

                if (contentKind == ClipboardContentKind.FileTransferData)
                {
                    HandleFileTransferData(reader);
                    return Task.CompletedTask;
                }

                if (Program.VerboseFlag)
                {
                    Console.WriteLine($"Unexpected transfer payload: kind={contentKind}");
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (IOException ex)
            {
                if (Program.VerboseFlag)
                {
                    Console.WriteLine($"Transfer channel error: {ex.Message}");
                }
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (token.Register(() => client.Close()))
        using (var stream = client.GetStream())
        using (var reader = new BinaryReader(stream))
        using (var writer = new BinaryWriter(stream))
        {
            SetWriter(writer);

            void SendCaptureStop()
            {
                SendControl(ControlMessage.CaptureStop, 0);
            }

            void OnCaptureStop()
            {
                SendCaptureStop();
            }

            _player.CaptureStopRequested += OnCaptureStop;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var kind = (MessageKind)reader.ReadByte();
                    switch (kind)
                    {
                        case MessageKind.Keyboard:
                        {
                            var message = reader.ReadInt32();
                            var vkCode = reader.ReadInt32();
                            var scanCode = reader.ReadInt32();
                            var flags = reader.ReadInt32();
                            if (Program.VerboseFlag)
                            {
                                Console.WriteLine($"Recv Keyboard: msg=0x{message:X} vk={vkCode} scan={scanCode} flags=0x{flags:X}");
                            }
                            _player.PlayKeyboard(new KeyboardEvent(message, vkCode, scanCode, flags));
                            break;
                        }
                        case MessageKind.Mouse:
                        {
                            var message = reader.ReadInt32();
                            var x = reader.ReadInt32();
                            var y = reader.ReadInt32();
                            var mouseData = reader.ReadInt32();
                            var flags = reader.ReadInt32();
                            if (Program.VerboseFlag)
                            {
                                Console.WriteLine($"Recv Mouse: msg=0x{message:X} x={x} y={y} data={mouseData} flags=0x{flags:X}");
                            }
                            _player.PlayMouse(new MouseEvent(message, x, y, mouseData, flags));
                            break;
                        }
                        case MessageKind.Control:
                        {
                            var control = (ControlMessage)reader.ReadInt32();
                            var value = reader.ReadInt32();
                            if (control == ControlMessage.CaptureStart)
                            {
                                _player.BeginCapture((CaptureEdge)value);
                            }
                            else if (control == ControlMessage.CaptureStop)
                            {
                                _player.EndCapture();
                            }
                            break;
                        }
                        case MessageKind.Clipboard:
                        {
                            var contentKind = (ClipboardContentKind)reader.ReadInt32();
                            if (contentKind == ClipboardContentKind.Text)
                            {
                                var length = reader.ReadInt32();
                                if (length < 0 || length > ClipboardConstants.MaxBytes)
                                {
                                    if (Program.VerboseFlag)
                                    {
                                        Console.WriteLine($"Invalid clipboard payload: kind={contentKind} length={length}");
                                    }
                                    return Task.CompletedTask;
                                }

                                if (length == 0)
                                {
                                    break;
                                }

                                var bytes = reader.ReadBytes(length);
                                if (bytes.Length != length)
                                {
                                    return Task.CompletedTask;
                                }

                                var text = Encoding.UTF8.GetString(bytes);
                                ClipboardReceived?.Invoke(text);
                                break;
                            }

                            if (contentKind == ClipboardContentKind.FileList)
                            {
                                if (!HandleFileListAnnouncement(reader))
                                {
                                    return Task.CompletedTask;
                                }
                                break;
                            }

                        if (contentKind == ClipboardContentKind.FileTransferRequest)
                        {
                            if (!HandleFileTransferRequestControl(reader))
                            {
                                return Task.CompletedTask;
                            }
                            break;
                        }

                        if (contentKind == ClipboardContentKind.FileTransferData)
                        {
                            if (Program.VerboseFlag)
                            {
                                Console.WriteLine("File transfer data received on control channel; ignoring.");
                            }
                            return Task.CompletedTask;
                        }

                            if (Program.VerboseFlag)
                            {
                                Console.WriteLine($"Invalid clipboard payload: kind={contentKind}");
                            }
                            break;
                        }
                        default:
                            Console.WriteLine($"Unknown message: {kind}");
                            return Task.CompletedTask;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("Client disconnected.");
            }
            catch (IOException ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Console.WriteLine($"Connection error: {ex.Message}");
                }
            }
            finally
            {
                _player.CaptureStopRequested -= OnCaptureStop;
                SetWriter(null);
            }
        }
        return Task.CompletedTask;
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

        return SendClipboardBytes(bytes);
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

        lock (_writeLock)
        {
            if (_writer == null)
            {
                return false;
            }

            try
            {
                _writer.Write((byte)MessageKind.Clipboard);
                _writer.Write((int)ClipboardContentKind.FileList);
                WriteString(_writer, pending.Token);
                _writer.Write(pending.Items.Count);
                foreach (var item in pending.Items)
                {
                    _writer.Write((byte)item.Kind);
                    WriteString(_writer, item.DisplayName);
                }

                _writer.Flush();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
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

        lock (_writeLock)
        {
            if (_writer == null)
            {
                lock (_fileListLock)
                {
                    _pendingReceive = null;
                }
                return false;
            }

            try
            {
                if (Program.VerboseFlag)
                {
                    Console.WriteLine($"Requesting file transfer: token={token}");
                }
                _writer.Write((byte)MessageKind.Clipboard);
                _writer.Write((int)ClipboardContentKind.FileTransferRequest);
                WriteString(_writer, token);
                WriteString(_writer, string.Empty);
                _writer.Flush();
                return true;
            }
            catch (IOException)
            {
                lock (_fileListLock)
                {
                    _pendingReceive = null;
                }
                return false;
            }
            catch (ObjectDisposedException)
            {
                lock (_fileListLock)
                {
                    _pendingReceive = null;
                }
                return false;
            }
        }
    }

    private void SendControl(ControlMessage message, int value)
    {
        lock (_writeLock)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                _writer.Write((byte)MessageKind.Control);
                _writer.Write((int)message);
                _writer.Write(value);
                _writer.Flush();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private bool SendClipboardBytes(byte[] bytes)
    {
        lock (_writeLock)
        {
            if (_writer == null)
            {
                return false;
            }

            try
            {
                _writer.Write((byte)MessageKind.Clipboard);
                _writer.Write((int)ClipboardContentKind.Text);
                _writer.Write(bytes.Length);
                _writer.Write(bytes);
                _writer.Flush();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
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

    private bool HandleFileTransferRequestControl(BinaryReader reader)
    {
        var token = ReadString(reader);
        var destination = ReadString(reader);
        if (string.IsNullOrWhiteSpace(token) || destination == null)
        {
            return false;
        }

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Received transfer request (control): token={token} destination={destination}");
        }

        return true;
    }

    private bool HandleFileTransferRequestTransfer(BinaryReader reader, BinaryWriter writer)
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
            Console.WriteLine($"Received transfer request (channel): token={token} destination={destination} hasLocal={local != null}");
        }

        if (local == null || !string.Equals(local.Token, token, StringComparison.Ordinal))
        {
            if (Program.VerboseFlag)
            {
                Console.WriteLine("Transfer request token mismatch; sending empty payload.");
            }
            return SendEmptyFileTransfer(token, writer);
        }

        return SendFileTransferData(local, writer);
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

    private void SetWriter(BinaryWriter? writer)
    {
        lock (_writeLock)
        {
            _writer = writer;
        }

        if (writer == null)
        {
            ClearRemoteFileList();
            ClearLocalFileList();
        }
    }
}
