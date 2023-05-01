////////////////////////////////////////////////////////////////////////////
//
// GitReader - Lightweight Git local repository traversal library.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GitReader.Internal;

internal sealed class DeltaDecodedStream : Stream
#if NET45_OR_GREATER || NETSTANDARD || NETCOREAPP
    , IValueTaskStream
#endif
{
    private abstract class State
    {
    }

    private sealed class CopyState : State
    {
        public readonly uint Size;
        public uint Position;

        public CopyState(uint size) =>
            this.Size = size;
    }

    private sealed class InsertState : State
    {
        public readonly byte Size;
        public byte Position;

        public InsertState(byte size) =>
            this.Size = size;
    }

    private const int preloadBufferSize = 65536;

    private MemoizedStream baseObjectStream;
    private Stream deltaStream;
    private byte[] deltaBuffer;
    private int deltaBufferIndex;
    private int deltaBufferCount;

    private State? state;

    private DeltaDecodedStream(
        MemoizedStream baseObjectStream, Stream deltaStream,
        byte[] preloadBuffer, int preloadIndex, int preloadCount, long decodedObjectLength)
    {
        this.baseObjectStream = baseObjectStream;
        this.deltaStream = deltaStream;
        this.deltaBuffer = preloadBuffer;
        this.deltaBufferIndex = preloadIndex;
        this.deltaBufferCount = preloadCount;
        this.Length = decodedObjectLength;
    }

    public override long Length { get; }

    public override bool CanRead =>
        true;
    public override bool CanSeek =>
        false;
    public override bool CanWrite =>
        false;

#if !NETSTANDARD1_6
    public override void Close()
#else
    public void Close()
#endif
    {
        if (Interlocked.Exchange(ref this.baseObjectStream, null!) is { } baseObjectStream)
        {
            baseObjectStream.Dispose();
        }
        if (Interlocked.Exchange(ref this.deltaStream, null!) is { } deltaStream)
        {
            deltaStream.Dispose();
        }
        this.deltaBuffer = null!;
        this.state = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Close();
        }
    }

    private bool Prepare()
    {
        this.deltaBufferCount = this.deltaStream.Read(
            this.deltaBuffer, 0, this.deltaBuffer.Length);
        if (this.deltaBufferCount == 0)
        {
            return false;
        }

        this.deltaBufferIndex = 0;
        return true;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = 0;

        while (count >= 1)
        {
            if (this.state == null)
            {
                if (this.deltaBufferIndex >= this.deltaBufferCount)
                {
                    if (!this.Prepare())
                    {
                        break;
                    }
                }

                var opcode = this.deltaBuffer[this.deltaBufferIndex++];

                // Copy opcode
                if ((opcode & 0x80) != 0)
                {
                    var baseObjectOffset = 0U;
                    var baseObjectSize = 0U;

                    // offset1
                    if ((opcode & 0x01) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= this.deltaBuffer[this.deltaBufferIndex++];
                    }
                    // offset2
                    if ((opcode & 0x02) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 8);
                    }
                    // offset3
                    if ((opcode & 0x04) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 16);
                    }
                    // offset4
                    if ((opcode & 0x08) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 24);
                    }
                    // size1
                    if ((opcode & 0x10) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectSize |= this.deltaBuffer[this.deltaBufferIndex++];
                    }
                    // size2
                    if ((opcode & 0x20) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectSize |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 8);
                    }
                    // size3
                    if ((opcode & 0x40) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!this.Prepare())
                            {
                                break;
                            }
                        }
                        baseObjectSize |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 16);
                    }

                    this.baseObjectStream.Seek(
                        baseObjectOffset, SeekOrigin.Begin);

                    this.state = new CopyState(baseObjectSize);
                }
                // Insert opcode
                else
                {
                    // Size
                    var insertionSize = opcode & 0x7f;

                    this.state = new InsertState((byte)insertionSize);
                }
            }

            if (this.state is CopyState copyState)
            {
                var baseObjectRemains =
                    copyState.Size - copyState.Position;
                if (baseObjectRemains <= 0)
                {
                    this.state = null;
                    continue;
                }

                var length = Math.Min(baseObjectRemains, count);

                var r = this.baseObjectStream.Read(
                    buffer, offset, (int)length);

                offset += r;
                count -= r;
                read += r;

                copyState.Position += (uint)r;
            }
            else
            {
                var insertState = (InsertState)this.state;

                var insertionRemains =
                    insertState.Size - insertState.Position;
                if (insertionRemains <= 0)
                {
                    this.state = null;
                    continue;
                }

                var length = Math.Min(insertionRemains, count);

                while (length >= 1)
                {
                    Debug.Assert(length < byte.MaxValue);

                    if (this.deltaBufferIndex >= this.deltaBufferCount)
                    {
                        if (!this.Prepare())
                        {
                            break;
                        }
                    }

                    var l = Math.Min(
                        length, this.deltaBufferCount - this.deltaBufferIndex);

                    Array.Copy(
                        this.deltaBuffer, this.deltaBufferIndex,
                        buffer, offset, (int)l);

                    offset += l;
                    count -= l;
                    read += l;
                    this.deltaBufferIndex += l;

                    insertState.Position += (byte)l;
                    length -= l;
                }
            }
        }

        return read;
    }

#if !NET35 && !NET40
#if NET45_OR_GREATER || NETSTANDARD || NETCOREAPP
    private async ValueTask<bool> PrepareAsync(CancellationToken ct)
    {
        if (this.deltaStream is IValueTaskStream vts)
        {
            this.deltaBufferCount = await vts.ReadValueTaskAsync(
                this.deltaBuffer, 0, this.deltaBuffer.Length, ct);
        }
        else
        {
            this.deltaBufferCount = await this.deltaStream.ReadAsync(
                this.deltaBuffer, 0, this.deltaBuffer.Length, ct);
        }

        if (this.deltaBufferCount == 0)
        {
            return false;
        }

        this.deltaBufferIndex = 0;
        return true;
    }

    public async ValueTask<int> ReadValueTaskAsync(
        byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = 0;

        while (count >= 1)
        {
            if (this.state == null)
            {
                if (this.deltaBufferIndex >= this.deltaBufferCount)
                {
                    if (!await this.PrepareAsync(ct))
                    {
                        break;
                    }
                }

                var opcode = this.deltaBuffer[this.deltaBufferIndex++];

                // Copy opcode
                if ((opcode & 0x80) != 0)
                {
                    var baseObjectOffset = 0U;
                    var baseObjectSize = 0U;

                    // offset1
                    if ((opcode & 0x01) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= this.deltaBuffer[this.deltaBufferIndex++];
                    }
                    // offset2
                    if ((opcode & 0x02) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 8);
                    }
                    // offset3
                    if ((opcode & 0x04) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 16);
                    }
                    // offset4
                    if ((opcode & 0x08) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 24);
                    }
                    // size1
                    if ((opcode & 0x10) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectSize |= this.deltaBuffer[this.deltaBufferIndex++];
                    }
                    // size2
                    if ((opcode & 0x20) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectSize |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 8);
                    }
                    // size3
                    if ((opcode & 0x40) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectSize |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 16);
                    }

                    await this.baseObjectStream.SeekValueTaskAsync(
                        baseObjectOffset, SeekOrigin.Begin, ct);

                    this.state = new CopyState(baseObjectSize);
                }
                // Insert opcode
                else
                {
                    // Size
                    var insertionSize = opcode & 0x7f;

                    this.state = new InsertState((byte)insertionSize);
                }
            }

            if (this.state is CopyState copyState)
            {
                var baseObjectRemains =
                    copyState.Size - copyState.Position;
                if (baseObjectRemains <= 0)
                {
                    this.state = null;
                    continue;
                }

                var length = Math.Min(baseObjectRemains, count);

                Debug.Assert(length <= uint.MaxValue);

                var r = await this.baseObjectStream.ReadValueTaskAsync(
                    buffer, offset, (int)length, ct);

                offset += r;
                count -= r;
                read += r;

                copyState.Position += (uint)r;
            }
            else
            {
                var insertState = (InsertState)this.state;

                var insertionRemains =
                    insertState.Size - insertState.Position;
                if (insertionRemains <= 0)
                {
                    this.state = null;
                    continue;
                }

                var length = Math.Min(insertionRemains, count);

                while (length >= 1)
                {
                    Debug.Assert(length <= byte.MaxValue);

                    if (this.deltaBufferIndex >= this.deltaBufferCount)
                    {
                        if (!await this.PrepareAsync(ct))
                        {
                            break;
                        }
                    }

                    var l = Math.Min(
                        length, this.deltaBufferCount - this.deltaBufferIndex);

                    Array.Copy(
                        this.deltaBuffer, this.deltaBufferIndex,
                        buffer, offset, (int)l);

                    offset += l;
                    count -= l;
                    read += l;
                    this.deltaBufferIndex += l;

                    insertState.Position += (byte)l;
                    length -= l;
                }
            }
        }

        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct) =>
        this.ReadValueTaskAsync(buffer, offset, count, ct).AsTask();
#else
    private async Task<bool> PrepareAsync(CancellationToken ct)
    {
        this.deltaBufferCount = await this.deltaStream.ReadAsync(
            this.deltaBuffer, 0, this.deltaBuffer.Length, ct);
        if (this.deltaBufferCount == 0)
        {
            return false;
        }

        this.deltaBufferIndex = 0;
        return true;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = 0;

        while (count >= 1)
        {
            if (this.state == null)
            {
                if (this.deltaBufferIndex >= this.deltaBufferCount)
                {
                    if (!await this.PrepareAsync(ct))
                    {
                        break;
                    }
                }

                var opcode = this.deltaBuffer[this.deltaBufferIndex++];

                // Copy opcode
                if ((opcode & 0x80) != 0)
                {
                    var baseObjectOffset = 0U;
                    var baseObjectSize = 0U;

                    // offset1
                    if ((opcode & 0x01) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= this.deltaBuffer[this.deltaBufferIndex++];
                    }
                    // offset2
                    if ((opcode & 0x02) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 8);
                    }
                    // offset3
                    if ((opcode & 0x04) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 16);
                    }
                    // offset4
                    if ((opcode & 0x08) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectOffset |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 24);
                    }
                    // size1
                    if ((opcode & 0x10) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectSize |= this.deltaBuffer[this.deltaBufferIndex++];
                    }
                    // size2
                    if ((opcode & 0x20) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectSize |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 8);
                    }
                    // size3
                    if ((opcode & 0x40) != 0)
                    {
                        if (this.deltaBufferIndex >= this.deltaBufferCount)
                        {
                            if (!await this.PrepareAsync(ct))
                            {
                                break;
                            }
                        }
                        baseObjectSize |= ((uint)this.deltaBuffer[this.deltaBufferIndex++] << 16);
                    }

                    await this.baseObjectStream.SeekAsync(
                        baseObjectOffset, SeekOrigin.Begin, ct);

                    this.state = new CopyState(baseObjectSize);
                }
                // Insert opcode
                else
                {
                    // Size
                    var insertionSize = opcode & 0x7f;

                    this.state = new InsertState((byte)insertionSize);
                }
            }

            if (this.state is CopyState copyState)
            {
                var baseObjectRemains =
                    copyState.Size - copyState.Position;
                if (baseObjectRemains <= 0)
                {
                    this.state = null;
                    continue;
                }

                var length = Math.Min(baseObjectRemains, count);

                Debug.Assert(length <= uint.MaxValue);

                var r = await this.baseObjectStream.ReadAsync(
                    buffer, offset, (int)length, ct);

                offset += r;
                count -= r;
                read += r;

                copyState.Position += (uint)r;
            }
            else
            {
                var insertState = (InsertState)this.state;

                var insertionRemains =
                    insertState.Size - insertState.Position;
                if (insertionRemains <= 0)
                {
                    this.state = null;
                    continue;
                }

                var length = Math.Min(insertionRemains, count);

                while (length >= 1)
                {
                    Debug.Assert(length <= byte.MaxValue);

                    if (this.deltaBufferIndex >= this.deltaBufferCount)
                    {
                        if (!await this.PrepareAsync(ct))
                        {
                            break;
                        }
                    }

                    var l = Math.Min(
                        length, this.deltaBufferCount - this.deltaBufferIndex);

                    Array.Copy(
                        this.deltaBuffer, this.deltaBufferIndex,
                        buffer, offset, (int)l);

                    offset += l;
                    count -= l;
                    read += l;
                    this.deltaBufferIndex += l;

                    insertState.Position += (byte)l;
                    length -= l;
                }
            }
        }

        return read;
    }
#endif
#endif

    public override long Position
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public override void Flush() =>
        throw new NotImplementedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotImplementedException();

    public override void SetLength(long value) =>
        throw new NotImplementedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotImplementedException();

    public static async Task<DeltaDecodedStream> CreateAsync(
        Stream baseObjectStream, Stream deltaStream, CancellationToken ct)
    {
        void Throw(int step) =>
            throw new InvalidDataException(
                $"Could not parse the object. Step={step}");

        var preloadIndex = 0;
        var preloadBuffer = new byte[preloadBufferSize];

        var read = await deltaStream.ReadAsync(preloadBuffer, 0, preloadBuffer.Length, ct);
        if (read == 0)
        {
            Throw(1);
        }

        if (!ObjectAccessor.TryGetVariableSize(
            preloadBuffer, read, ref preloadIndex, out var baseObjectLength, 7))
        {
            Throw(2);
        }
        if (baseObjectLength > long.MaxValue)
        {
            Throw(3);
        }

        if (!ObjectAccessor.TryGetVariableSize(
            preloadBuffer, read, ref preloadIndex, out var decodedObjectLength, 7))
        {
            Throw(3);
        }
        if (decodedObjectLength > long.MaxValue)
        {
            Throw(3);
        }

        return new(
            MemoizedStream.Create(baseObjectStream, (long)baseObjectLength),
            deltaStream,
            preloadBuffer, preloadIndex, read, (long)decodedObjectLength);
    }
}
