# zEditor Tools

zEditor Tools is a collection of Unity Editor productivity tools.

## Requirements

- Unity 2022.3 or newer

## Install from Git

In Unity, open **Window > Package Manager**, choose **Add package from git URL...**, and use:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/zEditor(vSeries)#v0.1.1
```

The `path` query points Unity to the package subfolder in this repository. The tag is optional, but using a tag keeps the installed version reproducible.

`SceneTools` is a separate package in the same repository. To install it independently, use:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/SceneTools#v0.1.1
```

## Update from Git

Push a new commit and create a new tag, then reinstall the package using the new tag, for example:

```text
https://github.com/Hubr1zz/UnityEditorTools.git?path=/zEditor(vSeries)#v0.1.1
```

Unity records the resolved Git commit in the consuming project's lock file. Reusing the same Git URL does not automatically pull every new commit on project restart; use Package Manager's Git URL installation/update flow or change the revision/tag when you want to update.

## Local development

Use **Add package from disk...** in Package Manager and select this folder's `package.json`.

Changes made to this local package folder are immediately available to the project after Unity recompiles. Changes made to a cached Git or registry package should instead be developed in a local package or copied into the project as project-owned code.
