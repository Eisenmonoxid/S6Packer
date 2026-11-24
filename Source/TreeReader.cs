using System.Collections.Generic;
using System.IO;

namespace S6Packer.Source
{
    public static class TreeReader
    {
        public static DirEntry ReadRootFolder(string Root)
        {
            var RootEntry = new DirEntry
            {
                Path = ".",
                IsDirectory = true,
                FirstChild = ReadFolder(Root, Root)
            };
            
            return RootEntry;
        }

        #nullable enable
        private static DirEntry? ReadFolder(string Root, string Current)
        {
            DirEntry? FirstEntry = null;
            DirEntry? PreviousEntry = null;
            List<string> Entries = [.. Directory.EnumerateFileSystemEntries(Current)];

            foreach (string Entry in Entries)
            {
                string Name = Path.GetFileName(Entry);
                if (Name is "." or "..")
                {
                    continue;
                }

                var Node = new DirEntry
                {
                    IsDirectory = Directory.Exists(Entry),
                    Path = Path.GetRelativePath(Root, Entry)
                };

                FirstEntry ??= Node;
                PreviousEntry?.NextSibling = Node;
                PreviousEntry = Node;

                if (Node.IsDirectory)
                {
                    Node.FirstChild = ReadFolder(Root, Entry);
                }
            }

            return FirstEntry;
        }
        #nullable disable

        public static List<DirEntry> BuildLinearList(DirEntry Root)
        {
            var List = new List<DirEntry>();
            void Traverse(DirEntry Node)
            {
                List.Add(Node);
                for (var Child = Node.FirstChild; Child != null; Child = Child.NextSibling)
                {
                    Traverse(Child);
                }
            }

            Traverse(Root);
            return List;
        }
    }

    #nullable enable
    public class DirEntry
    {
        public string Path = string.Empty;
        public bool IsDirectory;
        public DirEntry? FirstChild;
        public DirEntry? NextSibling;
        public int DirOffset;
    }
    #nullable disable
}
