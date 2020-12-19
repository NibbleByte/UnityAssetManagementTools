using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools.AssetManagement
{

	/// <summary>
	/// Tool for searching duplicate assets.
	/// Compares files by name (for now).
	/// </summary>
	public class SearchDuplicateAssets : EditorWindow
	{
		[MenuItem("Tools/Asset Management/Search Duplicate Assets", false, 60)]
		static void Init()
		{
			var window = GetWindow<SearchDuplicateAssets>("Search Duplicate Assets");
			window._searchFilter.SetTemplateEnabled("Materials", true);
			window._searchFilter.SetTemplateEnabled("Textures", true);
		}

		private SearchAssetsFilter _searchFilter = new SearchAssetsFilter() { ExcludePackages = true };

		private string _resultsFilter = "";
		private List<SearchResultData> _results = new List<SearchResultData>();
		private Vector2 _scrollResultsPos;

		private bool _previewTextures = true;
		private float _texturesZoom = 80.0f;

		void OnEnable()
		{
			_searchFilter.RefreshCounters();
		}

		void OnGUI()
		{
			GUILayout.Label("Search:", EditorStyles.boldLabel);


			_searchFilter.DrawIncludeExcludeFolders();
			_searchFilter.DrawTypeFilters();

			// TODO: Search by file size.

			EditorGUILayout.Space();

			GUILayout.BeginHorizontal();
			{
				_previewTextures = GUILayout.Toggle(_previewTextures, "Preview Textures;", GUILayout.ExpandWidth(false));

				GUILayout.Label("Zoom:", GUILayout.ExpandWidth(false));

				if (GUILayout.Button("-", GUILayout.ExpandWidth(false))) {
					_texturesZoom -= 10.0f;
				}
				if (GUILayout.Button("+", GUILayout.ExpandWidth(false))) {
					_texturesZoom += 10.0f;
				}
			}
			GUILayout.EndHorizontal();

			if (GUILayout.Button("Search")) {
				PerformSearch();
			}

			DrawResults();
		}

		private void PerformSearch()
		{
			_results.Clear();

			var searchedPaths = _searchFilter
				.GetFilteredPaths()
				.Where(p => p.Contains("."))
				.ToArray();

			var duplicatesByName = new Dictionary<string, List<string>>();

			List<string> duplicates;
			for(int i = 0; i < searchedPaths.Length; ++i) {
				var path = searchedPaths[i];
				var filename = Path.GetFileName(path);

				bool cancel = EditorUtility.DisplayCancelableProgressBar("Searching...", $"{path}", (float)i / searchedPaths.Length);
				if (cancel)
					break;

				if (!duplicatesByName.TryGetValue(filename, out duplicates)) {
					duplicates = new List<string>();
					duplicatesByName[filename] = duplicates;
				}

				duplicates.Add(path);
			}

			EditorUtility.ClearProgressBar();


			foreach (var pair in duplicatesByName) {
				if (pair.Value.Count > 1) {

					var result = new SearchResultData
					{
						Duplicates = pair.Value
						.Select(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>)
						.Where(o => o != null)
						.Where(o => !string.IsNullOrEmpty(o.name))
						.ToList()
					};

					// The filter above can filter out more stuff.
					if (result.Duplicates.Count > 1) {
						_results.Add(result);
					}
				}
			}
		}


		private void DrawResults()
		{
			GUILayout.Label("Results:", EditorStyles.boldLabel);

			_resultsFilter = EditorGUILayout.TextField("Filter", _resultsFilter);

			_scrollResultsPos = EditorGUILayout.BeginScrollView(_scrollResultsPos, false, false);
			EditorGUILayout.BeginVertical();

			for (int i = 0; i < _results.Count; ++ i) {
				var result = _results[i];
				var name = result.Duplicates[0].name;

				if (!string.IsNullOrEmpty(_resultsFilter) && name.IndexOf(_resultsFilter, StringComparison.OrdinalIgnoreCase) == -1)
					continue;


				GUILayout.BeginHorizontal();
				{
					if (GUILayout.Button("X", GUILayout.ExpandWidth(false), GUILayout.Height(14))) {
						_results.RemoveAt(i);
						continue;
					}

					GUILayout.Label($"{name}: ", GUILayout.ExpandWidth(false));
				}
				GUILayout.EndHorizontal();


				if (_previewTextures && result.Duplicates[0] is Texture2D) {

					result.ScrollPos = EditorGUILayout.BeginScrollView(result.ScrollPos, false, false, GUILayout.Width(position.width - 15f), GUILayout.Height(_texturesZoom + 20.0f));
					EditorGUILayout.BeginHorizontal();

					foreach (var duplicate in result.Duplicates) {
						EditorGUILayout.ObjectField(duplicate, duplicate.GetType(), false, GUILayout.Width(_texturesZoom), GUILayout.Height(_texturesZoom));
					}

					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndScrollView();

				} else {

					EditorGUI.indentLevel++;

					foreach (var duplicate in result.Duplicates) {
						EditorGUILayout.BeginHorizontal(GUILayout.Width(position.width));

						EditorGUILayout.ObjectField(duplicate, duplicate.GetType(), false, GUILayout.Width(80.0f));
						GUILayout.Label(AssetDatabase.GetAssetPath(duplicate));

						EditorGUILayout.EndHorizontal();

					}

					EditorGUI.indentLevel--;
				}
			}

			EditorGUILayout.EndVertical();
			EditorGUILayout.EndScrollView();
		}


		
		[Serializable]
		private class SearchResultData
		{
			public List<UnityEngine.Object> Duplicates = new List<UnityEngine.Object>(10);
			public Vector2 ScrollPos;
		}
	}
}
