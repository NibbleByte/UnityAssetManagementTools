// Copyright (c) Snapshot Games 2014, All Rights Reserved, http://www.snapshotgames.com

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DevLocker.Tools.AssetManagement
{
	public class FindUsedByInScene : EditorWindow
	{
		[Serializable]
		private struct Result
		{
			public Object TargetObj;
			public int GameObjectInstanceID;
			public string TargetObjTypeName;
			public string PropertyName;

			public Result(Object obj, int instanceId, string propertyName)
			{
				TargetObj = obj;
				GameObjectInstanceID = instanceId;
				TargetObjTypeName = obj.GetType().Name;
				PropertyName = propertyName;
			}
		}

		[Serializable]
		private struct HierarchySummaryResult
		{
			public int GameObjectInstanceID;
			public string Summary;
		}

		[Flags]
		private enum SelectionTrackingType
		{
			SceneObjects = 1 << 0,
			PrefabAssets = 1 << 1,
			Materials = 1 << 2,
			Textures = 1 << 3,

			ScriptableObjects = 1 << 6,

			Others = 1 << 7,

		}

		private Object m_SelectedObject => 0 <= m_SelectionHistoryIndex && m_SelectionHistoryIndex < m_SelectionHistory.Count
			? m_SelectionHistory[m_SelectionHistoryIndex]
			: m_SelectionHistory.LastOrDefault()
			;

		private List<Object> m_SelectionHistory = new List<Object>();
		private int m_SelectionHistoryIndex = 0;

		private SelectionTrackingType m_SelectionTracking = SelectionTrackingType.SceneObjects;
		private bool m_LockSelection = false;

		private List<Result> m_References = new List<Result>();
		private List<HierarchySummaryResult> m_HierarchyReferences = new List<HierarchySummaryResult>();

		private Vector2 m_ScrollView = Vector2.zero;

		private GUIContent RefreshButtonContent;
		private GUIContent TrackSelectionButtonContent;
		private GUIContent LockToggleOnContent;
		private GUIContent LockToggleOffContent;
		private GUIStyle HierarchyUsedByStyle;

		[MenuItem("GameObject/-= Find Used By In Scene =-", false, 0)]
		public static void OpenWindow()
		{
			var window = GetWindow<FindUsedByInScene>("Used By ...");
			window.minSize = new Vector2(150f, 200f);

			window.TrySelect(Selection.activeGameObject);
		}

		void OnEnable()
		{
			m_SelectionTracking = (SelectionTrackingType)EditorPrefs.GetInt("DevLocker.UsedBy.SelectionTracking", (int) SelectionTrackingType.SceneObjects);

			Selection.selectionChanged += OnSelectionChange;
			EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;

			EditorApplication.RepaintHierarchyWindow();
		}

		void OnDisable()
		{
			EditorPrefs.SetInt("DevLocker.UsedBy.SelectionTracking", (int)m_SelectionTracking);

			Selection.selectionChanged -= OnSelectionChange;
			EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyWindowItemOnGUI;

			EditorApplication.RepaintHierarchyWindow();
		}

		private void OnSelectionChange()
		{
			if (m_LockSelection || Selection.activeObject == null)
				return;

			if (!m_SelectionTracking.HasFlag(SelectionTrackingType.SceneObjects) && Selection.activeObject is GameObject && !AssetDatabase.Contains(Selection.activeObject))
				return;

			if (!m_SelectionTracking.HasFlag(SelectionTrackingType.PrefabAssets) && Selection.activeObject is GameObject && AssetDatabase.Contains(Selection.activeObject))
				return;

			if (!m_SelectionTracking.HasFlag(SelectionTrackingType.Materials) && Selection.activeObject is Material)
				return;

			if (!m_SelectionTracking.HasFlag(SelectionTrackingType.Textures) && (Selection.activeObject is Texture || Selection.activeObject is Sprite))
				return;

			if (!m_SelectionTracking.HasFlag(SelectionTrackingType.ScriptableObjects) && Selection.activeObject is ScriptableObject)
				return;


			if (!m_SelectionTracking.HasFlag(SelectionTrackingType.Others)
				&& !(Selection.activeObject is GameObject)
				&& !(Selection.activeObject is Material)
				&& !(Selection.activeObject is Texture)
				&& !(Selection.activeObject is Sprite)
				&& !(Selection.activeObject is ScriptableObject)
				)
				return;

			if (Selection.activeObject && TrySelect(Selection.activeObject)) {
				Repaint();
			}
		}

		private void OnHierarchyWindowItemOnGUI(int instanceID, Rect selectionRect)
		{
			foreach(HierarchySummaryResult result in m_HierarchyReferences) {
				if (result.GameObjectInstanceID == instanceID) {

					Color prevColor = GUI.color;
					GUI.color = new Color(0.9f, 0.6f, 0.3f, 0.3f);

					GUI.Box(selectionRect, new GUIContent("", result.Summary), HierarchyUsedByStyle);

					GUI.color = prevColor;

					break;
				}
			}
		}

		private bool TrySelect(Object obj)
		{
			if (obj && m_SelectedObject != obj) {
				m_SelectionHistoryIndex = m_SelectionHistory.Count;
				m_SelectionHistory.Add(obj);
				PerformSearch();

				EditorApplication.RepaintHierarchyWindow();

				return true;
			}

			return false;
		}

		private void PerformSearch()
		{
			m_References.Clear();
			m_HierarchyReferences.Clear();

			if (m_SelectedObject == null)
				return;

			GameObject selectedGO = m_SelectedObject as GameObject;
			if (selectedGO == null) {
				if (m_SelectedObject is Component) {
					selectedGO = ((Component) m_SelectedObject).gameObject;
				} else {
					return;
				}
			}

#if UNITY_2019_4_OR_NEWER
			UnityEditor.SceneManagement.PrefabStage prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(selectedGO);
#else
			UnityEditor.Experimental.SceneManagement.PrefabStage prefabStage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetPrefabStage(selectedGO);
#endif

			if (prefabStage == null) {
				for (int i = 0; i < SceneManager.sceneCount; ++i) {
					foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects()) {
						SearchSelected(go);
					}
				}

			} else {
				SearchSelected(prefabStage.prefabContentsRoot);
			}


			// If found targets are child game objects, ping them so they show up in the Hierarchy (to be painted).
			if (m_References.Count > 0) {
				Result childResult = m_References.FirstOrDefault(r =>
					r.TargetObj is GameObject rgo && rgo.transform.IsChildOf(selectedGO.transform) && rgo != selectedGO
					|| r.TargetObj is Component rc && rc.transform.IsChildOf(selectedGO.transform) && rc.gameObject != selectedGO
				);

				if (childResult.TargetObj) {
					EditorGUIUtility.PingObject(childResult.TargetObj);
				}
			}
		}

		private void SearchSelected(GameObject go)
		{
			var hierarchyResults = new Dictionary<int, HierarchySummaryResult>();
			HierarchySummaryResult hierarchyResult;

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

						if (sp.objectReferenceValue == m_SelectedObject) {
							int instanceId = component.gameObject.GetInstanceID();
							m_References.Add(new Result(component, instanceId, sp.propertyPath));

							hierarchyResults.TryGetValue(instanceId, out hierarchyResult);
							hierarchyResult.GameObjectInstanceID = instanceId;
							hierarchyResult.Summary += $"{component.GetType().Name}.{sp.propertyPath}\n";
							hierarchyResults[instanceId] = hierarchyResult;
							break;
						}

						if (m_SelectedObject is GameObject && sp.objectReferenceValue is Component targetComponent) {
							if (targetComponent && targetComponent.gameObject == m_SelectedObject) {
								int instanceId = component.gameObject.GetInstanceID();
								m_References.Add(new Result(component, instanceId, sp.propertyPath));

								hierarchyResults.TryGetValue(instanceId, out hierarchyResult);
								hierarchyResult.GameObjectInstanceID = instanceId;
								hierarchyResult.Summary += $"{component.GetType().Name}.{sp.propertyPath}\n";
								hierarchyResults[instanceId] = hierarchyResult;
								break;
							}
						}
					}
				}
			}

			m_HierarchyReferences.AddRange(hierarchyResults.Values.Select(r => {
				r.Summary = $"Used by {r.Summary.Count(c => c == '\n')} components:\n" + r.Summary.Trim();
				return r;
			}));
		}

		private void SelectPrevious()
		{
			m_SelectionHistoryIndex--;
			while (m_SelectedObject == null && m_SelectionHistory.Count > 0) {
				m_SelectionHistory.RemoveAt(m_SelectionHistoryIndex);

				if (m_SelectionHistoryIndex > 0) {
					m_SelectionHistoryIndex--;
				}
			}

			PerformSearch();
			Repaint();
		}

		private void SelectNext()
		{
			m_SelectionHistoryIndex++;
			while (m_SelectedObject == null && m_SelectionHistory.Count > 0) {
				m_SelectionHistory.RemoveAt(m_SelectionHistoryIndex);

				if (m_SelectionHistoryIndex == m_SelectionHistory.Count) {
					m_SelectionHistoryIndex--;
				}
			}

			PerformSearch();
			Repaint();
		}

		private void OnGUI()
		{
			if (RefreshButtonContent == null) {
				RefreshButtonContent = new GUIContent(EditorGUIUtility.FindTexture("Refresh"), "Refresh references...");
				TrackSelectionButtonContent = new GUIContent(EditorGUIUtility.FindTexture("animationvisibilitytoggleon"), "Track selection for...");
				LockToggleOffContent = new GUIContent(EditorGUIUtility.IconContent("IN LockButton").image, "Don't track Unity selection (manually drag in object)");
				LockToggleOnContent = new GUIContent(EditorGUIUtility.IconContent("IN LockButton on").image, LockToggleOffContent.tooltip);

				HierarchyUsedByStyle = new GUIStyle();
				HierarchyUsedByStyle.normal.background = Texture2D.whiteTexture;
			}

			GUILayout.Label($"Selection:", EditorStyles.boldLabel);
			Object selectedObj;

			using (new EditorGUILayout.HorizontalScope()) {

				EditorGUI.BeginDisabledGroup(m_SelectionHistoryIndex <= 0);
				if (GUILayout.Button("<", GUILayout.MaxWidth(24f))) {
					SelectPrevious();
				}
				EditorGUI.EndDisabledGroup();

				EditorGUI.BeginDisabledGroup(m_SelectionHistoryIndex >= m_SelectionHistory.Count - 1);
				if (GUILayout.Button(">", GUILayout.MaxWidth(24f))) {
					SelectNext();
				}
				EditorGUI.EndDisabledGroup();

				selectedObj = EditorGUILayout.ObjectField(m_SelectedObject, typeof(Object), true);

				const float rightButtonsWidth = 28f;

				if (GUILayout.Button(RefreshButtonContent, GUILayout.Width(rightButtonsWidth))) {
					PerformSearch();
				}

				if (GUILayout.Button(m_LockSelection ? LockToggleOnContent : LockToggleOffContent, GUILayout.Width(rightButtonsWidth))) {
					m_LockSelection = !m_LockSelection;
				}


				Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f), GUILayout.Width(rightButtonsWidth));

				EditorGUI.BeginDisabledGroup(m_LockSelection);
				if (!m_LockSelection) {
					m_SelectionTracking = (SelectionTrackingType)EditorGUI.EnumFlagsField(rect, new GUIContent("", "Track selection for..."), m_SelectionTracking, GUI.skin.button);
				}
				GUI.Box(rect, TrackSelectionButtonContent, GUI.skin.button);

				EditorGUI.EndDisabledGroup();
			}


			if (TrySelect(selectedObj)) {
				Selection.activeObject = selectedObj;
			}

			GUILayout.Label($"References to the selection ({m_References.Count}):", EditorStyles.boldLabel);

			m_ScrollView = GUILayout.BeginScrollView(m_ScrollView);

			foreach (var result in m_References) {

				EditorGUILayout.BeginHorizontal();

				var type = result.TargetObj ? result.TargetObj.GetType() : typeof(Object);
				EditorGUILayout.ObjectField(result.TargetObj, type, true);
				EditorGUILayout.TextField(result.PropertyName, GUILayout.Width(100f));

				EditorGUILayout.EndHorizontal();
			}

			GUILayout.EndScrollView();
		}
	}
}