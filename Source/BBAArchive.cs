using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace S6Packer.Source
{
	internal class BBAArchiveFile
    {
		private readonly bool IsArchiveFileOrMap;
		private readonly BBAHeader GlobalHeader;
		private readonly BBADirectoryHeader GlobalDirectoryHeader;
		private readonly BBADirectory GlobalDirectory;
		private readonly BBADataEntry[] GlobalFileData;
		private readonly BBAFileHashTable GlobalHashTable;
		private readonly Stream ArchiveStream;
		private readonly BinaryReader GlobalArchiveReader;
		public void LinkHashTableEntriesToDataEntries() => GlobalHashTable.LinkHashTableEntriesToDataEntries(GlobalFileData.AsSpan());

		public BBAArchiveFile(Stream Archive, bool ArchiveFileOrMap)
		{
			ArchiveStream = Archive;
			IsArchiveFileOrMap = ArchiveFileOrMap;

			GlobalArchiveReader = new(ArchiveStream);
			ArchiveStream.Seek(GetDataStartOffset(GlobalArchiveReader), SeekOrigin.Begin);

			GlobalHeader = new BBAHeader(GlobalArchiveReader.ReadBytes(BBAHeader.Size));

			Span<byte> DirectoryHeader = GlobalArchiveReader.ReadBytes(BBADirectoryHeader.Size);
			Crypt.Decrypt(DirectoryHeader, GlobalHeader.GetDefinition().EncryptionID);
			GlobalDirectoryHeader = new BBADirectoryHeader(DirectoryHeader);

			GlobalArchiveReader.BaseStream.Position = GlobalDirectoryHeader.GetDefinition().Offset;
			Span<byte> FileDictionary = GlobalArchiveReader.ReadBytes((int)GlobalDirectoryHeader.GetDefinition().Length);
			Crypt.Decrypt(FileDictionary, GlobalDirectoryHeader.GetDefinition().EncryptionID);

			ref CompressedHeaderDefinition CompressedHeader = ref new BBACompression(FileDictionary).GetDefinition();
			ReadOnlySpan<byte> DecompressedHeader = BBACompression.Decompress(FileDictionary[BBACompression.Size..].ToArray(), (int)CompressedHeader.DecompressedSize);
			GlobalDirectory = new BBADirectory(DecompressedHeader);

			uint AmountOfFiles = GlobalDirectory.GetDefinition().NumberOfFiles;
			ReadOnlySpan<byte> DataEntriesSpan = DecompressedHeader[(int)(GlobalDirectory.GetDefinition().OffsetFileEntries + 4)..];
			GlobalFileData = ParseDataEntriesFromHeader(DataEntriesSpan, AmountOfFiles);

			ReadOnlySpan<byte> HashTableEntriesSpan = DecompressedHeader[(int)GlobalDirectory.GetDefinition().OffsetFileHashtable..];
			uint AmountOfHashTableEntries = MemoryMarshal.Read<uint>(HashTableEntriesSpan[..4]);

			GlobalHashTable = new BBAFileHashTable(AmountOfHashTableEntries);
			GlobalHashTable.ParseHashTableFromHeader(HashTableEntriesSpan[4..]);

			Console.WriteLine($"[INFO] Loaded Archive with {AmountOfFiles} files and {AmountOfHashTableEntries} HashTable entries.");
		}

		public BBAArchiveFile(Stream Archive, string FolderPath, string FileExtension)
		{
			ArchiveStream = Archive;
			ArchiveStream.Seek(0, SeekOrigin.Begin);

			IsArchiveFileOrMap = FileExtension == ".bba";

			DirEntry Root = TreeReader.ReadRootFolder(FolderPath);
			List<DirEntry> Entries = TreeReader.BuildLinearList(Root);

			GlobalFileData = new BBADataEntry[Entries.Count];

			uint HashTableSize = BBAFileHashTable.CalculateHashTableSize((uint)Entries.Count);
			GlobalHashTable = new BBAFileHashTable(HashTableSize);

			CalculateDirectoryOffsets(Entries, FolderPath);
			MemoryStream FileDataStream = PackFolderFilesIntoArchiveFile(Entries, FolderPath);
			WriteBBAArchiveFileMetadata(FileDataStream);
		}

		private UInt32 GetDataStartOffset(BinaryReader Reader)
		{
			byte[] ID = [0x52, 0x47, 0x4D, 0x48];
			byte[] Buffer = new byte[ID.Length];

			Reader.BaseStream.Seek(0, SeekOrigin.Begin);
			Reader.ReadExactly(Buffer);
			
			if (!ID.SequenceEqual(Buffer))
			{
				return 0x0;
			}

			Reader.BaseStream.Seek(0x8, SeekOrigin.Begin);
			UInt32 Offset = Reader.ReadUInt32();
			Reader.BaseStream.Seek(Offset, SeekOrigin.Begin);
			
			byte Element = Reader.ReadByte();
			return Element == 0x0 ? Offset + 0x1 : Offset;
		}

		private static BBADataEntry[] ParseDataEntriesFromHeader(ReadOnlySpan<byte> Data, uint AmountOfFiles)
		{
			BBADataEntry[] Entries = new BBADataEntry[AmountOfFiles];
			int FileEntryOffset = 0;
			for (uint i = 0; i < AmountOfFiles; i++)
			{
				BBADataEntry Element = new(Data[FileEntryOffset..]);
				Entries[i] = Element;
				FileEntryOffset += (int)BBADataEntry.GetPaddedSize(Element.GetDefinition().FileNameLength);
			}

			return Entries;
		}

		public void UnpackAllDataEntriesFromArchive(string ArchiveOutputDirectoryPath)
		{
			for (uint i = 0; i < GlobalFileData.Length; i++)
			{
				BBADataEntry Element = GlobalFileData[i];
				ref BBADataEntryDefinition Definition = ref Element.GetDefinition();
				string OutputPath = Path.Combine(ArchiveOutputDirectoryPath, Element.FileName).Replace('\\', Path.DirectorySeparatorChar);

				if (Definition.FileType == (uint)BBADataEntry.BBAArchiveFileType.Directory)
				{
					Directory.CreateDirectory(OutputPath);
				}
				else
				{
					FileStream CurrentFile;
					try
					{
						Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
						CurrentFile = File.OpenWrite(OutputPath);
					}
					catch (Exception ex)
					{
						Console.WriteLine("[ERROR] Could not open file for writing: " + OutputPath + ": " + ex.Message);
						continue;
					}
				    
					using BinaryWriter Writer = new(CurrentFile, Encoding.UTF8, false);
					if (Definition.CompressedFileSize != 0 || Definition.DecompressedFileSize != 0)
					{
						byte[] Data = ReadDataEntryFromArchive(Element);
						Writer.Write(Data);
					}

					new FileInfo(OutputPath)
					{
						LastWriteTimeUtc = Definition.TimeStamp == 0
							? DateTime.UtcNow
							: BBADataEntry.GetDateTimeFromTimeStamp(Definition.TimeStamp)
					};
				}
			}

			Console.WriteLine("\n[INFO] All files extracted to: " + ArchiveOutputDirectoryPath);
		}

		private void CalculateDirectoryOffsets(List<DirEntry> Entries, string FolderPath)
		{
			uint DirectoryOffset = 0;
			foreach (var Element in Entries)
			{
				Element.DirOffset = (int)DirectoryOffset;
				string SanitizedPath = Utility.SanitizePath(FolderPath, Path.Combine(FolderPath, Element.Path));
				DirectoryOffset += BBADataEntry.GetPaddedSizeByName(SanitizedPath);
			}
		}

		private void AddCompressedDataFileToStream(ReadOnlySpan<byte> Data, MemoryStream FileDataStream, BBADataEntry Entry, UInt32 FileOffset)
		{
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();
			int FHOffset = sizeof(UInt32) * 2; // 8 bytes for compressed file header
			Span<byte> DummyBuffer = new byte[FHOffset];

			byte[] CompressedData = BBACompression.Compress(Data);
			Definition.CompressedFileSize = (uint)(CompressedData.Length + FHOffset);

			FileDataStream.Write(DummyBuffer);
			FileDataStream.Write(CompressedData.AsSpan());
			FileDataStream.Seek(FileOffset, SeekOrigin.Begin);

			// Write File Header
			byte[] FileHeader = WriteFileHeader(ref Definition, CompressedData.AsSpan()[..FHOffset]);
			FileDataStream.Write(FileHeader);

			Definition.CompressedCRC32 = Utility.GetCombinedCRC32OfData(FileHeader, CompressedData.AsSpan());
		}

		private (BBADataEntry, byte[]) CreateBBADataEntryWrapper(string FolderPath, DirEntry Element, uint Offset)
		{
			byte[] Data;
			BBADataEntry Entry;

			string ElementPath = Path.Combine(FolderPath, Element.Path);
			if (!File.Exists(ElementPath) && Directory.Exists(ElementPath))
			{
				Entry = CreateBBADataEntryForDirectory(FolderPath, ElementPath);
				AddToFileData(Entry, Offset, Element);
				return (Entry, null);
			}

			try
			{
				Data = File.ReadAllBytes(ElementPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[ERROR] File " + ElementPath + " could not be opened! " + ex.Message);
				return (null, null);
			}

			Entry = CreateBBADataEntry(Data, FolderPath, ElementPath);
			AddToFileData(Entry, Offset, Element);

			return (Entry, Data);
		}

		public MemoryStream PackFolderFilesIntoArchiveFile(List<DirEntry> Entries, string FolderPath)
        {
            uint Offset = 0;
			uint FileOffset = 0;
			MemoryStream FileDataStream = new();

			foreach (var Element in Entries)
			{
				(BBADataEntry Entry, ReadOnlySpan<byte> Data) = CreateBBADataEntryWrapper(FolderPath, Element, Offset++);
				if (Entry == null || Entry.GetDefinition().FileType == (uint)BBADataEntry.BBAArchiveFileType.Directory)
				{
					continue;
				}

				ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();
				FileDataStream.Seek(FileOffset, SeekOrigin.Begin);

				if (Definition.FileType == (uint)BBADataEntry.BBAArchiveFileType.Compressed)
				{
					AddCompressedDataFileToStream(Data, FileDataStream, Entry, FileOffset);
				}
				else
				{
					Definition.CompressedFileSize = Definition.DecompressedFileSize;
					Definition.CompressedCRC32 = Definition.DecompressedCRC32;
					FileDataStream.Write(Data);
				}

				Definition.FileOffset = FileOffset;
				FileOffset += Definition.CompressedFileSize;
			}

			return FileDataStream;
        }

		private void AddToFileData(BBADataEntry Entry, uint Offset, DirEntry Element)
		{
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();
			Definition.FirstChild = Element.FirstChild?.DirOffset ?? -1;
			Definition.NextSibling = Element.NextSibling?.DirOffset ?? -1;
			GlobalFileData[Offset] = Entry;
		}

		private byte[] WriteFileHeader(ref BBADataEntryDefinition Definition, ReadOnlySpan<byte> Data)
		{
			int USize = sizeof(UInt32);
			uint EntryDef = Unsafe.As<BBADataEntryDefinition, uint>(ref Definition);

			BBAArchiveFileHeaderDefinition FileHeader = new()
			{
				CompressedSize = Definition.CompressedFileSize - 8,
				DecompressedSize = Definition.DecompressedFileSize
			};

			Span<uint> Entry = MemoryMarshal.CreateSpan(ref EntryDef, USize); 
			Span<BBAArchiveFileHeaderDefinition> FileHeaderDef = MemoryMarshal.CreateSpan(ref FileHeader, 1);
            Span<UInt32> Header = MemoryMarshal.Cast<BBAArchiveFileHeaderDefinition, UInt32>(FileHeaderDef);

			Header[2] = BinaryPrimitives.ReadUInt32LittleEndian(Data[.. USize]);
			Header[3] = BinaryPrimitives.ReadUInt32LittleEndian(Data[USize .. (USize * 2)]);

			Crypt.EncryptFileHeader(Header, Entry);
			return MemoryMarshal.AsBytes(Header).ToArray();
		}

		private BBADataEntry CreateBBADataEntry(ReadOnlySpan<byte> Data, string FolderPath, string ElementPath)
		{
			UInt32 Value = Utility.GetCRC32OfData(Data);
			string SanitizedPath = Utility.SanitizePath(FolderPath, ElementPath);

			BBADataEntry DataEntry = new(SanitizedPath);
			ref BBADataEntryDefinition Definition = ref DataEntry.GetDefinition();

			Definition.TimeStamp = Utility.GetLastWriteTimeFromFileOrDirectory(ElementPath);
			Definition.DecompressedFileSize = (uint)Data.Length;
			Definition.DecompressedCRC32 = Value;
			Definition.CompressedFileSize = Definition.DecompressedFileSize;
			Definition.CompressedCRC32 = Definition.DecompressedCRC32;
			Definition.FileType = BBADataEntry.IsCompressed(Path.GetExtension(ElementPath)) 
				? (uint)BBADataEntry.BBAArchiveFileType.Compressed : (uint)BBADataEntry.BBAArchiveFileType.None;
			Definition.FileNameLength = (uint)SanitizedPath.Length;
			Definition.FileNameOffset = (uint)(SanitizedPath.LastIndexOf('\\') + 1);

			return DataEntry;
        }

		private BBADataEntry CreateBBADataEntryForDirectory(string FolderPath, string ElementPath)
		{
			string SanitizedPath = Utility.SanitizePath(FolderPath, ElementPath);

			BBADataEntry DataEntry = new(SanitizedPath);
			ref BBADataEntryDefinition Definition = ref DataEntry.GetDefinition();

			Definition.TimeStamp = Utility.GetLastWriteTimeFromFileOrDirectory(ElementPath);
			Definition.DecompressedFileSize = 0;
			Definition.DecompressedCRC32 = 0;
			Definition.CompressedFileSize = 0;
			Definition.CompressedCRC32 = 0;
			Definition.FileType = (uint)BBADataEntry.BBAArchiveFileType.Directory;
			Definition.FileNameLength = (uint)SanitizedPath.Length;
			Definition.FileNameOffset = (uint)(SanitizedPath.LastIndexOf('\\') + 1);

			return DataEntry;
        }

		private byte[] ReadDataEntryFromArchive(BBADataEntry DataEntry)
		{
			ref BBADataEntryDefinition Definition = ref DataEntry.GetDefinition();

			GlobalArchiveReader.BaseStream.Position = Definition.FileOffset;
			if (Definition.FileType == (uint)BBADataEntry.BBAArchiveFileType.Compressed)
			{
				return DecompressDataEntryFromArchive(ref Definition);
			}
			else if (Definition.FileType == (uint)BBADataEntry.BBAArchiveFileType.ChunkedCompressed)
			{
				return DecompressChunkedDataEntryFromArchive(ref Definition);
			}
			else
			{
				int Size = (int)Definition.CompressedFileSize;
				if (Size == 0)
				{
					Size = (int)Definition.DecompressedFileSize;
				}
				
				return GlobalArchiveReader.ReadBytes(Size);
			}
		}

		private byte[] DecompressChunkedDataEntryFromArchive(ref BBADataEntryDefinition Definition)
		{
			int EncodedSize = (int)Definition.CompressedFileSize;
			byte[] EncodedData = GlobalArchiveReader.ReadBytes(EncodedSize);
			using MemoryStream Memory = new((int)Definition.DecompressedFileSize);

			int Offset = 0;
			while (Offset < EncodedData.Length)
			{
				uint EncodedChunkSize = BinaryPrimitives.ReadUInt32LittleEndian(EncodedData.AsSpan(Offset, sizeof(uint)));
				Offset += sizeof(uint);
				byte[] EncodedChunk = EncodedData.AsSpan(Offset, (int)EncodedChunkSize).ToArray();
				Offset += (int)EncodedChunkSize;
				Span<uint> Header = MemoryMarshal.Cast<byte, uint>(EncodedChunk.AsSpan(0, sizeof(uint) * 4));
				Crypt.DecryptChunkHeader(Header);

				uint CompressedChunkSize = Header[0];
				uint DecompressedChunkSize = Header[1];
				byte[] DecompressedChunk = BBACompression.Decompress(EncodedChunk.AsSpan(sizeof(uint) * 2, 
					(int)CompressedChunkSize).ToArray(), (int)DecompressedChunkSize);
				Memory.Write(DecompressedChunk);
			}

			return new ArraySegment<byte>(Memory.GetBuffer(), 0, checked((int)Memory.Length)).Array;
		}

		private byte[] DecompressDataEntryFromArchive(ref BBADataEntryDefinition Definition)
		{
			const int USize = sizeof(UInt32);
			int FileHeaderSize = Utility.GetSerializedSize<BBAArchiveFileHeaderDefinition>();
			const uint FileDataHeaderSize = sizeof(uint) * 2;
			byte[] CurrentFileHeader = new byte[FileHeaderSize];

			GlobalArchiveReader.Read(CurrentFileHeader, 0, FileHeaderSize);

			Span<uint> Data = MemoryMarshal.Cast<byte, uint>(CurrentFileHeader.AsSpan());
			Span<uint> Entry = MemoryMarshal.CreateSpan(ref Unsafe.As<BBADataEntryDefinition, uint>(ref Definition), USize);

			Crypt.DecryptFileHeader(Data, Entry);
			GlobalArchiveReader.BaseStream.Position = Definition.FileOffset + (USize * 2); // Skip first two uints of file header

			int CompressedDataSize = checked((int)(Definition.CompressedFileSize - FileDataHeaderSize));
			byte[] Source = GlobalArchiveReader.ReadBytes(CompressedDataSize);

			BinaryPrimitives.WriteUInt32LittleEndian(Source.AsSpan()[.. USize], Data[2]);
			BinaryPrimitives.WriteUInt32LittleEndian(Source.AsSpan()[USize .. (USize * 2)], Data[3]);

			return BBACompression.Decompress(Source, (int)Definition.DecompressedFileSize);
		}

		private byte[] GetSerializedFileData()
		{
			using MemoryStream Memory = new();
			using BinaryWriter Writer = new(Memory);

			uint FileEntryOffset = (uint)(BBAHeader.Size + BBADirectoryHeader.Size);
			for (int i = 0; i < GlobalFileData.Length; i++)
			{
				BBADataEntry DataEntry = GlobalFileData[i];
				ref BBADataEntryDefinition Definition = ref DataEntry.GetDefinition();

				if (Definition.FileType != (uint)BBADataEntry.BBAArchiveFileType.Directory)
				{
					Definition.FileOffset += FileEntryOffset;
				}

				Writer.Write(DataEntry.Serialize());
			}

			return new ArraySegment<byte>(Memory.GetBuffer(), 0, checked((int)Memory.Length)).Array;
		}

		private byte[] GetDecompressedHeaderData(bool IsBBAFile, Span<byte> SerializedFileData, Span<byte> SerializedHashTableData)
		{
			using MemoryStream Memory = new();
			using BinaryWriter Writer = new(Memory);

			Writer.BaseStream.Seek(BBADirectory.Size, SeekOrigin.Begin);
			Writer.Write(SerializedFileData);

			uint HashTableOffset = (uint)Writer.BaseStream.Position;
			Writer.Write(SerializedHashTableData.Length / Utility.GetSerializedSize<BBAArchiveHashTableEntryDefinition>());
			Writer.Write(SerializedHashTableData);

			BBADirectory Directory = new(IsBBAFile, HashTableOffset, (uint)GlobalFileData.Length);
			Writer.BaseStream.Seek(0, SeekOrigin.Begin);
			Writer.Write(Directory.Serialize());

			return new ArraySegment<byte>(Memory.GetBuffer(), 0, checked((int)Memory.Length)).Array;
		}

		private byte[] GetEncryptedFileDictionary(bool IsBBAFile, byte[] CompressedHeaderBytes, Span<byte> CompressedHeaderData)
		{
			byte[] FileDictionary = [..CompressedHeaderBytes, ..CompressedHeaderData];

			int Length = FileDictionary.Length;
			int Padding = Length % 4;
			if (Padding != 0)
			{
				Padding = 4 - Padding;
			}

			Array.Resize(ref FileDictionary, Length + Padding);
			Crypt.Encrypt(FileDictionary, IsBBAFile ? Crypt.S6_BBA_CRYPTID : Crypt.S6_MAP_CRYPTID);

			return FileDictionary;
		}

		private void WriteBBAArchiveFileMetadata(MemoryStream FileDataStream)
		{
			Span<byte> SerializedHashTableData = GlobalHashTable.SerializeHashTable(GlobalFileData);
			Span<byte> SerializedFileData = GetSerializedFileData();
			Span<byte> DecompressedHeaderData = GetDecompressedHeaderData(IsArchiveFileOrMap, SerializedFileData, SerializedHashTableData);
			Span<byte> CompressedHeaderData = BBACompression.Compress(DecompressedHeaderData);

			CompressedHeaderDefinition CompressedHeader = new()
			{
				DecompressedSize = (uint)DecompressedHeaderData.Length,
				CompressedSize = (uint)CompressedHeaderData.Length,
			};

			byte[] CompressedHeaderBytes = new byte[BBACompression.Size];
			MemoryMarshal.Write(CompressedHeaderBytes, in CompressedHeader);
			Span<byte> FileDictionaryData = GetEncryptedFileDictionary(IsArchiveFileOrMap, CompressedHeaderBytes, CompressedHeaderData);
			uint CRC32 = Utility.GetCRC32OfData(FileDictionaryData);

			UInt32 Offset = (uint)(BBAHeader.Size + BBADirectoryHeader.Size + FileDataStream.Length);
			BBADirectoryHeader DirectoryHeader = new(IsArchiveFileOrMap, Offset, (uint)FileDictionaryData.Length, CRC32);

			BBAHeader Header = new();

			Span<byte> SerializedDirectoryHeader = DirectoryHeader.Serialize();
			Crypt.Encrypt(SerializedDirectoryHeader, Header.GetDefinition().EncryptionID);

			ArchiveStream.Seek(0, SeekOrigin.Begin);
			ArchiveStream.Write(Header.Serialize());
			ArchiveStream.Write(SerializedDirectoryHeader);

			FileDataStream.Seek(0, SeekOrigin.Begin);
			FileDataStream.CopyTo(ArchiveStream);
			FileDataStream.Dispose();

			ArchiveStream.Write(FileDictionaryData);
			ArchiveStream.Flush();
		}

		private BBADataEntry GetDataEntryByName(string FileName) => GlobalFileData.FirstOrDefault(Name => Name.FileName == FileName);
		public byte[] GetFileDataByDataEntryName(string FileName)
		{
			BBADataEntry DataEntry = GetDataEntryByName(FileName);
			if (DataEntry == default)
			{
				return null;
			}

			return ReadDataEntryFromArchive(DataEntry);
		}
	}
}
