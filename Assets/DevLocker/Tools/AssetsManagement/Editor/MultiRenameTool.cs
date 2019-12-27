using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DevLocker.Tools.AssetsManagement
{

	/// <summary>
	/// Mass search and rename assets tool.
	/// Can specify pattern for rename.
	/// </summary>
	public class MultiRenameTool : EditorWindow
	{
		[MenuItem("Tools/Assets Management/Multi-Rename Tool", false, 65)]
		static void Init()
		
		{
			GetWindow<MultiRenameTool>("Multi-Rename Tool");
		}

		private void OnSelectionChange()
		{
			Repaint();
		}

		// Hidden Unity function, used to draw lock and other buttons at the top of the window.
		private void ShowButton(Rect rect)
		{
			var lockButtonStyle = GUI.skin.GetStyle("IN LockButton");

			_editorLocked = GUI.Toggle(rect, _editorLocked, GUIContent.none, lockButtonStyle);


			rect.x -= rect.width - 2.0f;
			rect.y += -2.0f;

			if (GUI.Button(rect, "+", GUI.skin.label)) {
				MultiRenameTool window = CreateInstance<MultiRenameTool>();
				window.titleContent = titleContent;
				window.Show();
			}
		}


		private Object _searchObject;
		private string _searchPattern = string.Empty;
		private string _replacePattern = string.Empty;
		private string _prefix = string.Empty;
		private string _suffix = string.Empty;
		private bool _folders = true;
		private bool _recursive = true;
		private bool _caseSensitive = true;

		private bool _editorLocked = false;

		private class RenameData
		{
			public Object Target;
			public string RenamedName;

			public RenameData(Object target, string renameTo)
			{
				Target = target;
				RenamedName = renameTo;
			}
		}

		private List<RenameData> _renameData = new List<RenameData>();
		private Vector2 _scrollPos;


		void OnGUI()
		{
			EditorGUILayout.Space();

			if (_editorLocked) {
				_searchObject = EditorGUILayout.ObjectField("Selected Object", _searchObject, _searchObject?.GetType() ?? typeof(Object), false);

			} else {
				EditorGUI.BeginDisabledGroup(true);
				if (Selection.assetGUIDs.Length <= 1) {
					var path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs.FirstOrDefault());
					var name = string.IsNullOrEmpty(path) ? "null" : Path.GetFileNameWithoutExtension(path);
					EditorGUILayout.TextField("Selected Object", name);
				} else {
					EditorGUILayout.TextField("Selected Object", $"{Selection.assetGUIDs.Length} Objects");
				}
				EditorGUI.EndDisabledGroup();
			}


			_searchPattern = EditorGUILayout.TextField("Search Pattern", _searchPattern);
			_replacePattern = EditorGUILayout.TextField("Replace Pattern", _replacePattern);

			EditorGUILayout.BeginHorizontal();
			_prefix = EditorGUILayout.TextField("Prefix / Suffix", _prefix);
			_suffix = EditorGUILayout.TextField(_suffix);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			_folders = EditorGUILayout.Toggle("Folders", _folders);
			_recursive = EditorGUILayout.Toggle("Recursive", _recursive);
			EditorGUILayout.EndHorizontal();

			_caseSensitive = EditorGUILayout.Toggle("Case Sensitive", _caseSensitive);

			if (GUILayout.Button("Search Selected")) {

				var searchObjects = Selection.assetGUIDs
					.Select(AssetDatabase.GUIDToAssetPath)
					.Select(AssetDatabase.LoadAssetAtPath<Object>)
					.ToArray();

				if (_editorLocked) {
					searchObjects = _searchObject ? new Object[] { _searchObject } : new Object[0];
				}

				if (searchObjects.Length == 0) {
					EditorUtility.DisplayDialog("Error", "Please select some assets/objects to search in.", "Ok");
					return;
				}

				PerformSearch(searchObjects, _searchPattern, _replacePattern, _recursive);
			}

			EditorGUI.BeginDisabledGroup(_renameData.Count == 0);
			if (GUILayout.Button("Execute Rename")) {
				ExecuteRename();
			}
			EditorGUI.EndDisabledGroup();

			DrawResults();
		}

		private void PerformSearch(Object[] targets, string searchPattern, string replacePattern, bool recursive)
		{
			_renameData.Clear();

			List<Object> allTargets = new List<Object>(targets.Length);
			foreach (var target in targets) {
				allTargets.Add(target);

				if (recursive) {
					if (AssetDatabase.Contains(target)) {

						// Folder (probably).
						if (target is DefaultAsset) {

							var guids = AssetDatabase.FindAssets("", new string[] { AssetDatabase.GetAssetPath(target) });
							foreach (var guid in guids) {
								var foundTarget = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));

								// Scriptable object with deleted script will cause this.
								if (foundTarget == null) {
									Debug.LogError($"Invalid asset found at \"{AssetDatabase.GUIDToAssetPath(guid)}\".");
									continue;
								}

								if (!AssetDatabase.IsMainAsset(foundTarget))
									continue;
								
								allTargets.Add(foundTarget);
							}
						}

					} else {

						var go = (GameObject) target;
						allTargets.AddRange(go.GetComponentsInChildren<Transform>().Select(t => t.gameObject));
					}
				}
			}

			allTargets = allTargets.Distinct().ToList();

			for (int i = 0; i < allTargets.Count; ++i) {
				var target = allTargets[i];

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Searching...", $"{target.name}", (float)i / allTargets.Count);
				if (cancel) {
					break;
				}

				// Folder (probably).
				if (target is DefaultAsset && !_folders)
					continue;

				string targetName = AssetDatabase.Contains(target) ? Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(target)) : target.name;
				string renamedName = targetName;

				if (!string.IsNullOrEmpty(searchPattern)) {
					var start = renamedName.IndexOf(searchPattern, _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
					if (start != -1) {
						renamedName = renamedName.Remove(start, searchPattern.Length);
						renamedName = renamedName.Insert(start, replacePattern);
						renamedName = _prefix + renamedName + _suffix;
					}

				} else {
					renamedName = _prefix + renamedName + _suffix;
				}


				if (targetName != renamedName) {
					_renameData.Add(new RenameData(target, renamedName));
				}
			}

			EditorUtility.ClearProgressBar();
		}

		private void DrawResults()
		{
			GUILayout.Label("Results:", EditorStyles.boldLabel);

			EditorGUILayout.BeginVertical();
			_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, false, false);

			for(int i = 0; i <_renameData.Count; ++i) {
				var data = _renameData[i];

				EditorGUILayout.BeginHorizontal();

				EditorGUILayout.ObjectField(data.Target, data.Target.GetType(), false);
				data.RenamedName = EditorGUILayout.TextField(data.RenamedName);

				if (GUILayout.Button("X", GUILayout.Width(20.0f), GUILayout.Height(16.0f))) {
					_renameData.RemoveAt(i);
					i--;
				}

				// TODO: Add indication that there are names clashing/overwriting.

				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void ExecuteRename()
		{
			for (int i = 0; i < _renameData.Count; ++i) {
				var data = _renameData[i];

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Renaming...", $"{data.Target.name}", (float)i / _renameData.Count);
				if (cancel) {
					break;
				}

				if (AssetDatabase.Contains(data.Target)) {
					string targetPath = AssetDatabase.GetAssetPath(data.Target);
					
					string error = AssetDatabase.RenameAsset(targetPath, data.RenamedName);
					if (!string.IsNullOrEmpty(error)) {
						Debug.LogError($"Could not rename asset: \"{targetPath}\" to \"{data.RenamedName}\".\nError: {error}.");
					}
				} else {
					Undo.RecordObject(data.Target, "Multi-Rename");
					data.Target.name = data.RenamedName;
				}
			}

			EditorUtility.ClearProgressBar();
		}
	}
}
