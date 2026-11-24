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

		public static ArraySegment<byte> Decompress(byte[] Data, int DecompressedSize)
		{
			using MemoryStream Output = new(DecompressedSize);
			using MemoryStream Input = new(Data);
			using ZLibStream Decompression = new(Input, CompressionMode.Decompress);
			Decompression.CopyTo(Output);
			return new ArraySegment<byte>(Output.GetBuffer(), 0, checked((int)Output.Length));
		}

		public static ArraySegment<byte> Compress(ReadOnlySpan<byte> Data, CompressionLevel Level)
		{
			using MemoryStream Output = new();
			using (ZLibStream Compression = new(Output, Level, leaveOpen: true))
			{
				Compression.Write(Data);
			}

			return new ArraySegment<byte>(Output.GetBuffer(), 0, checked((int)Output.Length));
		}
    }
}
