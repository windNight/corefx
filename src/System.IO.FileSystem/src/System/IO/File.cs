// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    // Class for creating FileStream objects, and some basic file management
    // routines such as Delete, etc.
    public static class File
    {
        private static Encoding s_UTF8NoBOM;

        internal const int DefaultBufferSize = 4096;

        public static StreamReader OpenText(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            return new StreamReader(path);
        }

        public static StreamWriter CreateText(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            return new StreamWriter(path, append: false);
        }

        public static StreamWriter AppendText(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            return new StreamWriter(path, append: true);
        }


        // Copies an existing file to a new file. An exception is raised if the
        // destination file already exists. Use the 
        // Copy(string, string, boolean) method to allow 
        // overwriting an existing file.
        //
        // The caller must have certain FileIOPermissions.  The caller must have
        // Read permission to sourceFileName and Create
        // and Write permissions to destFileName.
        // 
        public static void Copy(string sourceFileName, string destFileName)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName), SR.ArgumentNull_FileName);
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName), SR.ArgumentNull_FileName);
            if (sourceFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(sourceFileName));
            if (destFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(destFileName));

            InternalCopy(sourceFileName, destFileName, false);
        }

        // Copies an existing file to a new file. If overwrite is 
        // false, then an IOException is thrown if the destination file 
        // already exists.  If overwrite is true, the file is 
        // overwritten.
        //
        // The caller must have certain FileIOPermissions.  The caller must have
        // Read permission to sourceFileName 
        // and Write permissions to destFileName.
        // 
        public static void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName), SR.ArgumentNull_FileName);
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName), SR.ArgumentNull_FileName);
            if (sourceFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(sourceFileName));
            if (destFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(destFileName));

            InternalCopy(sourceFileName, destFileName, overwrite);
        }

        /// <devdoc>
        ///    Note: This returns the fully qualified name of the destination file.
        /// </devdoc>
        internal static string InternalCopy(string sourceFileName, string destFileName, bool overwrite)
        {
            Debug.Assert(sourceFileName != null);
            Debug.Assert(destFileName != null);
            Debug.Assert(sourceFileName.Length > 0);
            Debug.Assert(destFileName.Length > 0);

            string fullSourceFileName = Path.GetFullPath(sourceFileName);
            string fullDestFileName = Path.GetFullPath(destFileName);

            FileSystem.CopyFile(fullSourceFileName, fullDestFileName, overwrite);

            return fullDestFileName;
        }


        // Creates a file in a particular path.  If the file exists, it is replaced.
        // The file is opened with ReadWrite access and cannot be opened by another 
        // application until it has been closed.  An IOException is thrown if the 
        // directory specified doesn't exist.
        //
        // Your application must have Create, Read, and Write permissions to
        // the file.
        // 
        public static FileStream Create(string path)
        {
            return Create(path, DefaultBufferSize);
        }

        // Creates a file in a particular path.  If the file exists, it is replaced.
        // The file is opened with ReadWrite access and cannot be opened by another 
        // application until it has been closed.  An IOException is thrown if the 
        // directory specified doesn't exist.
        //
        // Your application must have Create, Read, and Write permissions to
        // the file.
        // 
        public static FileStream Create(string path, int bufferSize)
        {
            return new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize);
        }

        public static FileStream Create(string path, int bufferSize, FileOptions options)
        {
            return new FileStream(path, FileMode.Create, FileAccess.ReadWrite,
                                  FileShare.None, bufferSize, options);
        }
 
        // Deletes a file. The file specified by the designated path is deleted.
        // If the file does not exist, Delete succeeds without throwing
        // an exception.
        // 
        // On NT, Delete will fail for a file that is open for normal I/O
        // or a file that is memory mapped.  
        // 
        // Your application must have Delete permission to the target file.
        // 
        public static void Delete(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            string fullPath = Path.GetFullPath(path);

            FileSystem.DeleteFile(fullPath);
        }


        // Tests if a file exists. The result is true if the file
        // given by the specified path exists; otherwise, the result is
        // false.  Note that if path describes a directory,
        // Exists will return true.
        //
        // Your application must have Read permission for the target directory.
        // 
        public static bool Exists(string path)
        {
            try
            {
                if (path == null)
                    return false;
                if (path.Length == 0)
                    return false;

                path = Path.GetFullPath(path);
                // After normalizing, check whether path ends in directory separator.
                // Otherwise, FillAttributeInfo removes it and we may return a false positive.
                // GetFullPath should never return null
                Debug.Assert(path != null, "File.Exists: GetFullPath returned null");
                if (path.Length > 0 && PathInternal.IsDirectorySeparator(path[path.Length - 1]))
                {
                    return false;
                }

                return FileSystem.FileExists(path);
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { } // Security can throw this on ":"
            catch (SecurityException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return false;
        }

        public static FileStream Open(string path, FileMode mode)
        {
            return Open(path, mode, (mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite), FileShare.None);
        }

        public static FileStream Open(string path, FileMode mode, FileAccess access)
        {
            return Open(path, mode, access, FileShare.None);
        }

        public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return new FileStream(path, mode, access, share);
        }

        internal static DateTimeOffset GetUtcDateTimeOffset(DateTime dateTime)
        {
            // File and Directory UTC APIs treat a DateTimeKind.Unspecified as UTC whereas 
            // ToUniversalTime treats this as local.
            if (dateTime.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }

            return dateTime.ToUniversalTime();
        }

        public static void SetCreationTime(string path, DateTime creationTime)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetCreationTime(fullPath, creationTime, asDirectory: false);
        }

        public static void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetCreationTime(fullPath, GetUtcDateTimeOffset(creationTimeUtc), asDirectory: false);
        }

        public static DateTime GetCreationTime(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetCreationTime(fullPath).LocalDateTime;
        }

        public static DateTime GetCreationTimeUtc(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetCreationTime(fullPath).UtcDateTime;
        }

        public static void SetLastAccessTime(string path, DateTime lastAccessTime)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetLastAccessTime(fullPath, lastAccessTime, asDirectory: false);
        }

        public static void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetLastAccessTime(fullPath, GetUtcDateTimeOffset(lastAccessTimeUtc), asDirectory: false);
        }

        public static DateTime GetLastAccessTime(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetLastAccessTime(fullPath).LocalDateTime;
        }

        public static DateTime GetLastAccessTimeUtc(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetLastAccessTime(fullPath).UtcDateTime;
        }

        public static void SetLastWriteTime(string path, DateTime lastWriteTime)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetLastWriteTime(fullPath, lastWriteTime, asDirectory: false);
        }

        public static void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetLastWriteTime(fullPath, GetUtcDateTimeOffset(lastWriteTimeUtc), asDirectory: false);
        }

        public static DateTime GetLastWriteTime(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetLastWriteTime(fullPath).LocalDateTime;
        }

        public static DateTime GetLastWriteTimeUtc(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetLastWriteTime(fullPath).UtcDateTime;
        }

        public static FileAttributes GetAttributes(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return FileSystem.GetAttributes(fullPath);
        }

        public static void SetAttributes(string path, FileAttributes fileAttributes)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.SetAttributes(fullPath, fileAttributes);
        }

        public static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public static FileStream OpenWrite(string path)
        {
            return new FileStream(path, FileMode.OpenOrCreate,
                                  FileAccess.Write, FileShare.None);
        }

        public static string ReadAllText(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return InternalReadAllText(path, Encoding.UTF8);
        }

        public static string ReadAllText(string path, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return InternalReadAllText(path, encoding);
        }

        private static string InternalReadAllText(string path, Encoding encoding)
        {
            Debug.Assert(path != null);
            Debug.Assert(encoding != null);
            Debug.Assert(path.Length > 0);

            using (StreamReader sr = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true))
                return sr.ReadToEnd();
        }

        public static void WriteAllText(string path, string contents)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.Write(contents);
            }
        }

        public static void WriteAllText(string path, string contents, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            using (StreamWriter sw = new StreamWriter(path, false, encoding))
            {
                sw.Write(contents);
            }
        }

        public static byte[] ReadAllBytes(string path)
        {
            // bufferSize == 1 used to avoid unnecessary buffer in FileStream
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1))
            {
                long fileLength = fs.Length;
                if (fileLength > int.MaxValue)
                    throw new IOException(SR.IO_FileTooLong2GB);

                int index = 0;
                int count = (int)fileLength;
                byte[] bytes = new byte[count];
                while (count > 0)
                {
                    int n = fs.Read(bytes, index, count);
                    if (n == 0)
                        throw Error.GetEndOfFile();
                    index += n;
                    count -= n;
                }
                return bytes;
            }
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path), SR.ArgumentNull_Path);
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            InternalWriteAllBytes(path, bytes);
        }

        private static void InternalWriteAllBytes(string path, byte[] bytes)
        {
            Debug.Assert(path != null);
            Debug.Assert(path.Length != 0);
            Debug.Assert(bytes != null);

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
            }
        }
        public static string[] ReadAllLines(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return InternalReadAllLines(path, Encoding.UTF8);
        }

        public static string[] ReadAllLines(string path, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return InternalReadAllLines(path, encoding);
        }

        private static string[] InternalReadAllLines(string path, Encoding encoding)
        {
            Debug.Assert(path != null);
            Debug.Assert(encoding != null);
            Debug.Assert(path.Length != 0);

            string line;
            List<string> lines = new List<string>();

            using (StreamReader sr = new StreamReader(path, encoding))
                while ((line = sr.ReadLine()) != null)
                    lines.Add(line);

            return lines.ToArray();
        }

        public static IEnumerable<string> ReadLines(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return ReadLinesIterator.CreateIterator(path, Encoding.UTF8);
        }

        public static IEnumerable<string> ReadLines(string path, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return ReadLinesIterator.CreateIterator(path, encoding);
        }

        public static void WriteAllLines(string path, string[] contents)
        {
            WriteAllLines(path, (IEnumerable<string>)contents);
        }

        public static void WriteAllLines(string path, IEnumerable<string> contents)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            InternalWriteAllLines(new StreamWriter(path), contents);
        }

        public static void WriteAllLines(string path, string[] contents, Encoding encoding)
        {
            WriteAllLines(path, (IEnumerable<string>)contents, encoding);
        }

        public static void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            InternalWriteAllLines(new StreamWriter(path, false, encoding), contents);
        }

        private static void InternalWriteAllLines(TextWriter writer, IEnumerable<string> contents)
        {
            Debug.Assert(writer != null);
            Debug.Assert(contents != null);

            using (writer)
            {
                foreach (string line in contents)
                {
                    writer.WriteLine(line);
                }
            }
        }

        public static void AppendAllText(string path, string contents)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            using (StreamWriter sw = new StreamWriter(path, append: true))
            {
                sw.Write(contents);
            }
        }

        public static void AppendAllText(string path, string contents, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            using (StreamWriter sw = new StreamWriter(path, true, encoding))
            {
                sw.Write(contents);
            }
        }

        public static void AppendAllLines(string path, IEnumerable<string> contents)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            InternalWriteAllLines(new StreamWriter(path, append: true), contents);
        }

        public static void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            InternalWriteAllLines(new StreamWriter(path, true, encoding), contents);
        }

        public static void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName)
        {
            Replace(sourceFileName, destinationFileName, destinationBackupFileName, ignoreMetadataErrors: false);
        }

        public static void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName, bool ignoreMetadataErrors)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName));
            if (destinationFileName == null)
                throw new ArgumentNullException(nameof(destinationFileName));

            FileSystem.ReplaceFile(
                Path.GetFullPath(sourceFileName), 
                Path.GetFullPath(destinationFileName),
                destinationBackupFileName != null ? Path.GetFullPath(destinationBackupFileName) : null,
                ignoreMetadataErrors);
        }

        // Moves a specified file to a new location and potentially a new file name.
        // This method does work across volumes.
        //
        // The caller must have certain FileIOPermissions.  The caller must
        // have Read and Write permission to 
        // sourceFileName and Write 
        // permissions to destFileName.
        // 
        public static void Move(string sourceFileName, string destFileName)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName), SR.ArgumentNull_FileName);
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName), SR.ArgumentNull_FileName);
            if (sourceFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(sourceFileName));
            if (destFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(destFileName));

            string fullSourceFileName = Path.GetFullPath(sourceFileName);
            string fullDestFileName = Path.GetFullPath(destFileName);

            if (!FileSystem.FileExists(fullSourceFileName))
            {
                throw new FileNotFoundException(SR.Format(SR.IO_FileNotFound_FileName, fullSourceFileName), fullSourceFileName);
            }

            FileSystem.MoveFile(fullSourceFileName, fullDestFileName);
        }

        public static void Encrypt(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            // TODO: Not supported on Unix or in WinRt, and the EncryptFile API isn't currently
            // available in OneCore.  For now, we just throw PNSE everywhere.  When the API is
            // available, we can put this into the FileSystem abstraction and implement it
            // properly for Win32.

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_FileEncryption);
        }

        public static void Decrypt(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            // TODO: Not supported on Unix or in WinRt, and the EncryptFile API isn't currently
            // available in OneCore.  For now, we just throw PNSE everywhere.  When the API is
            // available, we can put this into the FileSystem abstraction and implement it
            // properly for Win32.

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_FileEncryption);
        }

        // UTF-8 without BOM and with error detection. Same as the default encoding for StreamWriter.
        private static Encoding UTF8NoBOM => s_UTF8NoBOM ?? (s_UTF8NoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));

        // If we use the path-taking constructors we will not have FileOptions.Asynchronous set and
        // we will have asynchronous file access faked by the thread pool. We want the real thing.
        private static StreamReader AsyncStreamReader(string path, Encoding encoding)
        {
            FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        }

        private static StreamWriter AsyncStreamWriter(string path, Encoding encoding, bool append)
        {
            FileStream stream = new FileStream(
                path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, DefaultBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return new StreamWriter(stream, encoding);
        }

        public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
            => ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

        public static Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<string>(cancellationToken)
                : InternalReadAllTextAsync(path, encoding, cancellationToken);
        }

        private static async Task<string> InternalReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken)
        {
            Debug.Assert(!string.IsNullOrEmpty(path));
            Debug.Assert(encoding != null);

            char[] buffer = null;
            StringBuilder sb = null;
            StreamReader sr = AsyncStreamReader(path, encoding);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb = StringBuilderCache.Acquire();
                buffer = ArrayPool<char>.Shared.Rent(sr.CurrentEncoding.GetMaxCharCount(DefaultBufferSize));
                for (;;)
                {
                    int read = await sr.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return sb.ToString();
                    }

                    sb.Append(buffer, 0, read);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                sr.Dispose();
                if (buffer != null)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }

                if (sb != null)
                {
                    StringBuilderCache.Release(sb);
                }
            }
        }

        public static Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default(CancellationToken))
            => WriteAllTextAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (string.IsNullOrEmpty(contents))
            {
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read).Dispose();
                return Task.CompletedTask;
            }

            return InternalWriteAllTextAsync(AsyncStreamWriter(path, encoding, append: false), contents, cancellationToken);
        }

        public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<byte[]>(cancellationToken);
            }

            FileStream fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            bool returningInternalTask = false;
            try
            {
                long fileLength = fs.Length;
                if (cancellationToken.IsCancellationRequested)
                {
                    return Task.FromCanceled<byte[]>(cancellationToken);
                }

                if (fileLength > int.MaxValue)
                {
                    return Task.FromException<byte[]>(new IOException(SR.IO_FileTooLong2GB));
                }

                if (fileLength == 0)
                {
                    return Task.FromResult(Array.Empty<byte>());
                }

                returningInternalTask = true;
                return InternalReadAllBytesAsync(fs, (int)fileLength, cancellationToken);
            }
            finally
            {
                if (!returningInternalTask)
                {
                    fs.Dispose();
                }
            }
        }

        private static async Task<byte[]> InternalReadAllBytesAsync(FileStream fs, int count, CancellationToken cancellationToken)
        {
            using (fs)
            {
                int index = 0;
                byte[] bytes = new byte[count];
                do
                {
                    int n = await fs.ReadAsync(bytes, index, count - index, cancellationToken).ConfigureAwait(false);
                    if (n == 0)
                    {
                        throw Error.GetEndOfFile();
                    }

                    index += n;
                } while (index < count);

                return bytes;
            }
        }

        public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path), SR.ArgumentNull_Path);
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : InternalWriteAllBytesAsync(path, bytes, cancellationToken);
        }

        private static async Task InternalWriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            Debug.Assert(!string.IsNullOrEmpty(path));
            Debug.Assert(bytes != null);

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await fs.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public static Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
            => ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);

        public static Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<string[]>(cancellationToken)
                : InternalReadAllLinesAsync(path, encoding, cancellationToken);
        }

        private static async Task<string[]> InternalReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken)
        {
            Debug.Assert(!string.IsNullOrEmpty(path));
            Debug.Assert(encoding != null);

            using (StreamReader sr = AsyncStreamReader(path, encoding))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string line;
                List<string> lines = new List<string>();
                while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lines.Add(line);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return lines.ToArray();
            }
        }

        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default(CancellationToken))
            => WriteAllLinesAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : InternalWriteAllLinesAsync(AsyncStreamWriter(path, encoding, append: false), contents, cancellationToken);
        }

        private static async Task InternalWriteAllLinesAsync(TextWriter writer, IEnumerable<string> contents, CancellationToken cancellationToken)
        {
            Debug.Assert(writer != null);
            Debug.Assert(contents != null);

            using (writer)
            {
                foreach (string line in contents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Note that this working depends on the fix to #14563, and cannot be ported without
                    // either also porting that fix, or explicitly checking for line being null.
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }

        private static async Task InternalWriteAllTextAsync(StreamWriter sw, string contents, CancellationToken cancellationToken)
        {
            char[] buffer = null;
            try
            {
                buffer = ArrayPool<char>.Shared.Rent(DefaultBufferSize);
                int count = contents.Length;
                int index = 0;
                while (index < count)
                {
                    int batchSize = Math.Min(DefaultBufferSize, count - index);
                    contents.CopyTo(index, buffer, 0, batchSize);
                    cancellationToken.ThrowIfCancellationRequested();
                    await sw.WriteAsync(buffer, 0, batchSize).ConfigureAwait(false);
                    index += batchSize;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await sw.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                sw.Dispose();
                if (buffer != null)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }
            }
        }

        public static Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default(CancellationToken))
            => AppendAllTextAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task AppendAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (string.IsNullOrEmpty(contents))
            {
                // Just to throw exception if there is a problem opening the file.
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read).Dispose();
                return Task.CompletedTask;
            }

            return InternalWriteAllTextAsync(AsyncStreamWriter(path, encoding, append: true), contents, cancellationToken);
        }

        public static Task AppendAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default(CancellationToken))
            => AppendAllLinesAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task AppendAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : InternalWriteAllLinesAsync(AsyncStreamWriter(path, encoding, append: true), contents, cancellationToken);
        }
    }
}
