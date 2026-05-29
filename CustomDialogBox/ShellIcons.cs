using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CustomDialogBox
{
    /// <summary>
    /// Static helper that retrieves Shell file-type icons via SHGetFileInfo and
    /// surfaces them as frozen WPF ImageSource objects.  Results are cached by
    /// lower-cased extension (or the sentinel "__dir__" for directories) so the
    /// expensive P/Invoke round-trip happens at most once per distinct type.
    ///
    /// Threading:
    ///   - SHGetFileInfo requires a COM STA context.  All calls to QueryShell are
    ///     marshalled to the WPF Dispatcher (which runs on an STA thread) to avoid
    ///     silent failures or undefined behaviour when invoked from a ThreadPool
    ///     (MTA) thread.
    ///   - ConcurrentDictionary makes the cache itself thread-safe; each caller
    ///     that loses the GetOrAdd race will simply discard its redundant value and
    ///     use the winner's BitmapSource — harmless and correct.
    ///
    /// Cache policy:
    ///   - No size bound: the number of distinct file extensions on a system is
    ///     finite and small in practice (typically fewer than a few hundred), so
    ///     eviction is not implemented.  If a factory returns null (Shell
    ///     unavailable for that key), null is stored permanently; subsequent calls
    ///     for the same key will not retry even if the Shell becomes available
    ///     again.  This is an accepted trade-off: a restart recovers the state.
    /// </summary>
    public static class ShellIcons
    {
        // -----------------------------------------------------------------------
        // P/Invoke declarations
        // -----------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int    iIcon;
            public uint   dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string         pszPath,
            uint           dwFileAttributes,
            ref SHFILEINFO psfi,
            uint           cbSizeFileInfo,
            uint           uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // -----------------------------------------------------------------------
        // SHGFI flags
        // -----------------------------------------------------------------------

        private const uint SHGFI_ICON              = 0x000000100;
        private const uint SHGFI_SMALLICON         = 0x000000001;

        /// <summary>
        /// SHGFI_LARGEICON vaut intentionnellement 0 dans l'API Shell32 :
        /// c'est l'absence du flag SHGFI_SMALLICON qui selectionne la grande icone.
        /// La valeur nulle n'est pas un flag absent — c'est le comportement attendu.
        /// </summary>
        private const uint SHGFI_LARGEICON         = 0x000000000;

        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        // -----------------------------------------------------------------------
        // FILE_ATTRIBUTE flags
        // -----------------------------------------------------------------------

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL    = 0x00000080;

        /// <summary>
        /// Longueur maximale de chemin acceptee par SHGetFileInfo via P/Invoke.
        /// Au-dela de MAX_PATH (260), le comportement natif est indefini.
        /// </summary>
        private const int MAX_PATH = 260;

        // -----------------------------------------------------------------------
        // Cache — key: (lower-cased extension or "__dir__", largeIcon)
        // -----------------------------------------------------------------------

        private static readonly ConcurrentDictionary<(string, bool), BitmapSource> _cache =
            new ConcurrentDictionary<(string, bool), BitmapSource>();

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the Shell icon for a file identified by its extension
        /// (e.g. ".txt", ".docx").  The leading dot is optional.
        /// Pass <paramref name="largeIcon"/> = true for 32x32, false for 16x16.
        /// Returns <c>null</c> when the Shell cannot provide an icon.
        /// </summary>
        public static BitmapSource GetFileIcon(string extension, bool largeIcon = false)
        {
            if (string.IsNullOrEmpty(extension))
                return null;

            // Normalise: always lower-case, always starts with a dot.
            if (extension[0] != '.')
                extension = "." + extension;

            var key = (extension.ToLowerInvariant(), largeIcon);
            return _cache.GetOrAdd(key, k => CreateFileIcon(k.Item1, k.Item2));
        }

        /// <summary>
        /// Returns the Shell icon for a generic directory.
        /// Pass <paramref name="largeIcon"/> = true for 32x32, false for 16x16.
        /// Returns <c>null</c> when the Shell cannot provide an icon.
        /// </summary>
        public static BitmapSource GetDirectoryIcon(bool largeIcon = false)
        {
            var key = ("__dir__", largeIcon);
            return _cache.GetOrAdd(key, k => CreateDirectoryIcon(k.Item2));
        }

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        private static BitmapSource CreateFileIcon(string extension, bool largeIcon)
        {
            // Symbolic path — no filesystem access occurs because
            // SHGFI_USEFILEATTRIBUTES tells the Shell to trust dwFileAttributes
            // instead of resolving the path on disk.
            string fakePath = "file" + extension;
            uint sizeFlag   = largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON;

            return QueryShell(
                fakePath,
                FILE_ATTRIBUTE_NORMAL,
                SHGFI_ICON | sizeFlag | SHGFI_USEFILEATTRIBUTES);
        }

        private static BitmapSource CreateDirectoryIcon(bool largeIcon)
        {
            uint sizeFlag = largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON;

            return QueryShell(
                "folder",
                FILE_ATTRIBUTE_DIRECTORY,
                SHGFI_ICON | sizeFlag | SHGFI_USEFILEATTRIBUTES);
        }

        /// <summary>
        /// Core wrapper around SHGetFileInfo.
        ///
        /// STA requirement : SHGetFileInfo relies on COM (Shell namespace extensions)
        /// and must run on a Single-Threaded Apartment thread.  WPF's Dispatcher runs
        /// on an STA thread.  If this method is called from a ThreadPool (MTA) thread,
        /// execution is marshalled synchronously to the Dispatcher to prevent silent
        /// failures (IntPtr.Zero returns) or undefined native behaviour.
        ///
        /// Path-length guard : paths longer than MAX_PATH (260 chars) are rejected
        /// before the P/Invoke call because SHGetFileInfo behaviour is undefined
        /// beyond that limit.
        ///
        /// Converts the returned HICON into a frozen BitmapSource, then
        /// unconditionally destroys the HICON in a finally block to prevent GDI
        /// handle leaks.
        /// </summary>
        private static BitmapSource QueryShell(string path, uint fileAttributes, uint flags)
        {
            // Guard: path-length limit before reaching native code.
            if (path != null && path.Length >= MAX_PATH)
            {
                Debug.WriteLine($"[ShellIcons] QueryShell — chemin trop long ({path.Length} chars), appel SHGetFileInfo ignore.");
                return null;
            }

            // Guard: STA requirement — SHGetFileInfo doit s'executer sur le thread STA
            // du Dispatcher. Si on est deja sur ce thread, l'appel est direct.
            // Sinon on marshalle de facon synchrone pour eviter les echecs silencieux.
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return (BitmapSource)Application.Current.Dispatcher.Invoke(
                    (Func<BitmapSource>)(() => QueryShell(path, fileAttributes, flags)));
            }

            var  info       = new SHFILEINFO();
            uint structSize = (uint)Marshal.SizeOf(info);

            IntPtr result = SHGetFileInfo(path, fileAttributes, ref info, structSize, flags);

            // Guard 1: Shell unavailable.
            if (result == IntPtr.Zero)
                return null;

            // Guard 2: Shell returned a struct but produced no icon handle.
            // Happens on some obscure extensions where the Shell returns partial info.
            if (info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                BitmapSource bs = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // Freeze makes the BitmapSource immutable and free-threaded,
                // which is required for safe storage in the shared ConcurrentDictionary.
                bs.Freeze();

                return bs;
            }
            finally
            {
                // Covers both the success path and any exception inside the try block
                // (OOM, thread abort…).  The HICON never leaks.
                DestroyIcon(info.hIcon);
            }
        }
    }
}
