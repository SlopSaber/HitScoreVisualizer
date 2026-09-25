# HSV text shader bundle

Build this project with Unity 6000.3.19f1 for Windows. `BuildHsvTextOverlay.Build` writes `Output/hsv-text-overlay.bundle`. Copy that file to `HitScoreVisualizer/Shaders/hsv-text-overlay.bundle`, then build the C# project. The C# build embeds the bundle in `HitScoreVisualizer.dll`.

The shader is based on the Text Mesh Pro mobile overlay shader already present in the SiraLocalizer Unity source. It keeps the game's zero-alpha blend for bloom behavior and uses `ZTest Always` so scenery depth cannot hide score text. Its stencil properties keep note masking active.
