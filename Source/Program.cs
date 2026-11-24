using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace S6Packer.Source
{
    #if S6PACKER_STANDALONE_COMPILATION
	internal class Program
	{
        static void Main(string[] args)
		{
			Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Title = "S6Packer";
            Console.Clear();

			string Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            Console.WriteLine("[INFO] S6Packer v" + Version + " - github.com/Eisenmonoxid/S6Packer");
            Console.WriteLine("[INFO] Currently running on " + RuntimeInformation.OSDescription.ToString() + " with runtime " + RuntimeInformation.FrameworkDescription);

            bool Result = false;
            string ArchiveFilePath = Utility.GetArchiveFileFromArgs(args);
            if (ArchiveFilePath != default)
            {
                Result = Unpack(ArchiveFilePath);
            }
            else
            {
                CompressionLevel CompressionLevel = CompressionLevel.SmallestSize;
                string FolderPath = args.FirstOrDefault(Element => Directory.Exists(Element));
                string FileExtension = args.FirstOrDefault(Element => Element.StartsWith("--Type:"));
                string Level = args.FirstOrDefault(Element => Element.StartsWith("--CompressionLevel:"));
                if (Level != default)
                {
                    try
                    {
                        CompressionLevel = (CompressionLevel)Convert.ToUInt32(Level.Replace("--CompressionLevel:", ""));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] Could not parse CompressionLevel! Using: " + CompressionLevel.ToString() + ". Message: " + ex.ToString());
                    }
                }

                if (FolderPath != default && FileExtension != default)
                {
                    Result = Pack(FolderPath, FileExtension, CompressionLevel);
                }
                else
                {
                    Console.WriteLine("[ERROR] Neither archive file nor folder path in arguments! Aborting ...");
                }
            }

            Console.WriteLine("\n[INFO] Finished!" + (!Result ? " One or more errors occured." : " No errors occured."));
            Console.WriteLine("[INFO] Exiting ...");
            return;
		}

        static bool Pack(string FolderPath, string FileExtension, CompressionLevel Level = CompressionLevel.SmallestSize)
        {
            Stopwatch Watch = new();
            DirectoryInfo Info = new(FolderPath);

            FileExtension = FileExtension.Replace("--Type:", "");
            if (FileExtension != ".bba" && FileExtension != ".s6map" && FileExtension != ".s6xmap")
            {
                Console.WriteLine("[ERROR] File Extension for packing must either be .bba, .s6map or .s6xmap. Given Extension was: " + FileExtension);
                return false;
            }

			string OutputFilePath = Path.Combine(Info.Parent.FullName, Info.Name.Replace("_Extracted", "") + FileExtension);
            FileStream Stream;
            try
            {
                Stream = File.Create(OutputFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }

            Console.WriteLine("[INFO] Packing folder content " + FolderPath + " to archive file " + Stream.Name);

            bool Result = true;
            Watch.Start();
            using (Stream)
            {
                try
                {
                    new BBAArchiveFile(Stream, FolderPath, FileExtension, Level);
                    Stream.SetLength(Stream.Position);
                }
                catch (Exception ex)
                {
                    Result = false;
                    Console.WriteLine("[ERROR] " + ex.ToString());
                }
            }
            Watch.Stop();

            Console.WriteLine("[INFO] Operation took " + Watch.ElapsedMilliseconds + " ms.");
            return Result;
        }

        static bool Unpack(string FilePath)
        {
            Stopwatch Watch = new();
            BBAArchiveFile Archive;

            FileStream Stream = GetFileStream(FilePath);
            if (Stream == null)
            {
                return false;
            }

            try
            {
                Archive = new BBAArchiveFile(Stream, true);
            }
            catch (Exception ex)
            {
                Stream.Dispose();
                Console.WriteLine(ex.Message);
                return false;
            }

            Console.WriteLine("[INFO] Unpacking archive file " + Stream.Name);

            FileInfo Info = new(FilePath);
			string ArchiveOutputDirectoryPath = Path.Combine(Info.DirectoryName, Path.GetFileNameWithoutExtension(Info.Name) + "_Extracted");

            Watch.Start();
            Archive.UnpackAllDataEntriesFromArchive(ArchiveOutputDirectoryPath);
            Watch.Stop();

            Stream.Dispose();
            Console.WriteLine("[INFO] Operation took " + Watch.ElapsedMilliseconds + " ms.");
			return true;
        }

		static FileStream GetFileStream(string Filepath)
        {
            FileStream Stream;
            if (Filepath == default)
            {
                Console.WriteLine("[ERROR] No argument(s) given! Aborting ...");
                return null;
            }

            if (!File.Exists(Filepath))
            {
                Console.WriteLine("[ERROR] File does not exist! Aborting ...");
                return null;
            }

            try
            {
                Stream = new FileStream(Filepath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("[ERROR] Could not open FileStream! Aborting ...");

                return null;
            }

			return Stream;
        }
	}
    #endif
}
