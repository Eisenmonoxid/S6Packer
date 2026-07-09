using System;
using System.IO;
using System.Runtime.InteropServices;
using System.IO.Compression;

namespace S6Packer.Source
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct CompressedHeaderDefinition
	{
		public UInt32 CompressedSize;
		public UInt32 DecompressedSize;
        // Data follows (dynamic length)
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public unsafe struct BBAArchiveFileHeaderDefinition
	{
		public UInt32 CompressedSize;
		public UInt32 DecompressedSize;
		public fixed UInt32 Header[2];
	}

	internal class BBACompression(ReadOnlySpan<byte> Data)
    {
        private CompressedHeaderDefinition Definition = Utility.Parse<CompressedHeaderDefinition>(Data);
		public ref CompressedHeaderDefinition GetDefinition() => ref Definition;
		public static readonly int Size = Utility.GetSerializedSize<CompressedHeaderDefinition>();

		public static byte[] Decompress(byte[] Data, int DecompressedSize)
		{
			byte[] Buffer = new byte[DecompressedSize];
			using MemoryStream Input = new(Data);
			using ZLibStream Decompression = new(Input, CompressionMode.Decompress);

			int AmountOfBytesRead = 0;
			while (AmountOfBytesRead < DecompressedSize)
			{
				int AmountRead = Decompression.Read(Buffer, AmountOfBytesRead, DecompressedSize - AmountOfBytesRead);
				if (AmountRead == 0)
				{
					break;
				}

				AmountOfBytesRead += AmountRead;
			}

			if (AmountOfBytesRead != DecompressedSize)
			{
				throw new InvalidDataException($"Expected {DecompressedSize} bytes, got {AmountOfBytesRead}");
			}

			return Buffer;
		}

		public static byte[] Compress(ReadOnlySpan<byte> Data)
		{
			using MemoryStream Output = new();
            using ZLibStream Compression = new(Output, CompressionMode.Compress);

            Compression.Write(Data);
			Compression.Dispose();

			return Output.ToArray();
		}
    }
}
