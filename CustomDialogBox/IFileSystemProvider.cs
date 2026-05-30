using System.IO;
using System.Threading;

namespace CustomDialogBox
{
    /// <summary>
    /// Abstraction over the file system.
    /// Inject a test double for unit tests or a VFS/cloud implementation for extensions.
    /// </summary>
    public interface IFileSystemProvider
    {
        FileSystemNodeViewModel[] GetChildren(string path, CancellationToken ct = default);
        DriveInfo[]               GetDrives();
    }
}
