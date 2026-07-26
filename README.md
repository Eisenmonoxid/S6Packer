# S6Packer
A simple command line application that unpacks and packs archive files of the game "The Settlers 6 - Rise of an Empire" and its History Edition.  

**This is a full, from the ground up rewrite of [the bba6tool](https://github.com/SettlersDev/bba6tool) originally created by yoq.**

## Usage
Unpack: Launch the executable with the path to the .bba|.s6map|.s6xmap file as argument.   
Pack: Launch the executable with the path to the folder and `--Type: ` as arguments.   
(e.g. `S6Packer.exe "C:\Usermap_Extracted" "--Type: .s6xmap"`)   
`"--Type: .s6xmap"` needs to be enclosed in quotation marks, otherwise the application cannot parse the arguments correctly.

For example:
```
C:\Settlers\S6Packer.exe "C:\Settlers\Maps.bba" -- Unpacks the content of the archive file Maps.bba into a folder called Maps_Extracted
C:\Settlers\S6Packer.exe "C:\Settlers\Usermap_Extracted" "--Type: .s6xmap" -- Packs the contents of Usermap_Extracted into Usermap.s6xmap
```

## Features
- Extracts all data from Settlers 6 .bba|.s6map|.s6xmap|.s6savegame|.s6xsavegame archive files from both the Original Release and the History Editions of the game.
- Packs files into a .bba|.s6map|.s6xmap archive file.
- Works on both Windows and Linux, fast file encryption/decryption by utilizing Unsafe and Spans.
- Fixes some errors (like the DateTime of the files being set to zero) of the original tool.
- Adds some interface functions like linking HashTable entries to their File Dictionary offsets or extracting only specified files from an archive.

**Should there be any questions: [Settlers Discord Server](https://discord.gg/7SGkQtAAET).**
