﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    internal partial class HttpConnection
    {
        private sealed class ChunkedEncodingReadStream : HttpContentReadStream
        {
            /// <summary>How long a chunk indicator is allowed to be.</summary>
            /// <remarks>
            /// While most chunks indicators will contain no more than ulong.MaxValue.ToString("X").Length characters,
            /// "chunk extensions" are allowed. We place a limit on how long a line can be to avoid OOM issues if an
            /// infinite chunk length is sent.  This value is arbitrary and can be changed as needed.
            /// </remarks>
            private const int MaxChunkBytesAllowed = 16*1024;
            /// <summary>How long a trailing header can be.  This value is arbitrary and can be changed as needed.</summary>
            private const int MaxTrailingHeaderLength = 16*1024;
            /// <summary>The number of bytes remaining in the chunk.</summary>
            private ulong _chunkBytesRemaining;
            /// <summary>The current state of the parsing state machine for the chunked response.</summary>
            private ParsingState _state = ParsingState.ExpectChunkHeader;

            public ChunkedEncodingReadStream(HttpConnection connection) : base(connection) { }

            public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Cancellation requested.
                    return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
                }

                if (_connection == null || destination.Length == 0)
                {
                    // Response body fully consumed or the caller didn't ask for any data.
                    return new ValueTask<int>(0);
                }

                // Try to consume from data we already have in the buffer.
                int bytesRead = ReadChunksFromConnectionBuffer(destination.Span);
                if (bytesRead > 0)
                {
                    return new ValueTask<int>(bytesRead);
                }

                // We may have just consumed the remainder of the response (with no actual data
                // available), so check again.
                if (_connection == null)
                {
                    Debug.Assert(_state == ParsingState.Done);
                    return new ValueTask<int>(0);
                }

                // Nothing available to consume.  Fall back to I/O.
                return ReadAsyncCore(destination, cancellationToken);
            }

            private async ValueTask<int> ReadAsyncCore(Memory<byte> destination, CancellationToken cancellationToken)
            {
                // Should only be called if ReadChunksFromConnectionBuffer returned 0.

                Debug.Assert(_connection != null);
                Debug.Assert(destination.Length > 0);

                CancellationTokenRegistration ctr = _connection.RegisterCancellation(cancellationToken);
                try
                {
                    while (true)
                    {
                        if (_connection == null)
                        {
                            // Fully consumed the response in ReadChunksFromConnectionBuffer.
                            return 0;
                        }

                        if (_state == ParsingState.ExpectChunkData &&
                            destination.Length >= _connection.ReadBufferSize &&
                            _chunkBytesRemaining >= (ulong)_connection.ReadBufferSize)
                        {
                            // As an optimization, we skip going through the connection's read buffer if both
                            // the remaining chunk data and the destination buffer are both at least as large
                            // as the connection buffer.  That avoids an unnecessary copy while still reading
                            // the maximum amount we'd otherwise read at a time.
                            Debug.Assert(_connection.RemainingBuffer.Length == 0);
                            int bytesRead = await _connection.ReadAsync(destination.Slice(0, (int)Math.Min((ulong)destination.Length, _chunkBytesRemaining)));
                            if (bytesRead == 0)
                            {
                                throw new IOException(SR.net_http_invalid_response);
                            }
                            _chunkBytesRemaining -= (ulong)bytesRead;
                            if (_chunkBytesRemaining == 0)
                            {
                                _state = ParsingState.ExpectChunkTerminator;
                            }
                            return bytesRead;
                        }

                        // We're only here if we need more data to make forward progress.
                        await _connection.FillAsync();

                        // Now that we have more, see if we can get any response data, and if
                        // we can we're done.
                        int bytesCopied = ReadChunksFromConnectionBuffer(destination.Span);
                        if (bytesCopied > 0)
                        {
                            return bytesCopied;
                        }
                    }
                }
                catch (Exception exc) when (ShouldWrapInOperationCanceledException(exc, cancellationToken))
                {
                    throw new OperationCanceledException(s_cancellationMessage, exc, cancellationToken);
                }
                finally
                {
                    ctr.Dispose();
                }
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                ValidateCopyToArgs(this, destination, bufferSize);

                return
                    cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) :
                    _connection == null ? Task.CompletedTask :
                    CopyToAsyncCore(destination, cancellationToken);
            }

            private async Task CopyToAsyncCore(Stream destination, CancellationToken cancellationToken)
            {
                CancellationTokenRegistration ctr = _connection.RegisterCancellation(cancellationToken);
                try
                {
                    while (true)
                    {
                        while (true)
                        {
                            ReadOnlyMemory<byte> bytesRead = ReadChunkFromConnectionBuffer(int.MaxValue);
                            if (bytesRead.Length == 0)
                            {
                                break;
                            }
                            await destination.WriteAsync(bytesRead, cancellationToken).ConfigureAwait(false);
                        }

                        if (_connection == null)
                        {
                            // Fully consumed the response.
                            return;
                        }

                        await _connection.FillAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exc) when (ShouldWrapInOperationCanceledException(exc, cancellationToken))
                {
                    throw new OperationCanceledException(s_cancellationMessage, exc, cancellationToken);
                }
                finally
                {
                    ctr.Dispose();
                }
            }

            private int ReadChunksFromConnectionBuffer(Span<byte> destination)
            {
                int totalBytesRead = 0;
                while (destination.Length > 0)
                {
                    ReadOnlyMemory<byte> bytesRead = ReadChunkFromConnectionBuffer(destination.Length);
                    Debug.Assert(bytesRead.Length <= destination.Length);
                    if (bytesRead.Length == 0)
                    {
                        break;
                    }

                    totalBytesRead += bytesRead.Length;
                    bytesRead.Span.CopyTo(destination);
                    destination = destination.Slice(bytesRead.Length);
                }
                return totalBytesRead;
            }

            private ReadOnlyMemory<byte> ReadChunkFromConnectionBuffer(int maxBytesToRead)
            {
                Debug.Assert(maxBytesToRead > 0);

                try
                {
                    ReadOnlySpan<byte> currentLine;
                    switch (_state)
                    {
                        case ParsingState.ExpectChunkHeader:
                            Debug.Assert(_chunkBytesRemaining == 0, $"Expected {nameof(_chunkBytesRemaining)} == 0, got {_chunkBytesRemaining}");

                            // Read the chunk header line.
                            _connection._allowedReadLineBytes = MaxChunkBytesAllowed;
                            if (!_connection.TryReadNextLine(out currentLine))
                            {
                                // Could not get a whole line, so we can't parse the chunk header.
                                return default;
                            }

                            // Parse the hex value from it.
                            if (!Utf8Parser.TryParse(currentLine, out ulong chunkSize, out int bytesConsumed, 'X'))
                            {
                                throw new IOException(SR.net_http_invalid_response);
                            }
                            _chunkBytesRemaining = chunkSize;

                            // If there's a chunk extension after the chunk size, validate it.
                            if (bytesConsumed != currentLine.Length)
                            {
                                ValidateChunkExtension(currentLine.Slice(bytesConsumed));
                            }

                            // Proceed to handle the chunk.  If there's data in it, go read it.
                            // Otherwise, finish handling the response.
                            if (chunkSize > 0)
                            {
                                _state = ParsingState.ExpectChunkData;
                                goto case ParsingState.ExpectChunkData;
                            }
                            else
                            {
                                _state = ParsingState.ConsumeTrailers;
                                goto case ParsingState.ConsumeTrailers;
                            }

                        case ParsingState.ExpectChunkData:
                            Debug.Assert(_chunkBytesRemaining > 0);

                            ReadOnlyMemory<byte> connectionBuffer = _connection.RemainingBuffer;
                            if (connectionBuffer.Length == 0)
                            {
                                return default;
                            }

                            int bytesToConsume = Math.Min(maxBytesToRead, (int)Math.Min((ulong)connectionBuffer.Length, _chunkBytesRemaining));
                            Debug.Assert(bytesToConsume > 0);

                            _connection.ConsumeFromRemainingBuffer(bytesToConsume);
                            _chunkBytesRemaining -= (ulong)bytesToConsume;
                            if (_chunkBytesRemaining == 0)
                            {
                                _state = ParsingState.ExpectChunkTerminator;
                            }

                            return connectionBuffer.Slice(0, bytesToConsume);

                        case ParsingState.ExpectChunkTerminator:
                            Debug.Assert(_chunkBytesRemaining == 0, $"Expected {nameof(_chunkBytesRemaining)} == 0, got {_chunkBytesRemaining}");

                            _connection._allowedReadLineBytes = MaxChunkBytesAllowed;
                            if (!_connection.TryReadNextLine(out currentLine))
                            {
                                return default;
                            }

                            if (currentLine.Length != 0)
                            {
                                ThrowInvalidHttpResponse();
                            }

                            _state = ParsingState.ExpectChunkHeader;
                            goto case ParsingState.ExpectChunkHeader;

                        case ParsingState.ConsumeTrailers:
                            Debug.Assert(_chunkBytesRemaining == 0, $"Expected {nameof(_chunkBytesRemaining)} == 0, got {_chunkBytesRemaining}");

                            while (true)
                            {
                                _connection._allowedReadLineBytes = MaxTrailingHeaderLength;
                                if (!_connection.TryReadNextLine(out currentLine))
                                {
                                    break;
                                }

                                if (currentLine.IsEmpty)
                                {
                                    _state = ParsingState.Done;
                                    _connection.CompleteResponse();
                                    _connection = null;
                                    break;
                                }
                            }

                            return default;

                        default:
                        case ParsingState.Done: // shouldn't be called once we're done
                            Debug.Fail($"Unexpected state: {_state}");
                            return default;
                    }
                }
                catch (Exception)
                {
                    // Ensure we don't try to read from the connection again (in particular, for draining)
                    _connection.Dispose();
                    _connection = null;
                    throw;
                }
            }

            private static void ValidateChunkExtension(ReadOnlySpan<byte> lineAfterChunkSize)
            {
                // Until we see the ';' denoting the extension, the line after the chunk size
                // must contain only tabs and spaces.  After the ';', anything goes.
                for (int i = 0; i < lineAfterChunkSize.Length; i++)
                {
                    byte c = lineAfterChunkSize[i];
                    if (c == ';')
                    {
                        break;
                    }
                    else if (c != ' ' && c != '\t') // not called out in the RFC, but WinHTTP allows it
                    {
                        throw new IOException(SR.net_http_invalid_response);
                    }
                }
            }

            private enum ParsingState : byte
            {
                ExpectChunkHeader,
                ExpectChunkData,
                ExpectChunkTerminator,
                ConsumeTrailers,
                Done
            }

            public override bool NeedsDrain => (_connection != null);

            public override async Task<bool> DrainAsync(int maxDrainBytes)
            {
                Debug.Assert(_connection != null);

                int drainedBytes = 0;
                while (true)
                {
                    drainedBytes += _connection.RemainingBuffer.Length;
                    while (true)
                    {
                        ReadOnlyMemory<byte> bytesRead = ReadChunkFromConnectionBuffer(int.MaxValue);
                        if (bytesRead.Length == 0)
                        {
                            break;
                        }
                    }

                    // When ReadChunkFromConnectionBuffer reads the final chunk, it will clear out _connection
                    // and return the connection to the pool.
                    if (_connection == null)
                    {
                        return true;
                    }

                    if (drainedBytes >= maxDrainBytes)
                    {
                        return false;
                    }

                    await _connection.FillAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
