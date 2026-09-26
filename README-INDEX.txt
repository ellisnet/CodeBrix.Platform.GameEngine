================================================================================
README-INDEX: CodeBrix.Platform.GameEngine
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below and
read its AGENT-README file in full. Read MAINTAINER-README.txt only if you are
changing this repository itself.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.Platform.GameEngine.MitLicenseForever — the managed,
      cross-platform 2D / 2.5D game engine (engine core + CodeBrix.Platform host
      layer, both assemblies in one package). License: MIT.

  src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt
      CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever — optional SDL2 game
      controller (gamepad) support for that engine. License: MIT AND Zlib.

  src/CodeBrix.Platform.GameEngine.KenneyAssets/AGENT-README.txt
      CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever — optional
      Kenney asset bundle support for that engine: it catalogs what a bundle
      holds and materializes images, sprite atlases, audio, fonts, SVG, Tiled
      maps and glTF models into engine objects. License: MIT.

  src/CodeBrix.Platform.GameEngine.GeneratedMusic/AGENT-README.txt
      CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever — optional
      generated in-game music for that engine: CodeBrix.Audio.MusicGeneration's
      endless, model-generated music played on the engine's music bus through
      one UseGeneratedMusic call. License: MIT.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging, versioning and provenance notes for
      maintainers.
  EXTRAS-README.txt
      Samples, tools and other non-package content in this repository.

NESTED READMEs (samples and tools)
----------------------------------
  samples/KenneyAssetsDemo/README.md
      The Kenney asset sample: what it demonstrates, its controls, and where its
      asset bundles came from.
  samples/Platformer.Brix/README.md
      The side-view platform sample: its level rules and how it is built.
  samples/SpaceDuel.Brix/README.md
      The space-duel sample, including how to drop in licensed ship art.
  tools/padcheck/README.md
      The hand-run gamepad hardware check and its button/axis tables.
  tools/sdl2_library_building/README.md
      The Windows-ARM64 SDL2 build script: prerequisites, usage and switches.
  EXTRAS-README.txt describes every sample and tool; those two tool READMEs are
  the authoritative detail for the tools.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  THIRD-PARTY-NOTICES.txt
      What came from where, and under which licences.
  README-INDEX.txt
      This file.

================================================================================
END OF README-INDEX
================================================================================
