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
		private GUIContent CopyButtonContent;
		private GUIStyle HierarchyUsedByStyle;
		private GUIStyle UrlStyle;
		private GUIStyle ResultPropertyStyle;

		private System.Diagnostics.Stopwatch m_SearchStopwatch = new System.Diagnostics.Stopwatch();
		private bool SearchIsSlow => m_SearchStopwatch.ElapsedMilliseconds > 0.4f * 1000;

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
			if (RefreshButtonContent == null) {
				InitStyles();
			}

			foreach (HierarchySummaryResult result in m_HierarchyReferences) {
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
					// Can have selected asset instead.
					//return;
				}
			}

#if UNITY_2019_4_OR_NEWER
			UnityEditor.SceneManagement.PrefabStage prefabStage = selectedGO
				? UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(selectedGO)
				: UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()
				;
#else
			UnityEditor.Experimental.SceneManagement.PrefabStage prefabStage = selectedGO
				? UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetPrefabStage(selectedGO)
				: UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()
				;
#endif

			m_SearchStopwatch.Restart();

			try {
				if (prefabStage == null) {
					List<GameObject> allObjects = new List<GameObject>();

					for (int i = 0; i < SceneManager.sceneCount; ++i) {
						allObjects.AddRange(SceneManager.GetSceneAt(i).GetRootGameObjects());
					}

					for(int i = 0; i < allObjects.Count; ++i) {
						SearchSelected(allObjects[i], (float)i / allObjects.Count, 1f / allObjects.Count);
					}

				} else {
					SearchSelected(prefabStage.prefabContentsRoot, 0f, 1f);
				}

			} catch(OperationCanceledException) {
				m_References.Clear();
				m_HierarchyReferences.Clear();
			}
			finally {
				if (SearchIsSlow) {
					EditorUtility.ClearProgressBar();
				}
				m_SearchStopwatch.Stop();
			}


			// If found targets are child game objects, ping them so they show up in the Hierarchy (to be painted).
			// EDIT: This was too annoying...
			//if (selectedGO && m_References.Count > 0) {
			//	Result childResult = m_References.FirstOrDefault(r =>
			//		r.TargetObj is GameObject rgo && rgo.transform.IsChildOf(selectedGO.transform) && rgo != selectedGO
			//		|| r.TargetObj is Component rc && rc.transform.IsChildOf(selectedGO.transform) && rc.gameObject != selectedGO
			//	);
			//
			//	if (childResult.TargetObj) {
			//		EditorGUIUtility.PingObject(childResult.TargetObj);
			//	}
			//}
		}

		private void SearchSelected(GameObject go, float progress, float progressStepRange)
		{
			var hierarchyResults = new Dictionary<int, HierarchySummaryResult>();
			HierarchySummaryResult hierarchyResult;

			if (SearchIsSlow) {
				if (EditorUtility.DisplayCancelableProgressBar("Find Used By In Scene", $"Searching \"{go.name}\"...", progress)) {
					throw new OperationCanceledException();
				}
			}

			var components = go.GetComponentsInChildren<Component>(true);
			for(int i = 0; i < components.Length; ++i) {
				Component component = components[i];

				// Could be missing component.
				if (component == null)
					continue;

				if (component is Transform || component is CanvasRenderer)
					continue;

				if (SearchIsSlow) {
					if (EditorUtility.DisplayCancelableProgressBar("Find Used By In Scene", $"Searching \"{component.name}\"...", progress + ((float)i / components.Length) * progressStepRange)) {
						throw new OperationCanceledException();
					}
				}

				SerializedObject so = new SerializedObject(component);
				var sp = so.GetIterator();

				// For testing out the progress bar.
				//System.Threading.Thread.Sleep(500);

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

		private void InitStyles()
		{
			RefreshButtonContent = new GUIContent(EditorGUIUtility.FindTexture("Refresh"), "Refresh references...");
			TrackSelectionButtonContent = new GUIContent(EditorGUIUtility.FindTexture("animationvisibilitytoggleon"), "Track selection for...");
			LockToggleOffContent = new GUIContent(EditorGUIUtility.IconContent("IN LockButton").image, "Don't track Unity selection (manually drag in object)");
			LockToggleOnContent = new GUIContent(EditorGUIUtility.IconContent("IN LockButton on").image, LockToggleOffContent.tooltip);

			CopyButtonContent = new GUIContent(EditorGUIUtility.FindTexture("Clipboard"), "Copy the Component + Property names");

			HierarchyUsedByStyle = new GUIStyle();
			HierarchyUsedByStyle.normal.background = Texture2D.whiteTexture;

			GUIStyle slimLabel = new GUIStyle(GUI.skin.label);
			slimLabel.padding = new RectOffset();
			slimLabel.border = new RectOffset();
			slimLabel.margin.left = 0;
			slimLabel.margin.right = 0;

			UrlStyle = new GUIStyle(GUI.skin.label);
			UrlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
			UrlStyle.hover.textColor = UrlStyle.normal.textColor;
			UrlStyle.active.textColor = Color.red;

			ResultPropertyStyle = new GUIStyle(slimLabel);
			ResultPropertyStyle.padding = new RectOffset(0, 0, 1, 0);
			ResultPropertyStyle.margin = new RectOffset(0, 0, 1, 0);
		}

		private void OnGUI()
		{
			if (RefreshButtonContent == null) {
				InitStyles();
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

			float totalWidth = position.width;

			float columnNameWidth = Mathf.Min(totalWidth * 0.3f, 140f);
			float columnComponentWidth = Mathf.Min(totalWidth * 0.25f, 120f);
			float columnPropertyCopyWidth = 24f;

			EditorGUILayout.BeginHorizontal();

			GUILayout.Label(new GUIContent("GameObject", "Click on the GameObject name to ping it."), EditorStyles.boldLabel, GUILayout.Width(columnNameWidth));
			GUILayout.Label(new GUIContent("Component", "Click on the component class name to open it in your IDE."), EditorStyles.boldLabel, GUILayout.Width(columnComponentWidth));
			GUILayout.Label(new GUIContent("Property", "Property name is selectable"), EditorStyles.boldLabel);

			EditorGUILayout.EndHorizontal();

			m_ScrollView = GUILayout.BeginScrollView(m_ScrollView);

			GUIContent displayContent = new GUIContent();

			for (int i = 0; i < m_References.Count; ++i) {
				Result result = m_References[i];

				if (result.TargetObj == null)
					continue;

				GameObject resultGO = (GameObject) EditorUtility.InstanceIDToObject(result.GameObjectInstanceID);

				string resultTypeName = result.TargetObj.GetType().Name;

				EditorGUILayout.BeginHorizontal();


				displayContent.text = $"\"{resultGO.name}\"";
				displayContent.tooltip = resultGO.name;
				if (GUILayout.Button(displayContent, UrlStyle, GUILayout.Width(columnNameWidth))) {
					EditorGUIUtility.PingObject(resultGO);
				}
				EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);


				displayContent.text = resultTypeName;
				displayContent.tooltip = resultTypeName + " "; // Tooltip doesn't display if text & tooltip are the same?
				if (GUILayout.Button(displayContent, UrlStyle, GUILayout.Width(columnComponentWidth))) {

					MonoScript asset = AssetDatabase.FindAssets($"t:script {resultTypeName}")
						.Select(AssetDatabase.GUIDToAssetPath)
						.Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == resultTypeName)
						.Select(AssetDatabase.LoadAssetAtPath<MonoScript>)
						.FirstOrDefault();

					if (asset) {
						AssetDatabase.OpenAsset(asset);
						GUIUtility.ExitGUI();
					}
				}
				EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);



				int prevIndent = EditorGUI.indentLevel;
				EditorGUI.indentLevel = 0;

				float prevLabelWidth = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = 0f;

				// Get rect manually, because implementation has hard-coded two lines height.
				var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, ResultPropertyStyle, GUILayout.ExpandWidth(true));
				EditorGUI.SelectableLabel(rect, result.PropertyName, ResultPropertyStyle);

				EditorGUIUtility.labelWidth = prevLabelWidth;
				EditorGUI.indentLevel = prevIndent;

				if (GUILayout.Button(CopyButtonContent, GUIStyle.none, GUILayout.Width(columnPropertyCopyWidth))) {
					const string arrayPattern = ".Array.data["; // Unity displays Array properties a bit ugly. No need to copy it.

					EditorGUIUtility.systemCopyBuffer = result.PropertyName.Contains(arrayPattern)
						? $"{resultTypeName}.{result.PropertyName.Substring(0, result.PropertyName.IndexOf(arrayPattern))}"
						: $"{resultTypeName}.{result.PropertyName}"
						;

					ShowNotification(new GUIContent($"Copied to clipboard."), 0.3f);
				}

				EditorGUILayout.EndHorizontal();
			}

			GUILayout.EndScrollView();

			GUILayout.FlexibleSpace();

			GUILayout.Label($"Selection used by ({m_References.Count})", EditorStyles.boldLabel);
		}
	}
}