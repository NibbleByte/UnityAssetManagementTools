using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DevLocker.Tools.AssetsManagement
{

	/// <summary>
	/// Tool for searching references FAST. 
	/// Search is done by text (instead of loading all the assets) and works only with text assets set in unity.
	/// Created by Filip Slavov to serve the mighty Vesselin Jilov at Snapshot Games.
	/// </summary>
	public class SearchReferencesFast : EditorWindow
	{
		[MenuItem("Tools/Assets Management/Search References (FAST)", false, 61)]
		static void Init()
		{
			var window = GetWindow<SearchReferencesFast>("Search References");
			window._searchFilter.SetTemplateEnabled("Scenes", true);
		}

		private void OnSelectionChange()
		{
			Repaint();
		}

		// Hidden Unity function, used to draw lock and other buttons at the top of the window.
		private void ShowButton(Rect rect)
		{
			if (GUI.Button(rect, "+", GUI.skin.label)) {
				SearchReferencesFast window = CreateInstance<SearchReferencesFast>();
				window.titleContent = titleContent;
				window.Show();

				window._searchText = _searchText;
				window._textToSearch = _textToSearch;

				window._searchMetas = _searchMetas;
				window._searchFilter = _searchFilter.Clone();

				window._searchFilter.RefreshCounters();
			}
		}

		private bool m_ShowPreferences = false;
		private const string PROJECT_EXCLUDES_PATH = "ProjectSettings/SearchReferencesFast.Exclude.txt";

		private bool _searchText = false;
		private string _textToSearch;


		private enum SearchMetas
		{
			DontSearchMetas,
			SearchWithMetas,
			MetasOnly
		}

		private bool _foldOutSearchCriterias = true;
		private SearchMetas _searchMetas = SearchMetas.SearchWithMetas;
		[SerializeField]
		private SearchAssetsFilter _searchFilter = new SearchAssetsFilter() { ExcludePackages = true };

		private string _resultsSearchEntryFilter = "";
		private string _resultsFoundEntryFilter = "";
		private SearchResult _results = new SearchResult();

		private Vector2 _scrollPos;


		private const string TOOL_HELP =
				"Search works only when assets are set in text mode.\n" +
				"It takes the GUIDs (from the meta files) and searches them in the assets as plain text, skipping any actual loading.\n" +
				"Useful when searching in scenes.\n\n" +
				"NOTE: Prefabs in scenes, that contain references to assets, do not store those references in the scene itself and won't be found, unless they are overridden.\n" +
				"NOTE2: Searching for foreign assets (example: meshes/materials/clips in fbx) is supported."
			;


		void OnEnable()
		{
			_searchFilter.RefreshCounters();

			if (File.Exists(PROJECT_EXCLUDES_PATH)) {
				_searchFilter.ExcludePreferences = new List<string>(File.ReadAllLines(PROJECT_EXCLUDES_PATH));
			} else {
				_searchFilter.ExcludePreferences = new List<string>();
			}
		}

		// Sometimes the bold style gets corrupted and displays just black text, for no good reason.
		// This forces the style to reload on re-creation.
		[NonSerialized]
		private GUIStyle BOLDED_FOLDOUT_STYLE;
		private GUIContent RESULTS_SEARCHED_FILTER_LABEL = new GUIContent("Searched Filter", "Filter out results by hiding some search entries.");
		private GUIContent RESULTS_FOUNDED_FILTER_LABEL = new GUIContent("Founded Filter", "Filter out results by hiding some found entries (under each search entry).");
		private GUIContent REPLACE_PREFABS_ENTRY_BTN = new GUIContent("Replace in scenes", "Replace this searched prefab entry with the specified replacement (on the left) in whichever scene it was found.");
		private GUIContent REPLACE_PREFABS_ALL_BTN = new GUIContent("Replace all prefabs", "Replace ALL searched prefab entries with the specified replacement (if provided) in whichever scene they were found.");

		private void InitStyles()
		{
			BOLDED_FOLDOUT_STYLE = new GUIStyle(EditorStyles.foldout);
			BOLDED_FOLDOUT_STYLE.fontStyle = FontStyle.Bold;
		}

		void OnGUI()
		{
			if (BOLDED_FOLDOUT_STYLE == null) {
				InitStyles();
			}

			if (m_ShowPreferences) {
				DrawPreferences();
				return;
			}


			EditorGUILayout.Space();

			_searchText = EditorGUILayout.Toggle("Search Text", _searchText);
			if (_searchText) {
				_textToSearch = EditorGUILayout.TextField("Text", _textToSearch);
			} else {
				EditorGUI.BeginDisabledGroup(true);
				if (Selection.objects.Length <= 1) {
					EditorGUILayout.TextField("Selected Object", Selection.activeObject ? Selection.activeObject.name : "null");
				} else {
					EditorGUILayout.TextField("Selected Object", $"{Selection.objects.Length} Objects");
				}
				EditorGUI.EndDisabledGroup();
			}



			_foldOutSearchCriterias = EditorGUILayout.Foldout(_foldOutSearchCriterias, "Search in:", BOLDED_FOLDOUT_STYLE);

			if (_foldOutSearchCriterias) {
				EditorGUILayout.BeginVertical(EditorStyles.helpBox);

				_searchFilter.DrawIncludeExcludeFolders();


				EditorGUILayout.Space();
				EditorGUILayout.BeginHorizontal();
				_searchMetas = (SearchMetas) EditorGUILayout.EnumPopup("Metas", _searchMetas);
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space();

				_searchFilter.DrawTypeFilters();

				EditorGUILayout.EndVertical();
			}

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Search In Project")) {

				_resultsSearchEntryFilter = string.Empty;
				_resultsFoundEntryFilter = string.Empty;

				if (_searchText) {
					if (string.IsNullOrWhiteSpace(_textToSearch)) {
						EditorUtility.DisplayDialog("Invalid Input", "Please enter some valid text to search for.", "Ok");
						return;
					}

					PerformTextSearch(_textToSearch);
					return;

				} else {
					if (Selection.objects.Length == 0) {
						EditorUtility.DisplayDialog("Invalid Input", "Please select some assets to search for.", "Ok");
						return;
					}

					PerformSearch(Selection.objects);
					return; // HACK: causes Null exception in editor layout system for some reason.
				}

			}

			if (GUILayout.Button("P", GUILayout.Width(20.0f))) {
				m_ShowPreferences = true;
				GUIUtility.ExitGUI();
			}

			if (GUILayout.Button("?", GUILayout.Width(20.0f))) {
				EditorUtility.DisplayDialog("Help", TOOL_HELP, "Ok");
			}

			EditorGUILayout.EndHorizontal();

			DrawResults();
		}

		private void PerformSearch(Object[] targets)
		{
			// Collect all objects guids.
			var targetGuids = new List<SearchEntryData>(targets.Length);
			for (int i = 0; i < targets.Length; ++i) {
				var target = targets[i];
				var targetPath = AssetDatabase.GetAssetPath(target);

				// If object is invalid for some reason - skip. (script of scriptable object was deleted or something)
				if (target == null) {
					Debug.LogWarning("Selected object was invalid!", target);
					continue;
				}

				if (string.IsNullOrEmpty(targetPath)) {

					// If object is prefab placed in the current scene...
#if UNITY_2018_2_OR_NEWER
					var prefab = PrefabUtility.GetCorrespondingObjectFromSource(target);
#else
					var prefab = PrefabUtility.GetPrefabParent(target);
#endif
					if (prefab) {
						target = prefab;
						targetPath = AssetDatabase.GetAssetPath(target);
					} else {
						continue;
					}
				}

				// Folder (probably).
				if (target is DefaultAsset && Directory.Exists(targetPath)) {
					if (!EditorUtility.DisplayDialog("Folder selected", $"Folder '{targetPath}' was selected as target. Do you want to target all the assets inside (recursively)?\nThe more assets you target, the slower the search will be!", "Do it!", "Skip"))
						continue;

					var guids = AssetDatabase.FindAssets("", new string[] { targetPath });
					foreach (var guid in guids) {
						var foundTarget = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));

						if (!SearchEntryData.IsSupported(foundTarget)) {
							// Local Ids of non-foreign nested assets are actually 64bit numbers, but unity provides only 32bit int?
							Debug.LogWarning($"Asset {foundTarget.name} is a normal asset instance nested in another one and is not supported for Unity versions before 2018.2.x for search.", foundTarget);
							continue;
						}

						targetGuids.Add(new SearchEntryData(foundTarget));
					}
					continue;
				}


				if (!SearchEntryData.IsSupported(target)) {
					// Local Ids of non-foreign nested assets are actually 64bit numbers, but unity provides only 32bit int?
					Debug.LogWarning($"Asset {target.name} is a normal asset instance nested in another one and is not supported for Unity versions before 2018.2.x for search.", target);
					continue;
				}
				targetGuids.Add(new SearchEntryData(target));
			}


			if (targetGuids.Count == 0)
				return;


			var searchPaths = _searchFilter.GetFilteredPaths().ToArray();

			_results.Reset();

			// This used to be on demand, but having empty search results is more helpful, then having them missing.
			foreach (var target in targetGuids) {
				_results.Add(target.Target, new SearchResultData() { Root = target.Target });
			}

			var foundGuidsCache = new HashSet<Object>();

			for (int searchIndex = 0; searchIndex < searchPaths.Length; ++searchIndex) {
				var searchPath = searchPaths[searchIndex];
				var searchFullPath = Application.dataPath + searchPath.Remove(0, "Assets".Length);

				// Probably a folder. Skip it.
				if (string.IsNullOrEmpty(Path.GetExtension(searchPath))) {
					continue;
				}

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Searching...", $"{Path.GetFileName(searchPath)}", (float)searchIndex / searchPaths.Length);
				if (cancel) {
					EditorUtility.ClearProgressBar();
					return;
				}

				foundGuidsCache.Clear();


				// Read each line for every target object. Early break when it has a chance.
				var lines = File.ReadLines(searchFullPath);
				if (_searchMetas != SearchMetas.DontSearchMetas) {
					// Read metas as well (embedded materials references for FBX are stored in the meta).
					lines = _searchMetas == SearchMetas.SearchWithMetas ? lines.Concat(File.ReadLines(searchFullPath + ".meta")) : File.ReadLines(searchFullPath + ".meta");
				}

				foreach (var line in lines) {

					foreach (var searchData in targetGuids) {

						if (foundGuidsCache.Contains(searchData.Target))
							continue;

						var matchFound = LineMatchesSearch(searchData, line, searchPath);

						if (matchFound) {

							var foundObj = AssetDatabase.LoadAssetAtPath<Object>(searchPath);

							// If object is invalid for some reason - skip. (script of scriptable object was deleted or something)
							if (foundObj == null) {
								continue;
							}

							if (foundObj != searchData.Target) {

								SearchResultData data = _results[searchData.Target];
								//SearchResultData data;
								//if (!_results.TryGetValue(searchData.Target, out data)) {
								//	data = new SearchResultData() { Root = searchData.Target };
								//	_results.Add(searchData.Target, data);
								//}

								if (!data.Found.Contains(foundObj)) {
									data.Found.Add(foundObj);
									_results.AddType(foundObj.GetType());
								}
							}

							foundGuidsCache.Add(searchData.Target);
						}

					}

					// No more need to check this file anymore.
					if (foundGuidsCache.Count == targetGuids.Count)
						break;
				}
			}

			foreach (var pair in _results.Data) {
				pair.Value.Found.Sort((l, r) => String.Compare(l.name, r.name, StringComparison.Ordinal));
			}

			EditorUtility.ClearProgressBar();
		}

		private static bool LineMatchesSearch(SearchEntryData searchData, string line, string searchPath)
		{
			var matchFound = line.Contains(searchData.Guid) && (string.IsNullOrEmpty(searchData.LocalId) || line.Contains(searchData.LocalId));

			// Embedded asset searching for references in the same main asset file.
			if (searchData.IsSubAsset && searchData.MainAssetPath == searchPath) {
				matchFound = line.Contains(string.Format("{{fileID: {0}}}", searchData.LocalId));   // If reference in the same file, guid is not used.
			}

			return matchFound;
		}

		public static bool PerformSingleSearch(Object asset, string searchPath)
		{
			var searchData = new SearchEntryData(asset);

			var lines = File.ReadLines(searchPath);
			foreach (var line in lines) {
				if (LineMatchesSearch(searchData, line, searchPath))
					return true;
			}

			return false;
		}
		
		public static bool PerformSingleSearch(IEnumerable<Object> assets, string searchPath)
		{
			var searchDatas = assets.Select(a => new SearchEntryData(a)).ToList();
			var lines = File.ReadLines(searchPath);
			
			foreach (var line in lines) {
				if (searchDatas.Any(sd => LineMatchesSearch(sd, line, searchPath)))
					return true;
			}

			return false;
		}

		private void PerformTextSearch(string text)
		{
			var searchPaths = _searchFilter.GetFilteredPaths().ToArray();

			_results.Reset();

			var data = new SearchResultData() { Root = this };

			for (int searchIndex = 0; searchIndex < searchPaths.Length; ++searchIndex) {
				var searchPath = searchPaths[searchIndex];
				var searchFullPath = Application.dataPath + searchPath.Remove(0, "Assets".Length);

				// Probably a folder. Skip it.
				if (string.IsNullOrEmpty(Path.GetExtension(searchPath))) {
					continue;
				}

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Searching...", $"{Path.GetFileName(searchPath)}", (float)searchIndex / searchPaths.Length);
				if (cancel) {
					EditorUtility.ClearProgressBar();
					return;
				}

				// Read each line for every target object. Early break when it has a chance.
				var lines = File.ReadLines(searchFullPath);
				if (_searchMetas != SearchMetas.DontSearchMetas) {
					// Read metas as well (embedded materials references for FBX are stored in the meta).
					lines = _searchMetas == SearchMetas.SearchWithMetas ? lines.Concat(File.ReadLines(searchFullPath + ".meta")) : File.ReadLines(searchFullPath + ".meta");
				}

				foreach (var line in lines) {

					if (line.Contains(text)) {

						var target = AssetDatabase.LoadAssetAtPath<Object>(searchPath);

						// If object is invalid for some reason - skip. (script of scriptable object was deleted or something)
						if (target == null) {
							continue;
						}

						data.Found.Add(target);
						_results.AddType(target.GetType());

						break;
					}
				}
			}

			if (data.Found.Count > 0) {
				_results.Add(this, data);
			}

			EditorUtility.ClearProgressBar();
		}

		private void DrawResults()
		{
			GUILayout.Label("Results:", EditorStyles.boldLabel);


			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField($"References: {_results.Data.Sum(r => r.Value.Found.Count)}");

				var index = EditorGUILayout.Popup(0, _results.ResultTypesNames.Prepend("Select By Type").ToArray());
				if (index > 0) {
					var selectedType = _results.ResultTypesNames.Skip(index - 1).First();

					Selection.objects = _results.Data
						.SelectMany(pair => pair.Value.Found)
						.Where(obj => obj.GetType().Name == selectedType)
						.ToArray();
				}
			}
			EditorGUILayout.EndHorizontal();



			_resultsSearchEntryFilter = EditorGUILayout.TextField(RESULTS_SEARCHED_FILTER_LABEL, _resultsSearchEntryFilter);
			_resultsFoundEntryFilter = EditorGUILayout.TextField(RESULTS_FOUNDED_FILTER_LABEL, _resultsFoundEntryFilter);

			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("Toggle Fold", GUILayout.ExpandWidth(false)) && _results.Data.Count > 0) {
					var toggledShowDetails = !_results.Data[0].Value.ShowDetails;
					_results.Data.ForEach(data => data.Value.ShowDetails = toggledShowDetails);
				}

				DrawReplaceAllPrefabs();

				DrawSaveResultsSlots();
			}
			EditorGUILayout.EndHorizontal();



			EditorGUILayout.BeginVertical();
			_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, false, false);

			for(int resultIndex = 0; resultIndex < _results.Data.Count; ++resultIndex) {
				var data = _results.Data[resultIndex].Value;

				if (!string.IsNullOrEmpty(_resultsSearchEntryFilter) 
					&& (data.Root == null || data.Root.name.IndexOf(_resultsSearchEntryFilter, StringComparison.OrdinalIgnoreCase) == -1))
					continue;

				EditorGUILayout.BeginHorizontal();

				var foldOutRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(12f));
				data.ShowDetails = EditorGUI.Foldout(foldOutRect, data.ShowDetails, "");
				EditorGUILayout.ObjectField(data.Root, data.Root?.GetType(), false);
				if (GUILayout.Button("X", GUILayout.Width(20.0f), GUILayout.Height(14.0f))) {
					_results.Data.RemoveAt(resultIndex);
					--resultIndex;
				}

				EditorGUILayout.EndHorizontal();


				if (data.ShowDetails) {
					EditorGUI.indentLevel += 2;

					for (int i = 0; i < data.Found.Count; ++i) {
						var found = data.Found[i];

						if (!string.IsNullOrEmpty(_resultsFoundEntryFilter) 
							&& (found == null || found.name.IndexOf(_resultsFoundEntryFilter, StringComparison.OrdinalIgnoreCase) == -1))
							continue;

						EditorGUILayout.BeginHorizontal();

						EditorGUILayout.ObjectField(found, found?.GetType() ?? typeof(Object), true);
						if (GUILayout.Button("X", GUILayout.Width(20.0f), GUILayout.Height(14.0f))) {
							data.Found.RemoveAt(i);
							--i;
						}

						EditorGUILayout.EndHorizontal();
					}

					EditorGUI.indentLevel -= 2;
				}

				DrawReplaceSinglePrefabs(data);
				
			}


			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}



		private void DrawReplaceSinglePrefabs(SearchResultData data)
		{
			bool showReplaceButton = data.ShowDetails
			                         && data.Root is GameObject
			                         && data.Found.Any(obj => obj is SceneAsset);


			if (showReplaceButton) {

				GUILayout.Space(8);

				EditorGUILayout.BeginHorizontal();
				GUILayout.Space((EditorGUI.indentLevel + 2) * 16);  // Magic...

				data.ReplacePrefab = (GameObject)EditorGUILayout.ObjectField(data.ReplacePrefab, typeof(GameObject), false);
				EditorGUILayout.LabelField(">>", GUILayout.Width(22f));

				if (GUILayout.Button(REPLACE_PREFABS_ENTRY_BTN, GUILayout.ExpandWidth(false))) {
					if (data.ReplacePrefab == null) {
						if (!EditorUtility.DisplayDialog(
							"Delete Prefab Instances",
							"Delete all instances of the prefab in the scenes in the list?",
							"Yes", "No")) 
						{
							return;
						}
					}

					if (data.Root == null)
						return;

					if (data.ReplacePrefab == data.Root) {
						if (!EditorUtility.DisplayDialog("Wut?!", "This is the same prefab! Are you sure?", "Do it!", "Abort"))
							return;
					}

					bool reparentChildren = false;
					
					if (data.ReplacePrefab != null) {
						var option = EditorUtility.DisplayDialogComplex("Reparent objects?",
							"If prefab has other game objects attached to its children, what should I do with them?",
							"Re-parent",
							"Destroy",
							"Cancel");

						if (option == 2)
							return;

						reparentChildren = option == 0;
					}

					StringBuilder replaceReport = new StringBuilder(300);
					
					if (data.ReplacePrefab != null) {
						replaceReport.AppendLine($"Search For: {data.Root.name}; Replace With: {data.ReplacePrefab.name}; Reparent: {reparentChildren}");
					} else {
						replaceReport.AppendLine($"Search and delete: {data.Root.name}");
					}

					ReplaceSinglePrefabResult(data, reparentChildren, replaceReport);

					Debug.Log($"Replace report:\n" + replaceReport, data.Root);
				}

				EditorGUILayout.EndHorizontal();
			}
		}


		private void DrawReplaceAllPrefabs()
		{
			bool enableReplaceButton = _results.Data.Any(pair => pair.Value.Root is GameObject && pair.Value.Found.Any(obj => obj is SceneAsset));
			EditorGUI.BeginDisabledGroup(!enableReplaceButton);
			if (GUILayout.Button(REPLACE_PREFABS_ALL_BTN, GUILayout.ExpandWidth(false))) {

				var option = EditorUtility.DisplayDialogComplex("Reparent objects?",
					"If prefab has other game objects attached to its children, what should I do with them?",
					"Re-parent",
					"Destroy",
					"Cancel");

				if (option == 2)
					return;

				if (!EditorUtility.DisplayDialog("Are you sure?",
					"This will replace all searched prefabs with the ones specified for replacing, in whichever scenes they were found. If nothing is specified, no replacing will occur.\n\nAre you sure?",
					"Do it!",
					"Cancel")) {
					return;
				}


				StringBuilder replaceReport = new StringBuilder(300);
				replaceReport.AppendLine($"Mass replace started! Reparent: {option == 0}");
				ReplaceAllPrefabResults(_results, option == 0, replaceReport);
				
				Debug.Log($"Replace report:\n" + replaceReport);
			} 
			EditorGUI.EndDisabledGroup();
		}


		private string GetSaveSlothPath(int index)
		{
			return Application.temporaryCachePath + "/" + $"SearchReferencesFast_Slot_{index}";
		}

		private void DrawSaveResultsSlots()
		{
			const int SLOTS_COUNT = 5;
			for (int i = 0; i < SLOTS_COUNT; ++i) {
				if (GUILayout.Button(i.ToString(), GUILayout.ExpandWidth(false))) {



					var option = EditorUtility.DisplayDialogComplex("Save/Load?",
						$"You have selected save slot {i}.\nYou can save to or load from it results.",
						"Save",
						"Load",
						"Cancel");

					switch (option) {
						case 0:
							var serializer = new BinaryFormatter();

							using (FileStream fileStream = File.Open(GetSaveSlothPath(i), FileMode.OpenOrCreate)) {
								serializer.Serialize(fileStream, _results);
							}

							break;

						case 1:
							if (!File.Exists(GetSaveSlothPath(i))) {
								EditorUtility.DisplayDialog("Load failed", $"No saved results were found at slot {i}.", "Ok");
								break;
							}

							serializer = new BinaryFormatter();

							using (FileStream fileStream = File.Open(GetSaveSlothPath(i), FileMode.Open)) {

								try {
									_results = (SearchResult) serializer.Deserialize(fileStream);
								}
								catch (Exception ex) {
									Debug.LogException(ex);
									EditorUtility.DisplayDialog("Error", $"Could not load saved results from slot {i}.\nProbably the data format changed.\nFor details check the logs.", "Ok");
								}
							}
							break;
					}

				}
			}
		}

		private void ReplaceSinglePrefabResult(SearchResultData searchResultData, bool reparentChildren, StringBuilder replaceReport)
		{
			Debug.Assert(searchResultData.Root);

			var scenes = searchResultData
					.Found
					.OfType<SceneAsset>()
					.ToList()
				;

			ReplacePrefabResultsInScenes(scenes, new List<SearchResultData>() { searchResultData }, reparentChildren, replaceReport);
		}

		private void ReplaceAllPrefabResults(SearchResult searchResult, bool reparentChildren, StringBuilder replaceReport)
		{
			var resultDataToReplace = searchResult
				.Data
				.Select(pair => pair.Value)
				.Where(data => data.Root != null)
				.Where(data => data.ReplacePrefab != null)
				.ToList()
				;

			var scenes = resultDataToReplace
				.SelectMany(data => data.Found.OfType<SceneAsset>())
				.Distinct()
				.ToList()
				;

			ReplacePrefabResultsInScenes(scenes, resultDataToReplace, reparentChildren, replaceReport);
		}

		
		// Replace one prefab in many scenes or many prefabs in many scenes.
		private void ReplacePrefabResultsInScenes(List<SceneAsset> scenes, List<SearchResultData> resultDataToReplace, bool reparentChildren, StringBuilder replaceReport)
		{
			for (int i = 0; i < scenes.Count; ++i) {
				var sceneAsset = scenes[i];

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Replacing...", $"{sceneAsset.name}", (float)i / scenes.Count);
				if (cancel) {
					EditorUtility.ClearProgressBar();
					break;
				}

				EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(sceneAsset));

				var scene = EditorSceneManager.GetActiveScene();
				foreach (var sceneRoot in scene.GetRootGameObjects()) {

					foreach (var transform in sceneRoot.GetComponentsInChildren<Transform>(true)) {

						// When replacing, we destroy the old object, so this transform could become null while iterating.
						if (transform == null)
							continue;

						var go = transform.gameObject;

#if UNITY_2018_2_OR_NEWER
						var foundPrefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
#else
						var foundPrefab = PrefabUtility.GetPrefabParent(go);
#endif
						// If the found prefab matches any of the requested (only prefab roots).
						var data = resultDataToReplace.FirstOrDefault(d => d.Root == foundPrefab);
						if (data != null) {
							
							// Store sibling index before reparenting children.
							int nextSiblingIndex = transform.GetSiblingIndex() + 1;
							
							if (reparentChildren) {
								var reparented = new List<GameObject>();
								ReparentForeignObjects(go, transform.parent, transform, reparented);

								if (reparented.Count > 0) {
									replaceReport.AppendLine($"> Re-parented: {string.Join(",", reparented.Select(g => g.name))}");
								}
							}
							
							if (data.ReplacePrefab != null) {
								replaceReport.AppendLine($"Scene: {sceneAsset.name}; Replaced: {GetGameObjectPath(go)};");

								var replaceInstance = (GameObject) PrefabUtility.InstantiatePrefab(data.ReplacePrefab);
								replaceInstance.transform.SetParent(transform.parent);
								replaceInstance.transform.localPosition = transform.localPosition;
								replaceInstance.transform.localRotation = transform.localRotation;
								replaceInstance.transform.localScale = transform.localScale;
								replaceInstance.SetActive(go.activeSelf);
								replaceInstance.transform.SetSiblingIndex(nextSiblingIndex);
							} else {
								replaceReport.AppendLine($"Scene: {sceneAsset.name}; Deleted: {GetGameObjectPath(go)};");
							}
							
							DestroyImmediate(go);
						}
					}
				}

				EditorSceneManager.SaveScene(scene);
			}


			foreach (var data in resultDataToReplace) {
				data.Found.RemoveAll(obj => obj is SceneAsset && scenes.Contains(obj));
			}

			EditorUtility.ClearProgressBar();

			EditorUtility.DisplayDialog("Complete", "Prefabs were replaced. Please check the replace report in the logs.", "I will!");
		}

		

		private string GetGameObjectPath(GameObject go)
		{
			var transform = go.transform;

			var path = new List<string>();

			while (transform) {
				path.Add(transform.name);
				transform = transform.parent;
			}

			path.Reverse();

			return string.Join("/", path);
		}



		private bool ReparentForeignObjects(GameObject root, Transform targetParent, Transform transform, List<GameObject> reparented)
		{
			if (PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject) != root) {
				transform.parent = targetParent;
				reparented.Add(transform.gameObject);
				return true;
			}

			for (int i = 0; i < transform.childCount; ++i) {
				if (ReparentForeignObjects(root, targetParent, transform.GetChild(i), reparented)) {
					--i;
				}
			}

			return false;
		}

		private Vector2 m_PreferencesScroll;
		private void DrawPreferences()
		{
			EditorGUILayout.LabelField("Preferences:", EditorStyles.boldLabel);

			m_PreferencesScroll = EditorGUILayout.BeginScrollView(m_PreferencesScroll, GUILayout.ExpandHeight(false));
			
			var so = new SerializedObject(this);
			var sp = so.FindProperty("_searchFilter").FindPropertyRelative("ExcludePreferences");
			
			EditorGUILayout.PropertyField(sp, new GUIContent("Exclude paths or file names for this project:"), true);
			
			so.ApplyModifiedProperties();
			
			EditorGUILayout.EndScrollView();
			
			if (GUILayout.Button("Done", GUILayout.ExpandWidth(false))) {
				_searchFilter.ExcludePreferences.RemoveAll(string.IsNullOrWhiteSpace);

				File.WriteAllLines(PROJECT_EXCLUDES_PATH, _searchFilter.ExcludePreferences);
				GUI.FocusControl("");
				m_ShowPreferences = false;
			}
		}


		private class SearchEntryData
		{
			public Object Target { get; }
			public bool IsSubAsset { get; }
			public string MainAssetPath { get; }
			public string Guid { get; private set; }
			public string LocalId { get; private set; }

			public SearchEntryData(Object target)
			{
				Target = target;
				MainAssetPath = AssetDatabase.GetAssetPath(target);
				IsSubAsset = AssetDatabase.IsSubAsset(target);

				TryGetGUIDAndLocalFileIdentifier(Target);

				// Prefabs don't have localId (or rather it is not used in the scenes at least).
				// We don't support searching for nested game objects of a prefab.
				if (Target is GameObject) {
					LocalId = null;
				}
			}

			public static bool IsSupported(Object target)
			{
#if UNITY_2018_2_OR_NEWER
				return true;
#else
				return AssetDatabase.IsMainAsset(target) || AssetDatabase.IsForeignAsset(target);
#endif
			}

			private void TryGetGUIDAndLocalFileIdentifier(Object target)
			{
#if UNITY_2018_2_OR_NEWER

				string guid;
				long localId;
				AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out guid, out localId);
				Guid = guid;

				// LocalId is 102900000 for folders, scenes and unknown asset types. Probably marks this as invalid id.
				// For unknown asset types, which actually have some localIds inside them (custom made), this will be invalid when linked in places.
				// Example: Custom generated mesh files that start with "--- !u!43 &4300000" at the top, will actually use 4300000 in the reference when linked somewhere.
				if (localId != 102900000) {
					LocalId = localId.ToString();
				}

#else
				if (!IsSupported(target)) {
					// Local Ids of non-foreign nested assets are actually 64bit numbers, but unity provides only 32bit int?
					throw new InvalidOperationException("Unsupported nested instances.");
				}

				Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target));
				LocalId = AssetDatabase.IsMainAsset(target) ? null : Unsupported.GetLocalIdentifierInFile(target.GetInstanceID()).ToString();
#endif
			}
		}

		// NOTE: This data used to look better, but in order to be serialized by Unity (when changing to play mode), some changes had to be made.
		[Serializable]
		private class SearchResult
		{
			public List<KeyValueResultPair> Data = new List<KeyValueResultPair>();
			public List<string> ResultTypesNames = new List<string>();

			public void Reset()
			{
				Data.Clear();
				ResultTypesNames.Clear();
			}

			public bool TryGetValue(Object key, out SearchResultData data)
			{
				var index = Data.FindIndex(p => p.Key == key);
				if (index == -1) {
					data = null;
					return false;
				} else {
					data = Data[index].Value;
					return true;
				}

			}

			public SearchResultData this[Object key]
			{
				get
				{
					SearchResultData data;
					if (!TryGetValue(key, out data)) {
						throw new InvalidDataException("Key is missing!");
					}
					return data;
				}
			}

			public void Add(Object key, SearchResultData data)
			{
				if (key == null) {
					throw new ArgumentNullException("Null key not allowed!");
				}

				if (Data.Any(p => p.Key == key)) {
					throw new ArgumentException($"Key {key.name} already exists!");
				}

				Data.Add(new KeyValueResultPair() { Key = key, Value = data});
			}

			public void AddType(Type type)
			{
				if (!ResultTypesNames.Contains(type.Name)) {
					ResultTypesNames.Add(type.Name);
				}
			}
		}


		[Serializable]
		private struct KeyValueResultPair : ISerializable
		{
			public Object Key;
			public SearchResultData Value;

			#region Serialization

			private KeyValueResultPair(SerializationInfo info, StreamingContext context)
			{
				Key = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(info.GetString("Key")));
				Value = (SearchResultData) info.GetValue("Value", typeof(SearchResultData));
			}


			public void GetObjectData(SerializationInfo info, StreamingContext context)
			{
				info.AddValue("Key", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Key)));
				info.AddValue("Value", Value);
			}
			#endregion
		}

		[Serializable]
		private class SearchResultData : ISerializable
		{
			public Object Root;
			public List<Object> Found = new List<Object>(10);

			// GUI
			public bool ShowDetails = true;
			public GameObject ReplacePrefab;


			#region Serialization
			public SearchResultData()
			{ }

			private SearchResultData(SerializationInfo info, StreamingContext context)
			{
				Root = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(info.GetString("Root")));
				var foundGuids = (string[]) info.GetValue("Found", typeof(string[]));
				Found = foundGuids
					.Select(AssetDatabase.GUIDToAssetPath)
					.Select(AssetDatabase.LoadAssetAtPath<Object>)
					.ToList();

				ShowDetails = info.GetBoolean("ShowDetails");
				ReplacePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(info.GetString("ReplacePrefab")));
			}


			public void GetObjectData(SerializationInfo info, StreamingContext context)
			{
				info.AddValue("Root", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Root)));
				info.AddValue("Found", Found.Select(AssetDatabase.GetAssetPath).Select(AssetDatabase.AssetPathToGUID).ToArray());

				info.AddValue("ShowDetails", ShowDetails);
				info.AddValue("ReplacePrefab", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ReplacePrefab)));
			}
			#endregion
		}
	}
}
