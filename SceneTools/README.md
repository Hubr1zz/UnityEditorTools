# Scene Tools

Optional Unity Editor tools for Scene View and hierarchy workflows.

## Time Scale

Open **Window > Time Scale Overlay**, then drag the overlay handle to a Scene View edge to dock it, just like Root Rotate. The overlay also appears in the Scene View Overlays menu.

In Play Mode, enter a multiplier from 0 to 10 (press Enter to apply), choose a preset from **Speed**, or click **1×** to restore normal speed. Zero pauses scaled game time. The field follows the actual `Time.timeScale`, including changes made by game scripts. The controls are disabled outside Play Mode, and leaving Play Mode restores the scale captured before the first adjustment. This editor tool only changes `Time.timeScale`; the project's physics timestep is preserved.

## Requirements

- Unity 2022.3 or newer

## Install from Git

In Unity, open **Window > Package Manager**, choose **Add package from git URL...**, and use:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/SceneTools#main
```

This package is maintained in the same repository as zEditor Tools, but it is installed independently so users can choose whether to include it.
