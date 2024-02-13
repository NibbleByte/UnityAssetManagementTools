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

		private bool _foldOutSearchCriterias = true;


		[NonSerialized] private GUIStyle FoldoutBoldStyle;
		[NonSerialized] private GUIStyle UrlStyle;

		void OnEnable()
		{
			_searchFilter.RefreshCounters();
		}

		private void InitStyles()
		{
			UrlStyle = new GUIStyle(GUI.skin.label);
			UrlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
			UrlStyle.hover.textColor = UrlStyle.normal.textColor;
			UrlStyle.active.textColor = Color.red;
			UrlStyle.wordWrap = false;

			FoldoutBoldStyle = new GUIStyle(EditorStyles.foldout);
			FoldoutBoldStyle.fontStyle = FontStyle.Bold;
		}

		void OnGUI()
		{
			if (UrlStyle == null) {
				InitStyles();
			}

			_foldOutSearchCriterias = EditorGUILayout.Foldout(_foldOutSearchCriterias, "Search in:", toggleOnLabelClick: true, FoldoutBoldStyle);
			if (_foldOutSearchCriterias) {
				EditorGUILayout.BeginVertical(EditorStyles.helpBox);

				_searchFilter.DrawIncludeExcludeFolders();
				_searchFilter.DrawTypeFilters();

				EditorGUILayout.EndVertical();
			}

			// TODO: Search by file size.

			EditorGUILayout.Space();

			if (GUILayout.Button("Search")) {
				PerformSearch();
			}


			GUILayout.Label("Results:", EditorStyles.boldLabel);

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

				GUILayout.FlexibleSpace();

				if (GUILayout.Button("Collapse All", GUILayout.ExpandWidth(false))) {
					foreach (var result in _results) {
						result.Foldout = false;
					}
				}
				if (GUILayout.Button("Expand All", GUILayout.ExpandWidth(false))) {
					foreach (var result in _results) {
						result.Foldout = true;
					}
				}
			}
			GUILayout.EndHorizontal();

			_resultsFilter = EditorGUILayout.TextField("Filter", _resultsFilter);

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
			for (int i = 0; i < searchedPaths.Length; ++i) {
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

					var result = new SearchResultData {
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
			_scrollResultsPos = EditorGUILayout.BeginScrollView(_scrollResultsPos, false, false);
			EditorGUILayout.BeginVertical();

			for (int i = 0; i < _results.Count; ++i) {
				var result = _results[i];
				var name = result.Duplicates.FirstOrDefault(obj => obj != null)?.name ?? "";

				if (!string.IsNullOrEmpty(_resultsFilter) && name.IndexOf(_resultsFilter, StringComparison.OrdinalIgnoreCase) == -1)
					continue;

				EditorGUILayout.BeginHorizontal();
				result.Foldout = EditorGUILayout.Foldout(result.Foldout, $"{name} ({result.Duplicates.Count})", toggleOnLabelClick: true);
				if (GUILayout.Button(new GUIContent("X", "Remove entry from list."), GUILayout.ExpandWidth(false), GUILayout.Height(14))) {
					_results.RemoveAt(i);
					GUIUtility.ExitGUI();
				}
				EditorGUILayout.EndHorizontal();

				if (!result.Foldout)
					continue;

				if (_previewTextures && result.Duplicates[0] is Texture2D) {

					result.ScrollPos = EditorGUILayout.BeginScrollView(result.ScrollPos, false, false, GUILayout.Width(position.width - 15f), GUILayout.Height(_texturesZoom + 20.0f));
					EditorGUILayout.BeginHorizontal();

					foreach (var duplicate in result.Duplicates) {
						EditorGUILayout.ObjectField(duplicate, duplicate.GetType(), false, GUILayout.Width(_texturesZoom), GUILayout.Height(_texturesZoom));
					}

					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndScrollView();

				} else {

					foreach (var duplicate in result.Duplicates) {
						EditorGUILayout.BeginHorizontal();

						GUILayout.Space(16f);

						// Asset got deleted.
						if (duplicate == null) {
							EditorGUILayout.EndHorizontal();
							continue;
						}

						string path = AssetDatabase.GetAssetPath(duplicate);

						if (GUILayout.Button(path, UrlStyle, GUILayout.MaxWidth(position.width - 50f - 16f - 20f))) {
							EditorGUIUtility.PingObject(duplicate);
						}
						EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);

						Color prevBackgroundColor = GUI.backgroundColor;
						GUI.backgroundColor = Color.red;
						if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(50f))) {
							bool confirm = EditorUtility.DisplayDialog("Delete Asset?", $"Are you sure you want to delete this asset? {path}", "Delete!", "Cancel");
							if (confirm) {
								result.Duplicates.Remove(duplicate);
								AssetDatabase.DeleteAsset(path);
								GUIUtility.ExitGUI();
							}
						}
						GUI.backgroundColor = prevBackgroundColor;

						EditorGUILayout.EndHorizontal();

					}
				}
			}

			EditorGUILayout.EndVertical();
			EditorGUILayout.EndScrollView();
		}



		[Serializable]
		private class SearchResultData
		{
			public List<UnityEngine.Object> Duplicates = new List<UnityEngine.Object>(10);
			public bool Foldout = true;
			public Vector2 ScrollPos;
		}
	}
}
