using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CustomDialogBox
{
    /// <summary>
    /// Centralises file-system enumeration with uniform filtering and error handling.
    /// All calls happen on a background thread (called from Task.Run).
    /// </summary>
    internal static class FileSystemService
    {
        /// <summary>
        /// Enumerates visible subdirectories and files of <paramref name="path"/>.
        /// Hidden, System, and ReparsePoint entries are excluded.
        /// Per-entry exceptions are swallowed; only top-level access failures propagate
        /// as an empty result (never crash the caller).
        /// </summary>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="ct"/> is cancelled mid-enumeration.
        /// </exception>
        public static FileSystemNodeViewModel[] EnumerateChildren(
            string path,
            CancellationToken ct = default)
        {
            var list = new List<FileSystemNodeViewModel>();

            DirectoryInfo dir;
            try { dir = new DirectoryInfo(path); }
            catch { return list.ToArray(); }

            // --- Directories first ---
            try
            {
                foreach (var d in dir.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        FileAttributes a = d.Attributes;
                        if ((a & FileAttributes.Hidden)       != 0) continue;
                        if ((a & FileAttributes.System)       != 0) continue;
                        if ((a & FileAttributes.ReparsePoint) != 0) continue;
                        list.Add(new FileSystemNodeViewModel(d));
                    }
                    catch { /* skip inaccessible entry */ }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException)        { }
            catch (DirectoryNotFoundException)  { }
            catch (IOException)                 { }

            // --- Files ---
            try
            {
                foreach (var f in dir.EnumerateFiles())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        FileAttributes a = f.Attributes;
                        if ((a & FileAttributes.Hidden)       != 0) continue;
                        if ((a & FileAttributes.System)       != 0) continue;
                        if ((a & FileAttributes.ReparsePoint) != 0) continue;
                        list.Add(new FileSystemNodeViewModel(f));
                    }
                    catch { /* skip */ }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException)        { }
            catch (DirectoryNotFoundException)  { }
            catch (IOException)                 { }

            return list.ToArray();
        }
    }
}
