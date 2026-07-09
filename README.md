# S6Packer
A simple command line application that unpacks and packs archive files (.bba|.s6map|.s6xmap) of the game "The Settlers 6 - Rise of an Empire" and its History Edition.  

**This is a full, from the ground up rewrite of [the bba6tool](https://github.com/SettlersDev/bba6tool) originally created by yoq.**

## Usage
Unpack: Launch the executable with the path to the .bba|.s6map|.s6xmap file as argument.   
Pack: Launch the executable with the path to the folder (ends with `_Extracted`) and `--Type: ` as arguments.   
(e.g. `S6Packer.exe C:\\Usermap_Extracted --Type: .s6xmap`)

For example:
```
"C:\Settlers\S6Packer.exe C:\Settlers\Maps.bba" -- Unpacks the content of the archive file Maps.bba into a folder called Maps_Extracted
"C:\Settlers\S6Packer.exe C:\Settlers\Usermap_Extracted --Type: .s6xmap" -- Packs the contents of Usermap_Extracted into Usermap.s6xmap
```

## Features
- Extracts all data from Settlers 6 .bba|.s6map|.s6xmap archive files from both the original release and the History Editions of the game.
- Can repack those files into a .bba|.s6map|.s6xmap archive file.
- Works on both Windows and Linux, fast file encryption/decryption by utilizing Unsafe and Spans.
- Fixes some errors (like the DateTime of the files being set to zero) of the original tool.
- Adds some interface functions like linking HashTable entries to their File Dictionary offsets or extracting only specified files from an archive.

**Should there be any questions: [Settlers Discord Server](https://discord.gg/7SGkQtAAET).**
