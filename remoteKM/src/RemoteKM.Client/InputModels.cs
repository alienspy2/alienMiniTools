namespace RemoteKM.Client;

internal enum MessageKind : byte
{
    Keyboard = 1,
    Mouse = 2,
    Control = 3,
    Clipboard = 4
}

internal enum ControlMessage : int
{
    CaptureStart = 1,
    CaptureStop = 2
}

internal enum ClipboardContentKind : int
{
    Text = 1,
    FileList = 2,
    FileTransferRequest = 3,
    FileTransferData = 4
}

internal static class ClipboardConstants
{
    internal const int MaxBytes = 1024 * 1024;
    internal const int FileChunkSize = 64 * 1024;
}

internal enum ClipboardFileEntryKind : byte
{
    File = 1,
    Directory = 2
}

internal enum FileTransferDirection
{
    Sending = 1,
    Receiving = 2
}

internal sealed record FileTransferProgress(
    FileTransferDirection Direction,
    int CurrentFileIndex,
    int TotalFiles,
    bool Completed,
    long CurrentFileBytes,
    long TotalFileBytes);

internal enum CaptureEdge : int
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 3,
    Bottom = 4
}

internal enum HotKeyModifiers
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

internal sealed record HotKeyConfig(int Modifiers, int VirtualKey);

internal sealed record KeyboardEvent(int Message, int VkCode, int ScanCode, int Flags);

internal sealed record MouseEvent(int Message, int X, int Y, int MouseData, int Flags);
