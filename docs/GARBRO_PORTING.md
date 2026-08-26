# GARbro format-porting strategy

GARbro is an MIT-licensed visual-novel resource browser with a large catalog
of legacy and game-specific handlers. Its breadth comes from many years of
individual format contributions rather than a single universal extractor.

This project targets .NET 8, a simplified Chinese-first workflow, safe output
paths, unified progress reporting, and visual-resource filtering. Importing
GARbro wholesale would also import its older .NET Framework architecture and
create two competing UI and archive abstractions. Instead, compatible handlers
should be ported selectively.

## Rules for a port

1. Confirm that the source file is covered by GARbro's MIT license.
2. Record the original GARbro file, author information when present, and commit.
3. Reimplement it behind this project's archive and extraction conventions.
4. Preserve GARbro's copyright and MIT notice in the source file and notices.
5. Add synthetic format tests; never commit commercial game files.
6. Keep path traversal protection, cancellation, and per-file failure isolation.
7. Prefer formats demonstrated by user reports over speculative breadth.

## Suggested priority

1. Common archive formats repeatedly found in the user's library.
2. Common visual-novel image containers that can be converted losslessly.
3. Additional KiriKiri variants and filters with reproducible samples.
4. Legacy engine formats with stable, well-documented handlers.

Game-specific encryption keys, copied commercial assets, and handlers without a
clear open-source provenance must not be committed.
