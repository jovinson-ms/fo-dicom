// Copyright (c) 2012-2021 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.IO;
using FellowOakDicom.IO.Buffer;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.Tests
{
    [Collection("General")]
    public class DicomStreamTest
    {
        /// <summary>
        /// A toy example of a Stream with an internal buffer.
        /// </summary>
        private class ToyBufferedStream : MemoryStream
        {
            private int _bufferPosition;

            private int _bufferLength;

            public ToyBufferedStream(byte[] bytes) : base(bytes, 0, bytes.Length, false, true)
            {
                _bufferLength = bytes.Length / 2;
                _bufferPosition = 0;
            }

            /// <summary>
            /// Returns the lesser of buffer length or requested bytes. A simplified version of the corresponding
            /// method in Azure.Storage.LazyLoadingReadOnlyStream:
            /// https://github.com/Azure/azure-sdk-for-net/blob/59dbd87c84d9ebf09f9075ad30ee440a0c0a5917/sdk/storage/Azure.Storage.Common/src/Shared/LazyLoadingReadOnlyStream.cs#L167
            /// </summary>
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position == Length) return 0;

                if (_bufferPosition == _bufferLength)
                {
                    _bufferPosition = 0; // We've reached the end of the buffer, simulating retrieving new bytes
                }

                int remainingBytesInBuffer = _bufferLength - _bufferPosition;
                int bytesToWrite = Math.Min(remainingBytesInBuffer, count);

                Array.Copy(GetBuffer(), Position + _bufferPosition, buffer, offset, bytesToWrite);
                
                Position += bytesToWrite;
                _bufferPosition += bytesToWrite;

                return bytesToWrite;
            }
        }

        /// <summary>
        /// Updates the Data accessor of StreamByteBuffer to read the underlying Stream until no more bytes are returned.
        /// </summary>
        private class BufferingStreamByteBuffer : StreamByteBuffer
        {
            public BufferingStreamByteBuffer(Stream stream, long position, long length) : base(stream, position, length)
            {
            }

            public override byte[] Data
            {
                get
                {
                    if (!Stream.CanRead)
                    {
                        throw new DicomIoException("cannot read from stream - maybe closed");
                    }

                    byte[] data = new byte[Size];
                    Stream.Position = Position;

                    int totalRead = 0;
                    int read;
                    do
                    {
                        read = Stream.Read(data, totalRead, (int)Size - totalRead);
                        totalRead += read;
                    }
                    while (read != 0);

                    return data;
                }
            }
        }

        #region Unit tests

        [Fact]
        public async Task Open_PixelDataLargerThanStreamBuffer_ReturnsIncompleteData()
        {
            var size = 100;
            var bufferSize = 50;
            var bytes = Enumerable.Repeat(1, size).Select(i => (byte)i).ToArray();

            using (var bufferedStream = new ToyBufferedStream(bytes))
            {
                var streamByteBuffer = new StreamByteBuffer(bufferedStream, 0, size);
                var data = streamByteBuffer.Data; // on-demand access

                Assert.Equal((byte)1, data[0]); // bytes in buffer size have data
                Assert.Equal((byte)0, data[bufferSize]); // bytes after buffer size have no data
                Assert.Equal((byte)0, data[size - 1]);
            }
        }

        [Fact]
        public async Task Open_PixelDataLargerThanStreamBufferWithImprovements_ReturnsCompleteData()
        {
            var size = 100;
            var bufferSize = 50;
            var bytes = Enumerable.Repeat(1, size).Select(i => (byte)i).ToArray();

            using (var bufferedStream = new ToyBufferedStream(bytes))
            {
                var bufferingStreamByteBuffer = new BufferingStreamByteBuffer(bufferedStream, 0, size);
                var data = bufferingStreamByteBuffer.Data; // on-demand access

                Assert.Equal((byte)1, data[0]); // all bytes have data
                Assert.Equal((byte)1, data[bufferSize]);
                Assert.Equal((byte)1, data[size - 1]);
            }
        }

        #endregion
    }
}
