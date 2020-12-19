using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DevLocker.Tools.AssetManagement
{

	/// <summary>
	/// Mass search and rename assets tool.
	/// Can specify pattern for rename.
	/// </summary>
	public class MultiRenameTool : EditorWindow
	{
		[MenuItem("Tools/Asset Management/Multi-Rename Tool", false, 65)]
		static void Init()

		{
			var window = GetWindow<MultiRenameTool>("Multi-Rename Tool");
			window.minSize = new Vector2(350f, 400f);
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
				window._searchObject = _searchObject;
				window._editorLocked = _editorLocked;

				window._searchPattern = _searchPattern;
				window._searchPatternEnabled = _searchPatternEnabled;
				window._replacePattern = _replacePattern;
				window._replacePatternEnabled = _replacePatternEnabled;
				window._prefix = _prefix;
				window._suffix = _suffix;

				window._folders = _folders;
				window._recursiveModes = _recursiveModes;
				window._transformMode = _transformMode;
				window._caseSensitive = _caseSensitive;

				window._useCounters = _useCounters;
				window._startCounter = _startCounter;
				window._counterStep = _counterStep;
				window._counterReset = _counterReset;
				window._counterLeadingZeroes = _counterLeadingZeroes;

				window.Show();
			}
		}

		[Flags]
		private enum RecursiveModes
		{
			None = 0,

			Folders = 1 << 0,
			SceneHierarchy = 1 << 1,
			SubAssets = 1 << 2,

			All = ~0,
		}

		private enum TransformModes
		{
			None,
			ToLower,
			ToUpper,
			CapitalizeWords,
			TrimSpaces,
		}


		private Object _searchObject;
		private string _searchPattern = string.Empty;
		private bool _searchPatternEnabled = true;
		private string _replacePattern = string.Empty;
		private bool _replacePatternEnabled = true;
		private string _prefix = string.Empty;
		private string _suffix = string.Empty;
		private bool _folders = true;
		private RecursiveModes _recursiveModes = RecursiveModes.Folders | RecursiveModes.SceneHierarchy;
		private TransformModes _transformMode = TransformModes.TrimSpaces;
		private bool _caseSensitive = true;

		private bool _useCounters = true;
		private int _startCounter = 1;
		private int _counterStep = 1;
		private int _counterReset = 0;
		private int _counterLeadingZeroes = 1;

		private bool _editorLocked = false;

		private bool _showHelpAbout = false;

		private GUIContent RemoveResultEntryContent = new GUIContent("X", "Remove result entry from the execution list.");
		private const string CountersPattern = @"\d";
		private readonly Color ErrorColor = new Color(0.801f, 0.472f, 0.472f);

		private static char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

		[Serializable]
		private class RenameData
		{
			public Object Target;
			public string RenamedName;
			public string RenamedPath;
			public bool Changed = false;
			public bool Conflict = false;

			public RenameData(Object target, string renameTo)
			{
				Target = target;
				RenamedName = renameTo;
			}

			// Refresh RenamedPath. This is done manually (not with setters / getters),
			// so that fields remain serializable and survive reload assembly.
			public void RefreshNames()
			{
				if (AssetDatabase.Contains(Target) && !string.IsNullOrEmpty(RenamedName)) {

					for(int i = RenamedName.Length - 1; i >= 0; --i) {
						if (Array.IndexOf(InvalidFileNameChars, RenamedName[i]) != -1) {
							RenamedName = RenamedName.Remove(i, 1);
						}
					}

					var assetPath = AssetDatabase.GetAssetPath(Target);
					var assetFolder = assetPath.Substring(0, assetPath.LastIndexOf('/') + 1);
					RenamedPath = assetFolder + RenamedName + Path.GetExtension(assetPath);
				} else {
					RenamedPath = string.Empty;
				}
			}
		}

		private List<RenameData> _renameData = new List<RenameData>();
		private Vector2 _scrollPos;
		private Vector2 _helpAboutScrollPos;


		void OnGUI()
		{
			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Multi-Rename Tool", EditorStyles.boldLabel);

			if (_showHelpAbout) {
				DrawHelpAbout();
				return;
			}

			EditorGUI.BeginChangeCheck();

			if (_editorLocked) {
				_searchObject = EditorGUILayout.ObjectField("Selected Object", _searchObject, _searchObject?.GetType() ?? typeof(Object), false);

			} else {
				EditorGUI.BeginDisabledGroup(true);

				var selection = GetUnitySelection();

				if (selection.Length <= 1) {
					var name = !selection.Any() ? "null" : selection.FirstOrDefault().name;
					EditorGUILayout.TextField("Selected Object", name);
				} else {
					EditorGUILayout.TextField("Selected Object", $"{selection.Length} Objects");
				}
				EditorGUI.EndDisabledGroup();
			}

			EditorGUILayout.BeginHorizontal();
			{
				EditorGUI.BeginDisabledGroup(!_searchPatternEnabled);
				var labelContent = new GUIContent("Search Pattern", "Searched text in the objects' names.\nDisable to match any names.");
				_searchPattern = TextFieldWithPlaceholder(labelContent, _searchPattern, _useCounters ? "Use \\d to match any numbers..." : "");
				EditorGUI.EndDisabledGroup();

				_searchPatternEnabled = EditorGUILayout.Toggle(_searchPatternEnabled, GUILayout.Width(16));

			}
			EditorGUILayout.EndHorizontal();


			EditorGUILayout.BeginHorizontal();
			{
				EditorGUI.BeginDisabledGroup(!_replacePatternEnabled);
				var labelContent = new GUIContent("Replace Pattern", "Replace the searched part of the objects' names.\nDisable to leave final names untouched. Useful with prefix / suffix.");
				_replacePattern = TextFieldWithPlaceholder(labelContent, _replacePattern, _useCounters ? "Use \\d to insert number..." : "");
				EditorGUI.EndDisabledGroup();

				_replacePatternEnabled = EditorGUILayout.Toggle(_replacePatternEnabled, GUILayout.Width(16));

			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Prefix / Suffix", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
			_prefix = EditorGUILayout.TextField(_prefix);
			_suffix = EditorGUILayout.TextField(_suffix);
			EditorGUILayout.EndHorizontal();

			var prevLabelWidth = EditorGUIUtility.labelWidth;

			EditorGUILayout.BeginHorizontal();
			_folders = EditorGUILayout.Toggle("Folders:", _folders);

			GUILayout.Space(8);

			EditorGUIUtility.labelWidth = 70;
			_recursiveModes = (RecursiveModes) EditorGUILayout.EnumFlagsField("Recursive:", _recursiveModes, GUILayout.Width(160));
			EditorGUIUtility.labelWidth = prevLabelWidth;

			GUILayout.FlexibleSpace();

			EditorGUILayout.EndHorizontal();


			EditorGUILayout.BeginHorizontal();
			_caseSensitive = EditorGUILayout.Toggle("Case Sensitive:", _caseSensitive);

			GUILayout.Space(8);

			EditorGUIUtility.labelWidth = 70;
			_transformMode = (TransformModes)EditorGUILayout.EnumPopup("Transform:", _transformMode, GUILayout.Width(160));
			EditorGUIUtility.labelWidth = prevLabelWidth;

			GUILayout.FlexibleSpace();

			EditorGUILayout.EndHorizontal();

			_useCounters = EditorGUILayout.Toggle("Use \"\\d\" numbers:", _useCounters);
			if (_useCounters) {

				EditorGUILayout.BeginHorizontal();
				{
					GUILayout.Space(16);
					EditorGUIUtility.labelWidth = 50;
					_startCounter = EditorGUILayout.IntField("Start:", _startCounter, GUILayout.Width(80f));
					EditorGUIUtility.labelWidth = 100;
					_counterReset = EditorGUILayout.IntField("Reset at:", _counterReset, GUILayout.Width(130f));
				}
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.BeginHorizontal();
				{
					GUILayout.Space(16);
					EditorGUIUtility.labelWidth = 50;
					_counterStep = EditorGUILayout.IntField("Step:", _counterStep, GUILayout.Width(80f));
					EditorGUIUtility.labelWidth = 100;
					_counterLeadingZeroes = EditorGUILayout.IntField("Leading zeros:", _counterLeadingZeroes, GUILayout.Width(130f));

					_counterReset = Mathf.Max(0, _counterReset);
					_counterLeadingZeroes = Mathf.Max(1, _counterLeadingZeroes);
				}
				EditorGUILayout.EndHorizontal();

				EditorGUIUtility.labelWidth = prevLabelWidth;
				GUILayout.Label($"Add \"{CountersPattern}\" in any of the fileds above to be searched / replaced by number.", EditorStyles.helpBox);
			}

			if (EditorGUI.EndChangeCheck()) {
				RefreshRenameData();
			}

			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("Search Selected")) {

					IReadOnlyList<Object> searchObjects;

					if (_editorLocked) {
						searchObjects = _searchObject ? new List<Object> { _searchObject } : new List<Object>();
					} else {
						searchObjects = GetUnitySelection();
					}

					if (searchObjects.Count == 0) {
						EditorUtility.DisplayDialog("Error", "Please select some assets/objects to search in.", "Ok");
						return;
					}

					PerformSearch(searchObjects);
				}

				if (GUILayout.Button("Help", GUILayout.Width(45f))) {
					_showHelpAbout = true;
				}
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginDisabledGroup(_renameData.Count == 0);
			{
				if (GUILayout.Button("Execute Rename")) {
					bool agreed = true;

					if (_renameData.Any(rd => rd.Conflict)) {
						agreed = EditorUtility.DisplayDialog(
							"File conflicts!",
							"There are file name conflicts!\nFiles won't be renamed if another file with the same name exists.\nDo you want to proceed anyway?",
							"Yes!", "No"
							);
					}

					if (agreed) {
						ExecuteRename();
					}
				}

				if (GUILayout.Button("Clear", GUILayout.Width(45f))) {
					_renameData.Clear();
				}
			}
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.EndHorizontal();

			GUILayout.Label("Results:", EditorStyles.boldLabel);
			DrawDragObjects();

			DrawResults();
		}

		private Object[] GetUnitySelection()
		{
			// If scene game objects selected, just return that.
			// This fails if user selects folders on the left pane in project two-column view.
			if (!Selection.objects.All(AssetDatabase.Contains)) {	// All(empty) => true
				return Selection.objects;
			}

			// Selection.assetGUIDs doesn't guarantee order, but gives selected folders on the left pane of the project two-column view.
			// Order is important if used with counters.
			// It also has the latest selection (folders on left pane VS selected assets on right pane). Except for scene GOs.
			// Selection.objects doesn't update if folders on left side are selected.
			// Selection.assetGUIDs wouldn't give sub assets (because they all have the same GUIDs).
			// Selection.assetGUIDs wouldn't give scene game objects.
			var assetObjects = Selection.assetGUIDs
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(AssetDatabase.LoadMainAssetAtPath)
			;


			// Selecting folders in the left pane would result in folder objects different from the Selection.objects.
			// This fails if user selects folders on both panes, but who cares...
			// DefaultAsset is folder (probably).
			if (assetObjects.Any() && assetObjects.All(obj => obj is DefaultAsset && !Selection.objects.Contains(obj))) {
				return assetObjects.ToArray();
			} else {

				// Selection.objects keeps the order in which the user selected the objects.
				return Selection.objects;
			}
		}

		private void PerformSearch(IReadOnlyList<Object> targets)
		{
			_renameData.Clear();

			List<Object> allTargets = new List<Object>(targets.Count);
			foreach (var target in targets) {

				// Scriptable object with deleted script will cause this.
				if (target == null) {
					Debug.LogError($"Invalid asset found at \"{AssetDatabase.GetAssetPath(target)}\".");
					continue;
				}

				if (!allTargets.Contains(target)) {
					allTargets.Add(target);
				}

				if ((_recursiveModes & RecursiveModes.Folders) != 0) {
					var includeSubAssets = (_recursiveModes & RecursiveModes.SubAssets) != 0;
					TryAppendFolderAssets(target, allTargets, includeSubAssets);
				}

				if ((_recursiveModes & RecursiveModes.SubAssets) != 0) {
					TryAppendSubAssets(target, allTargets);
				}

				if ((_recursiveModes & RecursiveModes.SceneHierarchy) != 0) {
					TryAppendSceneHierarchyGameObjects(target, allTargets);
				}
			}

			int nextCounter = _startCounter;

			// Force find results according to search. Otherwise will match all files.
			var prevReplaceEnabled = _replacePatternEnabled;
			_replacePatternEnabled = true;

			for (int i = 0; i < allTargets.Count; ++i) {
				var target = allTargets[i];

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Searching...", $"{target.name}", (float)i / allTargets.Count);
				if (cancel) {
					break;
				}

				// Folder (probably).
				if (target is DefaultAsset && !_folders)
					continue;

				string targetName = target.name;
				string renamedName;

				if (TryRename(targetName, nextCounter, out renamedName)) {
					_renameData.Add(new RenameData(target, renamedName));

					nextCounter += _counterStep;
					if (_counterReset > 0 && nextCounter >= _counterReset) {
						nextCounter = 0;
					}
				}
			}

			_replacePatternEnabled = prevReplaceEnabled;

			EditorUtility.ClearProgressBar();
		}

		private bool TryRename(string targetName, int counter, out string renamedName)
		{
			var counterStr = counter.ToString().PadLeft(_counterLeadingZeroes, '0');
			var replacePattern = _useCounters ? _replacePattern.Replace(CountersPattern, counterStr) : _replacePattern;
			var prefixPattern = (_useCounters ? _prefix.Replace(CountersPattern, counterStr) : _prefix).TrimStart();
			var suffixPattern = (_useCounters ? _suffix.Replace(CountersPattern, counterStr) : _suffix).TrimEnd();
			var regexOptions = _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

			if (!_replacePatternEnabled) {
				renamedName = prefixPattern + TransformName(targetName) + suffixPattern;
				return true;
			}

			// Full name change.
			if (string.IsNullOrEmpty(_searchPattern) || !_searchPatternEnabled) {
				renamedName = prefixPattern + TransformName(replacePattern) + suffixPattern;
				return true;
			}

			var searchPattern = _useCounters
				? Regex.Escape(_searchPattern).Replace($"\\{CountersPattern}", $"{CountersPattern}+")
				: Regex.Escape(_searchPattern);

			if (!Regex.IsMatch(targetName, searchPattern, regexOptions)) {
				renamedName = string.Empty;
				return false;
			}

			renamedName = Regex.Replace(targetName, searchPattern, replacePattern, regexOptions);
			renamedName = prefixPattern + TransformName(renamedName) + suffixPattern;

			return true;
		}

		private void TryAppendFolderAssets(Object target, List<Object> allTargets, bool includeSubAssets)
		{
			// Folder (probably).
			if (AssetDatabase.Contains(target) && target is DefaultAsset) {
				var guids = AssetDatabase.FindAssets("", new string[] { AssetDatabase.GetAssetPath(target) });
				foreach (var guid in guids) {
					var foundTarget = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));

					// Scriptable object with deleted script will cause this.
					if (foundTarget == null) {
						Debug.LogError($"Invalid asset found at \"{AssetDatabase.GUIDToAssetPath(guid)}\".");
						continue;
					}

					if (!allTargets.Contains(foundTarget)) {
						allTargets.Add(foundTarget);
					}

					if (includeSubAssets) {
						TryAppendSubAssets(foundTarget, allTargets);
					}
				}
			}
		}

		private void TryAppendSubAssets(Object target, List<Object> allTargets)
		{
			if (AssetDatabase.Contains(target)) {
				var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(target));
				foreach (var subAsset in subAssets) {
					// Scriptable object with deleted script will cause this.
					if (subAsset == null) {
						Debug.LogError($"Invalid asset found at \"{AssetDatabase.GetAssetPath(target)}\".");
						continue;
					}

					if (!allTargets.Contains(subAsset)) {
						allTargets.Add(subAsset);
					}
				}
			}
		}

		private void TryAppendSceneHierarchyGameObjects(Object target, List<Object> allTargets)
		{
			if (!AssetDatabase.Contains(target) && target is GameObject) {
				var go = (GameObject) target;
				foreach (var foundTarget in go.GetComponentsInChildren<Transform>().Select(t => t.gameObject)) {
					if (!allTargets.Contains(foundTarget)) {
						allTargets.Add(foundTarget);
					}
				}
			}
		}

		private void RefreshRenameData()
		{
			int nextCounter = _startCounter;

			for(int i = 0; i < _renameData.Count; ++i) {
				var renameData = _renameData[i];
				renameData.Changed = false;

				// Got deleted in the meantime?
				if (renameData.Target == null)
					continue;

				// If name did not match, show empty sting.
				TryRename(renameData.Target.name, nextCounter, out renameData.RenamedName);
				renameData.RefreshNames();

				nextCounter += _counterStep;
				if (_counterReset > 0 && nextCounter >= _counterReset) {
					nextCounter = 0;
				}
			}

			// Mark for conflicts after all names have been refreshed to check for conflicts between results as well.
			foreach(var renameData in _renameData) {
				MarkConflicts(renameData);
			}
		}

		// Checks and marks RenameData if it conflicts with any other assets or entries from the results.
		private void MarkConflicts(RenameData renameData)
		{
			renameData.Conflict = false;

			// Check if file will conflict with this name.
			if (!renameData.Target.name.Equals(renameData.RenamedName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(renameData.RenamedPath)) {

				if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(renameData.RenamedPath))) {
					renameData.Conflict = true;
					return;
				}

				foreach(var otherData in _renameData) {
					if (otherData == renameData)
						continue;

					if (renameData.RenamedPath == otherData.RenamedPath) {
						renameData.Conflict = true;
						otherData.Conflict = true;

						// NOTE: this might not mark all available conflicts.
						return;
					}
				}
			}
		}


		private void DrawResults()
		{
			EditorGUILayout.BeginVertical();
			_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, false, false);

			var prevBackground = GUI.backgroundColor;

			for(int i = 0; i <_renameData.Count; ++i) {
				var data = _renameData[i];

				EditorGUILayout.BeginHorizontal();

				EditorGUILayout.ObjectField(data.Target, data.Target.GetType(), false);

				if (data.Changed) {
					GUI.backgroundColor = Color.yellow;
				}

				if (data.Target && data.Target.name.Equals(data.RenamedName, StringComparison.Ordinal)) {
					GUI.backgroundColor = new Color(0.305f, 0.792f, 0.470f);
				}

				if (data.Conflict) {
					GUI.backgroundColor = ErrorColor;
				}

				if (string.IsNullOrEmpty(data.RenamedName)) {
					GUI.backgroundColor = ErrorColor;
				}

				EditorGUI.BeginChangeCheck();
				data.RenamedName = EditorGUILayout.TextField(data.RenamedName);

				if (EditorGUI.EndChangeCheck()) {
					data.Changed = true;
					data.RefreshNames();

					// Mark (or clear) any conflicts with other result entries.
					foreach (var renameData in _renameData) {
						MarkConflicts(renameData);
					}
				}

				GUI.backgroundColor = prevBackground;

				if (GUILayout.Button(RemoveResultEntryContent, GUILayout.Width(20.0f), GUILayout.Height(16.0f))) {
					_renameData.RemoveAt(i);
					i--;
				}

				// TODO: Add indication that there are names clashing/overwriting.

				EditorGUILayout.EndHorizontal();
			}

			GUI.backgroundColor = prevBackground;

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private static GUIStyle DragDropZoneStyle = null;
		private void DrawDragObjects()
		{
			if (DragDropZoneStyle == null) {
				DragDropZoneStyle = new GUIStyle(EditorStyles.helpBox);
				DragDropZoneStyle.alignment = TextAnchor.MiddleCenter;
			}

			var dropObjectsRect = EditorGUILayout.GetControlRect(GUILayout.Height(EditorGUIUtility.singleLineHeight * 1.5f));

			if (_renameData.Any(rd => rd.Conflict)) {
				// Draw here to keep Layout structure so typing doesn't get interrupted.
				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = ErrorColor;
				GUI.Label(dropObjectsRect, "File name conflicts present!!!", DragDropZoneStyle);
				GUI.backgroundColor = prevColor;
			} else {
				GUI.Label(dropObjectsRect, "Drag and drop objects here to add to list!", DragDropZoneStyle);
			}

			if (dropObjectsRect.Contains(Event.current.mousePosition)) {

				if (Event.current.type == EventType.DragUpdated) {
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

					Event.current.Use();

				} else if (Event.current.type == EventType.DragPerform) {
					var draggedDatas = DragAndDrop.objectReferences
						.Where(o => _renameData.FindIndex(rd => rd.Target == o) == -1)	// No duplicates
						.Select(o => new RenameData(o, ""))
						;
					_renameData.AddRange(draggedDatas);
					RefreshRenameData();

					Event.current.Use();
				}
			}
		}

		private void ExecuteRename()
		{
			bool hasErrors = false;
			bool assetsChanged = false;

			var builder = new StringBuilder();
			builder.AppendLine("Rename results:");

			// Collect original paths of GameObjects in the scenes, before actually changing them.
			var sceneObjectPaths = new Dictionary<Object, string>();
			foreach(var data in _renameData) {
				var go = data.Target as GameObject;
				var targetPath = data.Target.name;

				if (go && !AssetDatabase.Contains(data.Target)) {
					while (go.transform.parent) {
						go = go.transform.parent.gameObject;
						targetPath = go.name + "/" + targetPath;
					}

					sceneObjectPaths.Add(data.Target, targetPath);
				}
			}

			for (int i = 0; i < _renameData.Count; ++i) {
				var data = _renameData[i];

				// Got deleted in the meantime?
				if (data.Target == null) {
					data.RenamedName = string.Empty;
					data.RefreshNames();
					continue;
				}

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Renaming...", $"{data.Target.name}", (float)i / _renameData.Count);
				if (cancel) {
					break;
				}

				// Invalid data, probably no match was found.
				if (string.IsNullOrEmpty(data.RenamedName))
					continue;

				data.RenamedName = data.RenamedName.Trim();
				data.RefreshNames();

				if (AssetDatabase.Contains(data.Target)) {

					if (AssetDatabase.IsSubAsset(data.Target)) {

						builder.AppendLine($"{AssetDatabase.GetAssetPath(data.Target)} - {data.Target.name} => {data.RenamedName}");
						data.Target.name = data.RenamedName;
						EditorUtility.SetDirty(data.Target);
						assetsChanged = true;

					} else {

						string targetPath = AssetDatabase.GetAssetPath(data.Target);

						string error = AssetDatabase.RenameAsset(targetPath, data.RenamedName);
						if (!string.IsNullOrEmpty(error)) {
							Debug.LogError($"Could not rename asset: \"{targetPath}\" to \"{data.RenamedName}\". Reason:\n{error}.", data.Target);
							hasErrors = true;
						} else {
							builder.AppendLine($"{targetPath} => {data.RenamedName}");
							assetsChanged = true;
						}
					}
				} else {
					Undo.RecordObject(data.Target, "Multi-Rename");

					data.Target.name = data.RenamedName;
					string targetPath;

					if (!sceneObjectPaths.TryGetValue(data.Target, out targetPath)) {
						targetPath = data.Target.name;
					}

					builder.AppendLine($"{targetPath} => {data.RenamedName}");
				}
			}

			Debug.Log(builder.ToString());

			EditorUtility.ClearProgressBar();

			if (assetsChanged) {
				AssetDatabase.SaveAssets();
			}

			if (hasErrors) {
				EditorUtility.DisplayDialog("Error", "Something bad happened while executing the operation. Check the error logs.", "I Will!");
			}
		}

		private string TransformName(string targetName)
		{
			StringBuilder sb;

			switch (_transformMode) {
				case TransformModes.None:
					return targetName;

				case TransformModes.ToLower:
					return targetName.ToLower();

				case TransformModes.ToUpper:
					return targetName.ToUpper();

				case TransformModes.CapitalizeWords:
					sb = new StringBuilder(targetName.Length);
					for(int i = 0; i < targetName.Length; ++i) {
						var ch = targetName[i];
						if (i == 0) {
							sb.Append(char.ToUpper(ch));
							continue;
						}

						var prevCh = targetName[i - 1];
						if (!char.IsLetter(prevCh)) {
							ch = char.ToUpper(ch);
						}

						sb.Append(ch);
					}

					return sb.ToString();

				case TransformModes.TrimSpaces:
					return targetName.Trim();

				default:
					throw new NotSupportedException();
			}
		}

		private static GUIStyle PlaceholderTextStyle = null;
		private static string TextFieldWithPlaceholder(GUIContent label, string text, string placeholderText)
		{
			if (PlaceholderTextStyle == null) {
				PlaceholderTextStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
				PlaceholderTextStyle.alignment = EditorStyles.label.alignment;
			}

			var rect = EditorGUILayout.GetControlRect();
			text = EditorGUI.TextField(rect, label, text);

			if (string.IsNullOrEmpty(text)) {
				rect.width -= EditorGUIUtility.labelWidth;
				rect.x += EditorGUIUtility.labelWidth;
				EditorGUI.LabelField(rect, placeholderText, PlaceholderTextStyle);
			}

			return text;
		}

		private void DrawHelpAbout()
		{
			_helpAboutScrollPos = EditorGUILayout.BeginScrollView(_helpAboutScrollPos);

			EditorGUILayout.LabelField("Pro Tips:", EditorStyles.boldLabel);

			const string tips =
				"Pro Tip # 1:\n" +
				"Modify results or query to your liking.\n" +
				"Query changes will update results instantly.\n" +
				"\n" +
				"Pro Tip # 2:\n" +
				"\"Use numbers\" will parse your search / replace patterns for numbers.\n" +
				"Use \"\\d\" to indicate numbers in your pattern.\n" +
				"\n" +
				"Pro Tip # 3:\n" +
				"Deactivate search pattern to match any names.\n" +
				"Deactivate replace pattern to keep the original name.\n" +
				"(useful with prefix / suffix)\n" +
				"\n" +
				"Pro Tip # 4:\n" +
				"Works with assets and scene objects.\n" +
				"\n" +
				"Pro Tip # 5:\n" +
				"Check logs after renaming has completed.";

			EditorGUILayout.LabelField(tips, EditorStyles.helpBox);
			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("About:", EditorStyles.boldLabel);

			var urlStyle = new GUIStyle(EditorStyles.label);
			urlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
			urlStyle.active.textColor = Color.red;

			const string mail = "NibbleByte3@gmail.com";

			GUILayout.Label("Created by Filip Slavov", GUILayout.ExpandWidth(false));
			if (GUILayout.Button(mail, urlStyle, GUILayout.ExpandWidth(false))) {
				Application.OpenURL("mailto:" + mail);
			}

			if (GUILayout.Button("Help / Documentation", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var assets = AssetDatabase.FindAssets("MultiRenameTool-Documentation");
				if (assets.Length == 0) {
					EditorUtility.DisplayDialog("Documentation missing!", "The documentation you requested is missing.", "Ok");
				} else {
					Application.OpenURL(Environment.CurrentDirectory + "/" + AssetDatabase.GUIDToAssetPath(assets[0]));
				}
			}

			if (GUILayout.Button("Plugin at Asset Store", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var githubURL = "https://assetstore.unity.com/packages/tools/utilities/multi-rename-tool-170616";
				Application.OpenURL(githubURL);
			}

			if (GUILayout.Button("Source at GitHub", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var githubURL = "https://github.com/NibbleByte/UnityAssetManagementTools";
				Application.OpenURL(githubURL);
			}

			EditorGUILayout.Space();

			if (GUILayout.Button("Close", GUILayout.MaxWidth(150f))) {
				_showHelpAbout = false;
				return;
			}

			EditorGUILayout.Space();

			GUILayout.FlexibleSpace();
		}
	}
}
