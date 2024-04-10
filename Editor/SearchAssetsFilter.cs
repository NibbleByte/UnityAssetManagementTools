// MIT License Copyright(c) 2024 Filip Slavov, https://github.com/NibbleByte/UnityAssetManagementTools

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools.AssetManagement
{

	/// <summary>
	/// Control for selecting types of assets to search for.
	/// Used in other tools.
	/// </summary>
	[Serializable]
	public class SearchAssetsFilter : ISerializable
	{
		public enum SearchFilterType
		{
			Scenes,
			Prefabs,
			ScriptObj,
			Models,
			Materials,
			Textures,
			Animations,
			Animators,
			Timelines,
		}

		[Serializable]
		private class GUISearchFilter
		{
			public SearchFilterType FilterType;
			public string Hint;
			public string Filter;
			public int Count;

			public GUISearchFilter() {}

			public GUISearchFilter(SearchFilterType searchFilterType, string searchFilter)
			{
				FilterType = searchFilterType;
				Filter = searchFilter;
			}

			public GUISearchFilter Clone()
			{
				var clone = new GUISearchFilter(FilterType, Filter);
				clone.Hint = Hint;
				clone.Count = Count;

				return clone;
			}

			public bool IsEnabled(string searchFilter)
			{
				int index = 0;

				while (true) {
					index = searchFilter.IndexOf(Filter, index);
					if (index == -1)
						return false;

					// Check for spaces around the filter or it wouldn't count.
					if (index != 0 && searchFilter[index - 1] != ' ') {
						index += Filter.Length - 1;
						continue;
					}

					if (index + Filter.Length < searchFilter.Length && searchFilter[index + Filter.Length] != ' ') {
						index += Filter.Length - 1;
						continue;
					}

					return true;
				}
			}

			public void ApplyToSearchFilter(ref string searchFilter, bool enable)
			{
				if (enable) {
					searchFilter = searchFilter.Trim();

					if (!IsEnabled(searchFilter)) {
						searchFilter = string.IsNullOrEmpty(searchFilter) ? Filter : $"{searchFilter} {Filter}";
					}
				} else {
					searchFilter = searchFilter.Replace(" " + Filter, "").Replace(Filter, "").Trim();
				}
			}
		}

		private bool m_IncludeFoldout = false;
		private string m_IncludeFolders = "";

		private bool m_ExcludeFoldout = false;
		private string m_ExcludeFolders = "";

		private static GUIStyle DragDropZoneStyle = null;

		public bool ExcludePackages;
		public List<string> ExcludePreferences = new List<string>();

		private string m_SearchFilter = "";
		public string SearchFilter => m_SearchFilter;
		public List<SearchFilterType> HideSearchFilters = new List<SearchFilterType>();

		private List<GUISearchFilter> m_SearchFilterTypes = new List<GUISearchFilter>()
		{
			new GUISearchFilter(SearchFilterType.Scenes, "t:Scene"),
			new GUISearchFilter(SearchFilterType.Prefabs, "t:Prefab"),
			new GUISearchFilter(SearchFilterType.ScriptObj, "t:ScriptableObject") { Hint = "Scriptable Objects" },
			new GUISearchFilter(SearchFilterType.Models, "t:Model"),
			new GUISearchFilter(SearchFilterType.Materials, "t:Material"),
			new GUISearchFilter(SearchFilterType.Textures, "t:Texture"),
			new GUISearchFilter(SearchFilterType.Animations, "t:Animation"),
			new GUISearchFilter(SearchFilterType.Animators, "t:AnimatorController t:AnimatorOverrideController"),
			new GUISearchFilter(SearchFilterType.Timelines, "t:TimelineAsset"),
		};

		public SearchAssetsFilter() { }

		#region Serialization

		private SearchAssetsFilter(SerializationInfo info, StreamingContext context)
		{
			m_IncludeFoldout = info.GetBoolean(nameof(m_IncludeFoldout));
			m_IncludeFolders = info.GetString(nameof(m_IncludeFolders));

			m_ExcludeFoldout = info.GetBoolean(nameof(m_ExcludeFoldout));
			m_ExcludeFolders = info.GetString(nameof(m_ExcludeFolders));

			ExcludePackages = info.GetBoolean(nameof(ExcludePackages));
			ExcludePreferences = (List<string>) info.GetValue(nameof(ExcludePreferences), typeof(List<string>));

			m_SearchFilter = info.GetString(nameof(m_SearchFilter));
		}


		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue(nameof(m_IncludeFoldout), m_IncludeFoldout);
			info.AddValue(nameof(m_IncludeFolders), m_IncludeFolders);

			info.AddValue(nameof(m_ExcludeFoldout), m_ExcludeFoldout);
			info.AddValue(nameof(m_ExcludeFolders), m_ExcludeFolders);

			info.AddValue(nameof(ExcludePackages), ExcludePackages);
			info.AddValue(nameof(ExcludePreferences), ExcludePreferences);

			info.AddValue(nameof(m_SearchFilter), m_SearchFilter);
		}

		#endregion

		public void SetSearchFilterType(SearchFilterType searchFilterType, bool enable)
		{
			var filterType = m_SearchFilterTypes.First(t => t.FilterType == searchFilterType);
			filterType.ApplyToSearchFilter(ref m_SearchFilter, enable);

			RefreshSearchFilterTypesOrder();
		}

		public void ClearSearchFilterTypes()
		{
			foreach(var filterType in m_SearchFilterTypes) {
				filterType.ApplyToSearchFilter(ref m_SearchFilter, false);
			}

			RefreshSearchFilterTypesOrder();
		}

		public SearchAssetsFilter Clone()
		{
			var clone = new SearchAssetsFilter();

			clone.m_SearchFilter = m_SearchFilter;
			clone.m_SearchFilterTypes.Clear();
			clone.m_SearchFilterTypes.AddRange(m_SearchFilterTypes.Select(t => t.Clone()));
			clone.RefreshSearchFilterTypesOrder(); // Just in case.

			clone.ExcludePackages = ExcludePackages;
			clone.ExcludePreferences = new List<string>(ExcludePreferences);
			clone.m_IncludeFoldout = m_IncludeFoldout;
			clone.m_IncludeFolders = m_IncludeFolders;
			clone.m_ExcludeFoldout = m_ExcludeFoldout;
			clone.m_ExcludeFolders = m_ExcludeFolders;

			return clone;
		}

		public bool Equals(SearchAssetsFilter other)
		{
			if (ReferenceEquals(this, other))
				return true;

			if (ReferenceEquals(other, null))
				return false;

			return m_SearchFilter.Equals(other.m_SearchFilter)
				&& ExcludePackages.Equals(other.ExcludePackages)
				&& ExcludePreferences.SequenceEqual(other.ExcludePreferences)
				&& m_IncludeFoldout.Equals(other.m_IncludeFoldout)
				&& m_IncludeFolders.Equals(other.m_IncludeFolders)
				&& m_ExcludeFoldout.Equals(other.m_ExcludeFoldout)
				&& m_ExcludeFolders.Equals(other.m_ExcludeFolders)
				;
		}

		private static void DrawFoldersFilter(string label, ref bool foldout, ref string foldersFilter, string[] folderPaths)
		{
			if (DragDropZoneStyle == null) {
				DragDropZoneStyle = new GUIStyle(EditorStyles.helpBox);
				DragDropZoneStyle.alignment = TextAnchor.MiddleCenter;
				DragDropZoneStyle.wordWrap = false;
			}

			foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, $"{label} ({folderPaths.Length})");

			var dragRect = GUILayoutUtility.GetLastRect();
			dragRect.x += dragRect.width;
			//dragRect.width = dragRect.width > 120f * 2 ? 120f : dragRect.width * 0.35f;
			dragRect.width = Mathf.Min(120f, dragRect.width * 0.4f);
			dragRect.x -= dragRect.width;
			dragRect.height = EditorGUIUtility.singleLineHeight;
			GUI.Label(dragRect, "Drag Folders Here", DragDropZoneStyle);

			if (dragRect.Contains(Event.current.mousePosition)) {

				if (Event.current.type == EventType.DragUpdated) {
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

					Event.current.Use();

				} else if (Event.current.type == EventType.DragPerform) {

					var draggedFolders = DragAndDrop.objectReferences
						.Select(AssetDatabase.GetAssetPath)
						.Where(p => !string.IsNullOrEmpty(p))
						.Where(p => !folderPaths.Contains(p));
					;

					foldersFilter = foldersFilter.TrimEnd() + "\n" + string.Join("\n", draggedFolders);
					foldersFilter = foldersFilter.Trim();

					Event.current.Use();
				}
			}

			if (foldout) {
				EditorGUI.indentLevel++;
				foldersFilter = EditorGUILayout.TextArea(foldersFilter);
				EditorGUI.indentLevel--;
			}
			EditorGUILayout.EndFoldoutHeaderGroup();
		}

		public void DrawIncludeExcludeFolders()
		{
			DrawFoldersFilter("Include folders", ref m_IncludeFoldout, ref m_IncludeFolders, GetIncludeFolders());
			DrawFoldersFilter("Exclude folders", ref m_ExcludeFoldout, ref m_ExcludeFolders, GetExcludeFolders());
		}

		public void DrawFiltersField()
		{
			m_SearchFilter = EditorGUILayout.TextField(new GUIContent("Search Filter", "What assets to search for. Same as searching in the project window.\nUse type filters below or type in your own criteria."), m_SearchFilter);
		}

		public void DrawTypeFilters(float totalWidth)
		{
			const float filterButtonWidth = 120f;
			int columnsCount = Mathf.Max(1, (int) (totalWidth / filterButtonWidth));
			int skipped = 0;

			for (int i = 0; i < m_SearchFilterTypes.Count; i++) {
				GUISearchFilter searchFilterType = m_SearchFilterTypes[i];
				if (HideSearchFilters.Contains(searchFilterType.FilterType)) {
					skipped++;
					continue;
				}

				int displayIndex = i - skipped;

				if (displayIndex % columnsCount == 0) {
					EditorGUILayout.BeginHorizontal();
				}

				bool searchFilterTypeEnabled = searchFilterType.IsEnabled(m_SearchFilter);

				Color prevBackgroundColor = GUI.backgroundColor;
				if (searchFilterTypeEnabled) {
					GUI.backgroundColor = Color.green;
				}

				if (GUILayout.Button(new GUIContent($"{ObjectNames.NicifyVariableName(searchFilterType.FilterType.ToString())} ({searchFilterType.Count})", searchFilterType.Hint), GUILayout.MaxWidth(filterButtonWidth))) {
					searchFilterType.ApplyToSearchFilter(ref m_SearchFilter, !searchFilterTypeEnabled);
					RefreshSearchFilterTypesOrder();
					GUI.FocusControl("");
				}

				GUI.backgroundColor = prevBackgroundColor;

				if (displayIndex % columnsCount == columnsCount - 1) {
					EditorGUILayout.EndHorizontal();
				}
			}

			if ((m_SearchFilterTypes.Count - skipped - 1) % columnsCount != columnsCount - 1) {
				EditorGUILayout.EndHorizontal();
			}
		}

		public string[] GetIncludeFolders()
		{
			return m_IncludeFolders.Split(";\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
		}

		public string[] GetExcludeFolders()
		{
			return m_ExcludeFolders.Split(";\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
		}

		// NOTE: Copy pasted from ScenesInProject.
		private static bool ShouldExclude(IEnumerable<string> excludes, string path)
		{
			foreach (var exclude in excludes) {

				bool isExcludePath = exclude.Contains('/');    // Check if this is a path or just a filename

				if (isExcludePath) {
					if (path.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
						return true;

				} else {

					var filename = Path.GetFileName(path);
					if (filename.IndexOf(exclude, StringComparison.OrdinalIgnoreCase) != -1)
						return true;
				}
			}

			return false;
		}

		private bool IsExcludedFilename(string path)
		{
			if (ExcludePreferences.Count == 0)
				return false;

			return ShouldExclude(ExcludePreferences, path);
		}

		private void RefreshSearchFilterTypesOrder()
		{
			var enabledSearchFilterTypes = new List<GUISearchFilter>();

			foreach(var searchFilterType in m_SearchFilterTypes) {
				if (searchFilterType.IsEnabled(m_SearchFilter)) {
					enabledSearchFilterTypes.Add(searchFilterType);
					searchFilterType.ApplyToSearchFilter(ref m_SearchFilter, false);
				}
			}

			m_SearchFilter = m_SearchFilter.Trim();

			foreach(var searchFilterType in enabledSearchFilterTypes) {
				searchFilterType.ApplyToSearchFilter(ref m_SearchFilter, true);
			}
		}

		public IEnumerable<string> GetFilteredPaths()
		{
			var includeFolders = GetIncludeFolders();
			var excludeFolders = GetExcludeFolders();

			var query = AssetDatabase.FindAssets(m_SearchFilter.Trim())
				.Select(AssetDatabase.GUIDToAssetPath)
				.Distinct() // Some assets might have sub-assets resulting in having the same path.
				.Where(path => includeFolders.Length == 0 || includeFolders.Any(includePath => path.StartsWith(includePath.Trim())))
				.Where(path => excludeFolders.Length == 0 || !excludeFolders.Any(excludePath => path.StartsWith(excludePath.Trim())))
				.Where(path => !IsExcludedFilename(path))
				;

			if (ExcludePackages) {
				query = query.Where(path => !path.StartsWith("Packages"));
			}

			return query;
		}



		public void RefreshCounters()
		{
			foreach (var searchFilterType in m_SearchFilterTypes) {
				searchFilterType.Count = AssetDatabase.FindAssets(searchFilterType.Filter).Length;
			}
		}
	}
}
