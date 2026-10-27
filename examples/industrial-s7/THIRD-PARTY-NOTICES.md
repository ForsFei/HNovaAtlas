# Third-party components

S7netplus 0.20.0 is restored from NuGet and corresponds to source commit
`f1ae0ea084e712b59e414de6aaee7d196244a239`:
https://github.com/S7NetPlus/s7netplus/tree/f1ae0ea084e712b59e414de6aaee7d196244a239
See the upstream S7.NET+ license and preserve its notices when distributing.

The optional Windows x64 `snap7.dll` test fixture comes from the same upstream
test tree, not from a Siemens installation. Snap7 is a separate component by
Davide Nardella and is licensed under LGPL version 3 or later. Its licensing
is not replaced by the S7.NET+ license. Upstream project/source information:
https://snap7.sourceforge.net/ and https://github.com/SCADACS/snap7

The DLL is downloaded only into ignored `.local/`, verified with SHA256
`17336585C8F5EBE55D6DB85152BFD64D5456F6C30B26AC862980AF85F725072D`,
and copied into ignored build output. This repository does not commit it.
`prepare-native.ps1` also downloads GNU GPLv3 and LGPLv3 license text to accompany
the local copy. Consult the relevant upstream source and licensing obligations
before redistributing any binaries or modified versions.

The native entry-point declarations in this example are minimal API bindings;
the complete upstream C# wrapper is not copied into this project.
