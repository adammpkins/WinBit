// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

namespace LibtorrentSharp.Enums;

/// <summary>
/// Identifies the libtorrent operation that triggered a peer disconnect, file
/// I/O failure, or other categorized error. Surfaced through error alerts that
/// carry an operation discriminator: <see cref="Alerts.FileErrorAlert"/>,
/// <see cref="Alerts.UdpErrorAlert"/>, and <see cref="Alerts.DhtErrorAlert"/>.
/// Values mirror libtorrent's <c>operation_t</c> from <c>libtorrent/operations.hpp</c>
/// in declaration order, so a numeric round-trip across the C ABI maps 1:1.
/// </summary>
public enum OperationType : byte
{
    /// <summary>The error was unexpected and it is unknown which operation caused it.</summary>
    Unknown = 0,

    /// <summary>BitTorrent logic determined to disconnect.</summary>
    Bittorrent,

    /// <summary>A call to ioctl() failed.</summary>
    IoControl,

    /// <summary>A call to getpeername() failed (querying the remote IP of a connection).</summary>
    GetPeerName,

    /// <summary>A call to getsockname() failed (querying the local IP of a connection).</summary>
    GetName,

    /// <summary>An attempt to allocate a receive buffer failed.</summary>
    AllocReceiveBuffer,

    /// <summary>An attempt to allocate a send buffer failed.</summary>
    AllocSendBuffer,

    /// <summary>Writing to a file failed.</summary>
    FileWrite,

    /// <summary>Reading from a file failed.</summary>
    FileRead,

    /// <summary>A non-read and non-write file operation failed.</summary>
    File,

    /// <summary>A socket write operation failed.</summary>
    SocketWrite,

    /// <summary>A socket read operation failed.</summary>
    SocketRead,

    /// <summary>A call to open() to create a socket failed.</summary>
    SocketOpen,

    /// <summary>A call to bind() on a socket failed.</summary>
    SocketBind,

    /// <summary>An attempt to query the number of bytes available on a socket failed.</summary>
    Available,

    /// <summary>A call related to BitTorrent protocol encryption failed.</summary>
    Encryption,

    /// <summary>An attempt to connect a socket failed.</summary>
    Connect,

    /// <summary>Establishing an SSL connection failed.</summary>
    SslHandshake,

    /// <summary>A connection failed to satisfy the bind interface setting.</summary>
    GetInterface,

    /// <summary>A call to listen() on a socket failed.</summary>
    SocketListen,

    /// <summary>An ioctl call to bind a socket to a specific network device failed.</summary>
    SocketBindToDevice,

    /// <summary>A call to accept() on a socket failed.</summary>
    SocketAccept,

    /// <summary>Converting a string into a valid network address failed.</summary>
    ParseAddress,

    /// <summary>Enumerating network devices or adapters failed.</summary>
    EnumInterfaces,

    /// <summary>Invoking stat() on a file failed.</summary>
    FileStat,

    /// <summary>Copying a file failed.</summary>
    FileCopy,

    /// <summary>Allocating storage for a file failed.</summary>
    FileFallocate,

    /// <summary>Creating a hard link failed.</summary>
    FileHardLink,

    /// <summary>Removing a file failed.</summary>
    FileRemove,

    /// <summary>Renaming a file failed.</summary>
    FileRename,

    /// <summary>Opening a file failed.</summary>
    FileOpen,

    /// <summary>Creating a directory failed.</summary>
    MakeDirectory,

    /// <summary>Checking fast-resume data against files on disk failed.</summary>
    CheckResume,

    /// <summary>An unknown C++ exception was caught.</summary>
    Exception,

    /// <summary>Allocating space for a piece in the cache failed.</summary>
    AllocCachePiece,

    /// <summary>Moving a part-file failed.</summary>
    PartFileMove,

    /// <summary>Reading from a part-file failed.</summary>
    PartFileRead,

    /// <summary>Writing to a part-file failed.</summary>
    PartFileWrite,

    /// <summary>A hostname lookup failed.</summary>
    HostnameLookup,

    /// <summary>Creating or reading a symlink failed.</summary>
    Symlink,

    /// <summary>Handshake with a peer or server failed.</summary>
    Handshake,

    /// <summary>Setting a socket option failed.</summary>
    SocketOption,

    /// <summary>Enumerating network routes failed.</summary>
    EnumRoute,

    /// <summary>Moving the read/write position in a file failed.</summary>
    FileSeek,

    /// <summary>An async wait operation on a timer failed.</summary>
    Timer,

    /// <summary>A call to mmap() (or its Windows counterpart) failed.</summary>
    FileMmap,

    /// <summary>A call to ftruncate() (or SetEndOfFile() on Windows) failed.</summary>
    FileTruncate,
}
