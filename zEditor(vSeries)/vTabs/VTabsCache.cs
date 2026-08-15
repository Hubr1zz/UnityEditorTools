#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using System.Reflection;
using System.Linq;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using static VTabs.VTabsAddTabWindow;
using static VTabs.Libs.VUtils;
using static VTabs.Libs.VGUI;
// using static VTools.VDebug;

namespace VTabs
{
    [FilePath("Library/vTabs Cache.asset", FilePathAttribute.Location.ProjectFolder)]
    public class VTabsCache : ScriptableSingleton<VTabsCache>
    {

        public List<TabEntry> allTabEntries = new();


        [System.Serializable]
        public class TabEntry
        {
            public string name = "";
            public string iconName = "";
            public string typeString = "";
        }



        public static void Save() => instance.Save(saveAsText: true);

        public static void Clear() => instance.allTabEntries.Clear();

    }
}
#endif