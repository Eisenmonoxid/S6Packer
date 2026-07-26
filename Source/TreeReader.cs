using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace S6Packer.Source
{
    public static class TreeReader
    {
        public static DirEntry ReadRootFolder(string Root)
        {
            var RootEntry = new DirEntry
            {
                Path = ".",
                IsDirectory = true
            };

            RootEntry.FirstChild = ReadFolder(Root, Root, RootEntry);
            return RootEntry;
        }

        private static DirEntry? ReadFolder(string Root, string Current, DirEntry Parent)
        {
            DirEntry? FirstEntry = null;
            DirEntry? PreviousEntry = null;
            List<string> Entries;

            try
            {
                Entries = [.. Directory.EnumerateFileSystemEntries(Current)];
            }
            catch
            {
                return null;
            }

            foreach (string Entry in Entries)
            {
                if (Path.GetFileName(Entry).StartsWith('.'))
                {
                    continue;
                }

                var Node = new DirEntry
                {
                    Parent = Parent,
                    IsDirectory = Directory.Exists(Entry),
                    Path = Path.GetRelativePath(Root, Entry).ToLowerInvariant()
                };

                FirstEntry ??= Node;
                PreviousEntry?.NextSibling = Node;
                PreviousEntry = Node;

                if (Node.IsDirectory)
                {
                    Node.FirstChild = ReadFolder(Root, Entry, Node);
                }
            }

            return FirstEntry;
        }

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

    public class DirEntry
    {
        public string Path;
        public bool IsDirectory;
        public DirEntry? Parent;
        public DirEntry? FirstChild;
        public DirEntry? NextSibling;
        public int DirOffset;
    }
}
