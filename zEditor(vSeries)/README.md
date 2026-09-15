# zEditor Tools

zEditor Tools is a collection of Unity Editor productivity tools.

## Requirements

- Unity 2022.3 or newer

## Install from Git

In Unity, open **Window > Package Manager**, choose **Add package from git URL...**, and use:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/zEditor(vSeries)#main
```

The `path` query points Unity to the package subfolder in this repository. `main` tracks the latest package commit. For a reproducible installation, replace `#main` with a version tag.

`SceneTools` is a separate package in the same repository. To install it independently, use:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/SceneTools#main
```

## Update from Git

Push a new commit to `main`, then update the package from Package Manager. For a pinned release, create a version tag and reinstall the package using that tag, for example:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/zEditor(vSeries)#main
```

Unity records the resolved Git commit in the consuming project's lock file. Reusing the same Git URL does not automatically pull every new commit on project restart; use Package Manager's Git URL installation/update flow or change the revision/tag when you want to update.

## Local development

Use **Add package from disk...** in Package Manager and select this folder's `package.json`.

Changes made to this local package folder are immediately available to the project after Unity recompiles. Changes made to a cached Git or registry package should instead be developed in a local package or copied into the project as project-owned code.
