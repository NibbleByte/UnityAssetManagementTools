using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools.AssetManagement
{

	/// <summary>
	/// Tool for searching components in prefabs.
	/// Found prefabs can be placed in the scene for further inspection.
	/// </summary>
	public class SearchPrefabsComponents : EditorWindow
	{
		[MenuItem("Tools/Asset Management/Search Prefabs Components", false, 60)]
		static void Init()
		{
			GetWindow<SearchPrefabsComponents>("Search Prefabs Components");
		}

		private void OnSelectionChange()
		{
			Repaint();
		}


		private string _searchGONameFilter = "";
		private string _searchComponentNamesPattern = "";

		private Dictionary<GameObject, SearchResultData> _results = new Dictionary<GameObject, SearchResultData>();
		private Vector2 _scrollPos;
		private readonly GUIContent _foundResultGuiContentCache = new GUIContent("P",   "P - Place prefab in scene.\n" +
																						"R - Remove placed prefab.\n" +
																						"S - Scene object. Does nothing.\n\n" +
																						"Hint: once placed, links in search result refer the scene instance.");


		private const string SEARCH_BY_COMPONENT_HELP = "Type in any component name EXACTLY (case-sensitive).White-space is ignored.\n" +
														"Inherited components are shown as well.\n" +
														"Example: 'Collider' will show 'BoxCollider' and 'CapsuleColldier' etc...\n" +
														"\n" +
														"Supports the following search patterns:\n" +
														"! - not expression  (evaluated first)\n" +
														"*&& - and expression (evaluated second)\n" +
														"+|, - or expression (evaluated last)\n" +
														"\n" +
														"Examples: \n" +
														"'BoxCollider + CapsuleColldier, SphereColldier' \n(finds box, capsule or sphere colliders)\n\n" +
														"'BoxCollider * AudioSource' \n(finds box colliders with audio source)\n\n" +
														"'!Collider * AudioSource' \n(finds audio sources with NO colliders)\n\n" +
														"'BoxCollider * AudioSource + SphereCollider * AudioSource' \n(finds box or sphere collider with audio source.)\n\n" +
														"'BoxCollider && AudioSource | SphereCollider & AudioSource' \n(same as above, programmer style.)"
			;

		void OnGUI()
		{
			EditorGUI.BeginDisabledGroup(true);
			if (Selection.objects.Length <= 1) {
				EditorGUILayout.TextField("Selected Object", Selection.activeObject ? Selection.activeObject.name : "null");
			} else {
				EditorGUILayout.TextField("Selected Object", $"{Selection.objects.Length} Objects");
			}
			EditorGUI.EndDisabledGroup();

			_searchGONameFilter = EditorGUILayout.TextField("Object name", _searchGONameFilter);

			EditorGUILayout.BeginHorizontal();
			_searchComponentNamesPattern = EditorGUILayout.TextField("Component name", _searchComponentNamesPattern);

			if (GUILayout.Button("?", GUILayout.Width(20.0f))) {
				EditorUtility.DisplayDialog("Search patterns", SEARCH_BY_COMPONENT_HELP, "Ok");
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();

			if (GUILayout.Button("Search Selected")) {
				if (string.IsNullOrEmpty(_searchGONameFilter.Trim()) && string.IsNullOrEmpty(_searchComponentNamesPattern.Trim()))
					return;

				if (Selection.objects.Length == 0) {
					Debug.LogError("Please select some folders/prefabs to search in.");
					return;
				}

				var searchPattern = GetSearchPattern(_searchComponentNamesPattern);
				if (searchPattern == null)
					return;

				PerformSearch(Selection.objects, _searchGONameFilter, searchPattern);
			}

			if (GUILayout.Button("Search All")) {
				if (string.IsNullOrEmpty(_searchGONameFilter.Trim()) && string.IsNullOrEmpty(_searchComponentNamesPattern.Trim()))
					return;

				var searchPattern = GetSearchPattern(_searchComponentNamesPattern);
				if (searchPattern == null)
					return;

				var assetFolder = new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets") };

				PerformSearch(assetFolder, _searchGONameFilter, searchPattern);
			}


			EditorGUILayout.EndHorizontal();

			DrawResults();
		}

		private KeyValuePair<bool, string>[][] GetSearchPattern(string searchComponentNamesPattern)
		{
			KeyValuePair<bool, string>[][]  searchPattern = null;

			if (searchComponentNamesPattern.IndexOfAny("()[]{}".ToCharArray()) != -1) {
				Debug.LogError("Searching by component name doesn't support parentheses.");
				return null;
			}

			searchPattern = Regex.Replace(searchComponentNamesPattern, @"\s+", "")
				.Split("+|,".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
				.Select(strAnd =>
				{
					return strAnd
						.Split("*&".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
						// KeyValuePair<Should Exist (Inverse-Not), componentName>
						.Select(str => new KeyValuePair<bool, string>(!str.StartsWith("!"), str.TrimStart('!')))
						.ToArray();
				})
				.ToArray();


			var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).ToList();
			foreach (var pattern in searchPattern.SelectMany(v => v)) {
				var foundType = allTypes.FirstOrDefault(t => t.Name == pattern.Value);

				if (foundType == null) {
					EditorUtility.DisplayDialog("Search pattern error", $"Invalid component name '{pattern.Value}'", "Ok");
					return null;
				}
			}

			return searchPattern;
		}

		private void PerformSearch(UnityEngine.Object[] objects, string nameFilter, KeyValuePair<bool, string>[][] searchPattern)
		{
			_results.Clear();

			foreach (var selected in objects) {

				if (selected is GameObject) {
					SearchPrefabsForComponent((GameObject)selected, (GameObject)selected, nameFilter, searchPattern);
					continue;
				}

				var path = AssetDatabase.GetAssetPath(selected);
				var guids = AssetDatabase.FindAssets("t:Prefab", new string[] {path});

				for(int i = 0; i < guids.Length; ++i) {
					var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));

					bool cancel = EditorUtility.DisplayCancelableProgressBar("Searching...", $"{prefab.name}", (float)i / guids.Length);
					if (cancel)
						break;

					SearchPrefabsForComponent(prefab, prefab, nameFilter, searchPattern);
				}

				EditorUtility.ClearProgressBar();
			}
		}


		private void DrawResults()
		{
			GUILayout.Label("Results:", EditorStyles.boldLabel);

			bool placeAll = GUILayout.Button("Place All", GUILayout.ExpandWidth(false));

			EditorGUILayout.BeginVertical();
			_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, false, false);

			foreach (var pair in _results) {
				var data = pair.Value;

				EditorGUILayout.BeginHorizontal();

				EditorGUI.BeginDisabledGroup(data.Found.Count == 1 && data.Found[0] == data.Root);
				data.ShowDetails = EditorGUILayout.Toggle(data.ShowDetails, GUILayout.Width(12f));
				EditorGUI.EndDisabledGroup();

				EditorGUILayout.ObjectField(data.ActiveRoot, typeof(GameObject), false);

				// Instantiate the prefab and show the found children with the instance. So clicking on the entry would lead to the scene instance.
				_foundResultGuiContentCache.text = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(data.Root)) ? "S" : (data.RootInstance ? "R" : "P");
				if (GUILayout.Button(_foundResultGuiContentCache, GUILayout.Width(20f)) || placeAll) {
					if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(data.Root)))
						continue;

					if (data.HasSceneInstance) {
						data.RemoveSceneInstance();
					} else {
						Selection.activeObject = data.InstantiateInScene();
					}

				}

				EditorGUILayout.EndHorizontal();


				// List found results under this root. Use FoundInstance instead of Found if prefab instantiated in the scene.
				if (data.ShowDetails) {
					EditorGUI.indentLevel += 2;

					for (int i = 0; i < data.Found.Count; ++i) {

						// In case it got removed by the user.
						bool hasInstance = data.HasSceneInstance && data.FoundInstances[i];
						var go = hasInstance ? data.FoundInstances[i] : data.Found[i];

						EditorGUILayout.BeginHorizontal();
						EditorGUI.BeginDisabledGroup(!hasInstance);

						EditorGUILayout.ObjectField(go, typeof(GameObject), true);
						GUILayout.Space(24f);

						EditorGUI.EndDisabledGroup();
						EditorGUILayout.EndHorizontal();
					}

					EditorGUI.indentLevel -= 2;
				}
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}


		private void SearchPrefabsForComponent(GameObject root, GameObject go, string nameFilter, KeyValuePair<bool, string>[][] searchPattern)
		{
			bool patternMatched = searchPattern.Length == 0 || searchPattern.Any(andPattern =>
			{
				return andPattern.All(pattern => pattern.Key == go.GetComponent(pattern.Value));
			});


			if (patternMatched && go.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) != -1) {
				SearchResultData data;
				if (!_results.TryGetValue(root, out data)) {
					data = new SearchResultData() { Root = root };
					_results.Add(root, data);
				}

				data.Found.Add(go);
			}


			foreach (Transform child in go.transform) {
				SearchPrefabsForComponent(root, child.gameObject, nameFilter, searchPattern);
			}
		}




		private class SearchResultData
		{
			public GameObject Root;
			public List<GameObject> Found = new List<GameObject>(10);

			// Scene instances (if created)
			public GameObject RootInstance;
			public List<GameObject> FoundInstances = null;

			public GameObject ActiveRoot => RootInstance ? RootInstance : Root;

			public bool HasSceneInstance => RootInstance;

			public GameObject InstantiateInScene()
			{
				RootInstance = (GameObject)PrefabUtility.InstantiatePrefab(Root);
				FoundInstances = new List<GameObject>();
				foreach (var go in Found) {
					var path = GetFindPath(Root.transform, go.transform);
					FoundInstances.Add(RootInstance.transform.Find(path).gameObject);
				}

				return RootInstance;
			}

			public void RemoveSceneInstance()
			{
				GameObject.DestroyImmediate(RootInstance);
				FoundInstances = null;
			}

			// GUI
			public bool ShowDetails = false;

			// Copy-pasted from	DevLocker.Utils.GameObjectUtils.GetFindPath()
			private static string GetFindPath(Transform root, Transform node)
			{
				string findPath = string.Empty;

				while (node != root && node != null) {
					findPath = node.name + (string.IsNullOrEmpty(findPath) ? "" : ("/" + findPath));
					node = node.parent;
				}

				if (root == null)
					findPath = "/" + findPath;

				return findPath;
			}
		}
	}
}
