using System;
using System.Runtime.InteropServices;

namespace S6Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public unsafe struct BBAHeaderDefinition
	{
		public fixed byte Header[3];
		public byte Version;
		public UInt32 Unknown;
		public UInt32 Size;
		public UInt32 EncryptionID;
	}
	
	internal class BBAHeader
	{
		private BBAHeaderDefinition Definition;
		public ref BBAHeaderDefinition GetDefinition() => ref Definition;
		public ReadOnlySpan<byte> Serialize() => Utility.Serialize(Definition);
		public static readonly int Size = Utility.GetSerializedSize<BBAHeaderDefinition>();

		public BBAHeader(ReadOnlySpan<byte> Data)
		{
			Definition = Utility.Parse<BBAHeaderDefinition>(Data);
		}

		public BBAHeader()
		{
			Definition = new BBAHeaderDefinition
			{
				Version = 4,
				Unknown = 5,
				Size = 64,
				EncryptionID = Crypt.S6_HEAD_CRYPTID
			};

			unsafe
			{
				fixed (byte* Pointer = Definition.Header)
				{
        			"BAF"u8.CopyTo(new Span<byte>(Pointer, 3));
				}
			}
		}
	}
}
