using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
				window.Show();
			}
		}


		private Object _searchObject;
		private string _searchPattern = string.Empty;
		private bool _searchPatternEnabled = true;
		private string _replacePattern = string.Empty;
		private bool _replacePatternEnabled = true;
		private string _prefix = string.Empty;
		private string _suffix = string.Empty;
		private bool _folders = true;
		private bool _recursive = true;
		private bool _caseSensitive = true;

		private bool _useCounters = false;
		private int _startCounter = 0;
		private int _counterStep = 1;
		private int _counterReset = 0;
		private int _counterLeadingZeroes = 1;

		private bool _editorLocked = false;

		private const string CountersPattern = @"\d";

		[Serializable]
		private class RenameData
		{
			public Object Target;
			public string RenamedName;
			public bool Changed = false;

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

			EditorGUILayout.LabelField("Multi-Rename Tool", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();

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

			EditorGUILayout.BeginHorizontal();
			_folders = EditorGUILayout.Toggle("Folders", _folders);
			GUILayout.Space(8);
			_recursive = EditorGUILayout.Toggle("Recursive", _recursive);
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndHorizontal();

			_caseSensitive = EditorGUILayout.Toggle("Case Sensitive", _caseSensitive);

			_useCounters = EditorGUILayout.Toggle("Use numbers", _useCounters);
			if (_useCounters) {
				var prevLabelWidth = EditorGUIUtility.labelWidth;

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

			if (GUILayout.Button("Search Selected")) {

				List<Object> searchObjects;

				if (_editorLocked) {
					searchObjects = _searchObject ? new List<Object> { _searchObject } : new List<Object>();
				} else {
					searchObjects = new List<Object>();

					// Selection.objects keeps the order in which the user selected the objects.
					searchObjects.AddRange(Selection.objects);

					// Selection.assetGUIDs doesn't guarantee order, but gives selected folders on the left pane of the project two-column view.
					// Order is important if used with counters.
					var objectsByGuids = Selection.assetGUIDs
					.Select(AssetDatabase.GUIDToAssetPath)
					.Select(AssetDatabase.LoadAssetAtPath<Object>)
					;

					foreach(var obj in objectsByGuids) {
						if (!searchObjects.Contains(obj)) {
							searchObjects.Add(obj);
						}
					}
				}

				if (searchObjects.Count == 0) {
					EditorUtility.DisplayDialog("Error", "Please select some assets/objects to search in.", "Ok");
					return;
				}

				PerformSearch(searchObjects);
			}

			EditorGUI.BeginDisabledGroup(_renameData.Count == 0);
			if (GUILayout.Button("Execute Rename")) {
				ExecuteRename();
			}
			EditorGUI.EndDisabledGroup();

			DrawResults();
		}

		private void PerformSearch(List<Object> targets)
		{
			_renameData.Clear();

			List<Object> allTargets = new List<Object>(targets.Count);
			foreach (var target in targets) {

				if (!allTargets.Contains(target)) {
					allTargets.Add(target);
				}

				if (_recursive) {
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

								if (!allTargets.Contains(foundTarget)) {
									allTargets.Add(foundTarget);
								}
							}
						}

					} else {

						var go = (GameObject) target;
						foreach (var foundTarget in go.GetComponentsInChildren<Transform>().Select(t => t.gameObject)) {
							if (!allTargets.Contains(foundTarget)) {
								allTargets.Add(foundTarget);
							}
						}
					}
				}
			}

			int nextCounter = _startCounter;

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
				string renamedName;

				if (TryRename(targetName, nextCounter, out renamedName)) {
					_renameData.Add(new RenameData(target, renamedName));

					nextCounter += _counterStep;
					if (_counterReset > 0 && nextCounter >= _counterReset) {
						nextCounter = 0;
					}
				}
			}

			EditorUtility.ClearProgressBar();
		}

		private bool TryRename(string targetName, int counter, out string renamedName)
		{
			var counterStr = counter.ToString().PadLeft(_counterLeadingZeroes, '0');
			var replacePattern = _useCounters ? _replacePattern.Replace(CountersPattern, counterStr) : _replacePattern;
			var prefixPattern = _useCounters ? _prefix.Replace(CountersPattern, counterStr) : _prefix;
			var suffixPattern = _useCounters ? _suffix.Replace(CountersPattern, counterStr) : _suffix;
			var regexOptions = _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

			if (!_replacePatternEnabled) {
				renamedName = prefixPattern + targetName + suffixPattern;
				return true;
			}

			// Full name change.
			if (string.IsNullOrEmpty(_searchPattern) || !_searchPatternEnabled) {
				renamedName = prefixPattern + replacePattern + suffixPattern;
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
			renamedName = prefixPattern + renamedName + suffixPattern;

			return true;
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

				nextCounter += _counterStep;
				if (_counterReset > 0 && nextCounter >= _counterReset) {
					nextCounter = 0;
				}
			}
		}


		private void DrawResults()
		{
			GUILayout.Label("Results:", EditorStyles.boldLabel);

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

				if (string.IsNullOrEmpty(data.RenamedName)) {
					GUI.backgroundColor = new Color(0.801f, 0.472f, 0.472f);
				}

				EditorGUI.BeginChangeCheck();
				data.RenamedName = EditorGUILayout.TextField(data.RenamedName);

				if (EditorGUI.EndChangeCheck()) {
					data.Changed = true;
				}

				GUI.backgroundColor = prevBackground;

				if (GUILayout.Button("X", GUILayout.Width(20.0f), GUILayout.Height(16.0f))) {
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

		private void ExecuteRename()
		{
			bool hasErrors = false;

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
					continue;
				}

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Renaming...", $"{data.Target.name}", (float)i / _renameData.Count);
				if (cancel) {
					break;
				}

				// Invalid data, probably no match was found.
				if (string.IsNullOrEmpty(data.RenamedName))
					continue;

				if (AssetDatabase.Contains(data.Target)) {
					string targetPath = AssetDatabase.GetAssetPath(data.Target);

					string error = AssetDatabase.RenameAsset(targetPath, data.RenamedName);
					if (!string.IsNullOrEmpty(error)) {
						Debug.LogError($"Could not rename asset: \"{targetPath}\" to \"{data.RenamedName}\".\nError: {error}.");
						hasErrors = true;
					} else {
						builder.AppendLine($"{targetPath} => {data.RenamedName}");
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

			if (hasErrors) {
				EditorUtility.DisplayDialog("Error", "Something bad happened while executing the operation. Check the error logs.", "I Will!");
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
	}
}
