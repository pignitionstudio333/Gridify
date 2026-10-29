# Contributing

Thanks for your interest! Bug reports, translations and pull requests are welcome.

## Build
Requirements: Windows 10/11 x64, .NET 10 SDK.

    dotnet build src/Gridify.csproj -c Release

Or run `scripts\build.bat` (publishes to `publish\` and can start the app). `scripts\release.bat <version>` builds the portable zip, the installer (needs Inno Setup 6) and checksums into `dist\`.

## Ground rules
- Reference mode: never move, copy or delete a user's original files (except an explicit delete to the Recycle Bin).
- No system pollution: the only registry write allowed is the optional HKCU Run value.
- The hidden desktop icon layer must always be restored on exit, crash and uninstall.
- All user-visible text goes through `Core\Loc.cs` (English and Traditional Chinese). Add the key to both dictionaries.
- Keep changes focused; describe how you tested.

## Reporting bugs
Please include your Windows version, whether you use the portable or installed build, and `crash.log` (in the data folder) if there is one.
