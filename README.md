# UnityEditorTools

Unity Editor packages. In Unity Package Manager, choose **Add package from git URL...**.
The URLs below track the latest commit on `main`:

| Package | URL |
| --- | --- |
| zEditor Tools | `https://github.com/Hubr1zz/UnityEditorTools.git?path=/zEditor(vSeries)#main` |
| Scene Tools | `https://github.com/Hubr1zz/UnityEditorTools.git?path=/SceneTools#main` |

To update an existing Git dependency, select the package in Package Manager and click **Update**. Unity locks the resolved commit in the consuming project's `Packages/packages-lock.json`, so using `#main` does not silently update the package every time the project opens.
