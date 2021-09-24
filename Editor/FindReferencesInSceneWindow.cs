// Copyright (c) Snapshot Games 2014, All Rights Reserved, http://www.snapshotgames.com

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DevLocker.Tools.AssetManagement
{
	public class FindReferencesInSceneWindow : EditorWindow
	{
		[Serializable]
		private struct Result
		{
			public Object TargetObj;
			public string PropertyName;

			public Result(Object obj, string propertyName)
			{
				TargetObj = obj;
				PropertyName = propertyName;
			}
		}

		private Object _selectedObject;

		private List<Result> _references = new List<Result>();

		private Vector2 _scrollView = Vector2.zero;

		[MenuItem("GameObject/Find References In Scene", false, 0)]
		public static void OpenWindow()
		{
			var window = GetWindow<FindReferencesInSceneWindow>("Scene References");
			window._selectedObject = Selection.activeGameObject;
			window.PerformSearch();
		}


		private void PerformSearch()
		{
			_references.Clear();

			if (_selectedObject == null)
				return;

			GameObject selectedGO = _selectedObject as GameObject;
			if (selectedGO == null) {
				if (_selectedObject is Component) {
					selectedGO = ((Component) _selectedObject).gameObject;
				} else {
					return;
				}
			}

			PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(selectedGO);

			if (prefabStage == null) {
				for (int i = 0; i < SceneManager.sceneCount; ++i) {
					foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects()) {
						SearchSelected(go);
					}
				}

			} else {
				SearchSelected(prefabStage.prefabContentsRoot);
			}
		}

		private void SearchSelected(GameObject go)
		{
			var components = go.GetComponentsInChildren<Component>(true);
			foreach (var component in components) {

				// Could be missing component.
				if (component == null)
					continue;

				SerializedObject so = new SerializedObject(component);
				var sp = so.GetIterator();

				// Iterate over the components' properties.
				while (sp.NextVisible(true)) {
					if (sp.propertyType == SerializedPropertyType.ObjectReference) {
						if (sp.objectReferenceValue == null)
							continue;

						if (sp.objectReferenceValue == _selectedObject) {
							_references.Add(new Result(component, sp.displayName));
							break;
						}

						if (_selectedObject is GameObject && sp.objectReferenceValue is Component targetComponent) {
							if (targetComponent && targetComponent.gameObject == _selectedObject) {
								_references.Add(new Result(component, sp.displayName));
								break;
							}
						}
					}
				}
			}
		}

		private void OnGUI()
		{
			var obj = EditorGUILayout.ObjectField(_selectedObject, typeof(Object), true);
			if (_selectedObject != obj) {
				_selectedObject = obj;
				PerformSearch();
			}

			GUILayout.Label($"References to the selection ({_references.Count}):", EditorStyles.boldLabel);

			_scrollView = GUILayout.BeginScrollView(_scrollView);

			foreach (var result in _references) {

				EditorGUILayout.BeginHorizontal();

				var type = result.TargetObj ? result.TargetObj.GetType() : typeof(Object);
				EditorGUILayout.ObjectField(result.TargetObj, type, true);
				GUILayout.Label(result.PropertyName, GUILayout.Width(100f));

				EditorGUILayout.EndHorizontal();
			}

			GUILayout.EndScrollView();
		}
	}
}