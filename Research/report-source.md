# HSV note occlusion investigation

Audience: HitScoreVisualizer maintainer. Date: 2026-09-24. Scope: Beat Saber 1.45.1, ordinary colored notes with Beat Saber Plus Note Tweaker, and HSV score text. Runtime visual proof of the revised build remains pending.

## Answer

The previous patch changed opaque note materials from queue 2004 to queue 5000 and put HSV text at queue 4999. The user confirmed that HSV still appeared over regular notes. Moving the note materials between render passes was the wrong control for this case. The replacement keeps their original materials and adds a colorless stencil pass to visible note meshes. HSV text skips pixels marked by that pass.

## Evidence

- `maincore_assets_all_99a08e87d16158a623e17b8fbaf08c8d.bundle` in the selected game instance contains `NoteHD` and `NoteLW` materials at custom render queue 2004. `sharedassets_assets_all_17ff2f2656a30f8c3f2137b43f7de416.bundle` contains their `Custom/NoteHD` and `Custom/NoteLW` shaders. The HD shader is tagged `RenderType=Opaque`, `RenderPipeline=UniversalPipeline`, and has a `UniversalForwardOnly` pass. This is direct inspection of the installed 1.45.1 assets.
- The same asset bundle gives the `NoteCube` mesh one submesh. [Unity's Mesh Renderer manual](https://docs.unity3d.com/ja/2021.2/Manual/class-MeshRenderer.html) says an additional material renders the last submesh again, allowing a second pass without replacing the note's material.
- [Unity's URP Render Objects documentation](https://docs.unity3d.com/kr/Packages/com.unity.render-pipelines.universal%4015.0/manual/renderer-features/renderer-feature-render-objects.html) confirms opaque and transparent objects use separate render passes. The assertion that the queue change caused this particular failure is an inference; no frame capture identified the rejected draw call.
- [Unity's stencil documentation](https://docs.unity3d.com/cn/2018.3/Manual/SL-Stencil.html) defines the read and write masks and `Replace` and `NotEqual` operations used by the new mask. [Unity's TextMesh Pro overlay shader source](https://github.com/Unity-Technologies/FPSSample/blob/master/Assets/TextMesh%20Pro/Resources/Shaders/TMP_SDF%20Overlay.shader) shows the text shader supports stencil comparison.
- `PublicMods/BeatSaberPlus/Modules/BeatSaberPlus_NoteTweaker/Patches/PGameNoteController.cs` scales note transforms; `PColorNoteVisuals.cs` changes material property blocks. They do not replace the note body shader or its render queue in the inspected paths.

## Implementation and limit

At note spawn, HSV appends one shared `UI/Default` material to each opaque note renderer. It writes stencil bit 128, writes no color, and keeps note materials intact. HSV text draws at queue 4999 only where that bit is absent. The mask material is removed at scene disposal. A one-time gameplay log reports the number of note renderers receiving the mask. The game was running during build, so the new DLL is staged until restart; visibility and stencil behavior have not yet been observed in game.
