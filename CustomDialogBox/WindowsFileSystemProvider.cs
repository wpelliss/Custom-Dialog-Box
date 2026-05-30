using System.IO;
using System.Threading;

namespace CustomDialogBox
{
    /// <summary>Default provider backed by the Windows NTFS file system.</summary>
    public sealed class WindowsFileSystemProvider : IFileSystemProvider
    {
        public FileSystemNodeViewModel[] GetChildren(string path, CancellationToken ct = default)
            => FileSystemService.EnumerateChildren(path, ct);

        public DriveInfo[] GetDrives() => DriveInfo.GetDrives();
    }
}
