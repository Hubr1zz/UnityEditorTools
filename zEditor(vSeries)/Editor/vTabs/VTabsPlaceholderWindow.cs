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
using System.Diagnostics;
using Type = System.Type;
using Delegate = System.Delegate;
using Action = System.Action;
using static VTabs.Libs.VUtils;
using static VTabs.Libs.VGUI;
// using static VTools.VDebug;



namespace VTabs
{
    public class VTabsPlaceholderWindow : EditorWindow
    {


        void OnGUI()
        {

            // GUILayout.Label(objectGlobalID.ToString());
            // GUILayout.Label(objectGlobalID.guid.ToPath());


            // if (isSceneObject)
            //     GUILayout.Label("scene object");

            // if (isPrefabObject)
            //     GUILayout.Label("prefab object");



            var fontSize = 13;


            var assetName = objectGlobalID.guid.ToPath().GetFilename();

            var assetIcon = AssetDatabase.GetCachedIcon(objectGlobalID.guid.ToPath());


            void label()
            {

                GUI.skin.label.fontSize = fontSize;



                GUILayout.Label("This object is from      " + assetName + ", which isn't loaded");


                var iconRect = lastRect.MoveX("This object is from".GetLabelWidth()).SetWidth(20).SetSizeFromMid(16).MoveX(.5f);

                GUI.DrawTexture(iconRect, assetIcon);



                GUI.skin.label.fontSize = 0;

            }
            void button()
            {
                GUI.skin.button.fontSize = fontSize;


                var buttonText = "Load      " + assetName;

                if (GUILayout.Button(buttonText, GUILayout.Height(30), GUILayout.Width(buttonText.GetLabelWidth(fontSize: fontSize) + 34)))
                    if (isPrefabObject)
                        PrefabStageUtility.OpenPrefab(objectGlobalID.guid.ToPath());
                    else if (isSceneObject)
                        EditorSceneManager.OpenScene(objectGlobalID.guid.ToPath());
                { } // todonow



                var iconRect = lastRect.MoveX("Load".GetLabelWidth()).SetWidth(20).SetSizeFromMid(16).MoveX(23 - 3);

                GUI.DrawTexture(iconRect, assetIcon);




                GUI.skin.button.fontSize = 0;


            }


            GUILayout.Space(15);
            // BeginIndent(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginVertical();


            label();

            Space(10);
            button();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();



            void tryLoadPrefabObject()
            {
                if (!isPrefabObject) return;
                if (StageUtility.GetCurrentStage() is not PrefabStage prefabStage) return;
                if (prefabStage.assetPath != objectGlobalID.guid.ToPath()) return;

                if (objectGlobalID.GetObject() is not Object prefabAssetObject) return;



                if (prefabAssetObject is Component assetComponent)
                    if (prefabStage.prefabContentsRoot.GetComponentsInChildren(assetComponent.GetType())
                                                      .FirstOrDefault(r => GlobalID.GetForPrefabStageObject(r) == objectGlobalID) is Component instanceComoponent)
                        Close_andOpenPropertyEditor(instanceComoponent);



                if (prefabAssetObject is GameObject assetGo)
                    if (prefabStage.prefabContentsRoot.GetComponentsInChildren<Transform>()
                                                      .Select(r => r.gameObject)
                                                      .FirstOrDefault(r => GlobalID.GetForPrefabStageObject(r) == objectGlobalID) is GameObject isntanceGo)
                        Close_andOpenPropertyEditor(isntanceGo);

            }
            void tryLoadSceneObject()
            {
                if (!isSceneObject) return;

                var loadedScenes = Enumerable.Range(0, EditorSceneManager.sceneCount)
                                             .Select(i => EditorSceneManager.GetSceneAt(i))
                                             .Where(r => r.isLoaded);
                if (!loadedScenes.Any(r => r.path == objectGlobalID.guid.ToPath())) return;

                if (objectGlobalID.GetObject() is not Object loadedObject) return;


                Close_andOpenPropertyEditor(loadedObject);


            }


            tryLoadPrefabObject();
            tryLoadSceneObject();



        }




        public void Close_andOpenPropertyEditor(Object o)
        {
            var dockArea = this.GetMemberValue<Object>("m_Parent");
            var tabIndex = dockArea.GetMemberValue<List<EditorWindow>>("m_Panes").IndexOf(this);


            var tabInfo = new VTabs.TabInfo(o);

            tabInfo.originalTabIndex = tabIndex;


            VTabs.guis_byDockArea[dockArea].AddTab(tabInfo, atOriginalTabIndex: true);



            this.Close();

        }










        public void Open_andReplacePropertyEditor(EditorWindow propertyEditorToReplace)
        {

            objectGlobalID = new GlobalID(propertyEditorToReplace.GetMemberValue<string>("m_GlobalObjectId"));


            isSceneObject = AssetDatabase.GetMainAssetTypeAtPath(objectGlobalID.guid.ToPath()) == typeof(SceneAsset);
            isPrefabObject = AssetDatabase.GetMainAssetTypeAtPath(objectGlobalID.guid.ToPath()) == typeof(GameObject);

            if (!isSceneObject && !isPrefabObject) { propertyEditorToReplace.Close(); Object.DestroyImmediate(this); return; }




            var dockArea = propertyEditorToReplace.GetMemberValue("m_Parent");

            var tabIndex = dockArea.GetMemberValue<List<EditorWindow>>("m_Panes")
                                   .IndexOf(propertyEditorToReplace);

            dockArea.InvokeMethod("AddTab", tabIndex, this, true);





            this.titleContent = propertyEditorToReplace.titleContent;


            if (propertyEditorToReplace.hasFocus)
                this.Focus();


            propertyEditorToReplace.Close();


        }

        public GlobalID objectGlobalID;

        public bool isSceneObject;
        public bool isPrefabObject;

        // todonow scene config? active/additive, if active, which otehr additive scenes were loaded? 

    }
}
#endif