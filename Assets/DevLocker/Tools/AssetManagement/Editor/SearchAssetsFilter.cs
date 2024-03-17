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
		[Serializable]
		private class GUISearchTemplate
		{
			public string Name;
			public string Hint;
			public string Filter;
			public int Count;

			public GUISearchTemplate() {}

			public GUISearchTemplate(string name, string searchFilter)
			{
				Name = name;
				Filter = searchFilter;
			}

			public GUISearchTemplate Clone()
			{
				var clone = new GUISearchTemplate(Name, Filter);
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

		private bool _includeFoldout = false;
		private string _includeFolders = "";

		private bool _excludeFoldout = false;
		private string _excludeFolders = "";

		private static GUIStyle DragDropZoneStyle = null;

		public bool ExcludePackages;
		public List<string> ExcludePreferences = new List<string>();

		private string _searchFilter = "";
		public string SearchFilter => _searchFilter;
		private List<GUISearchTemplate> _searchTemplates = new List<GUISearchTemplate>()
		{
			new GUISearchTemplate("Scenes", "t:Scene"),
			new GUISearchTemplate("Prefabs", "t:Prefab"),
			new GUISearchTemplate("Script Obj", "t:ScriptableObject") { Hint = "Scriptable Objects" },
			new GUISearchTemplate("Models", "t:Model"),
			new GUISearchTemplate("Materials", "t:Material"),
			new GUISearchTemplate("Textures", "t:Texture"),
			new GUISearchTemplate("Animations", "t:Animation"),
			new GUISearchTemplate("Animators", "t:AnimatorController t:AnimatorOverrideController"),
			new GUISearchTemplate("Timelines", "t:TimelineAsset"),
		};

		public SearchAssetsFilter() { }

		#region Serialization

		private SearchAssetsFilter(SerializationInfo info, StreamingContext context)
		{
			_includeFoldout = info.GetBoolean(nameof(_includeFoldout));
			_includeFolders = info.GetString(nameof(_includeFolders));

			_excludeFoldout = info.GetBoolean(nameof(_excludeFoldout));
			_excludeFolders = info.GetString(nameof(_excludeFolders));

			ExcludePackages = info.GetBoolean(nameof(ExcludePackages));
			ExcludePreferences = (List<string>) info.GetValue(nameof(ExcludePreferences), typeof(List<string>));

			_searchFilter = info.GetString(nameof(_searchFilter));
		}


		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue(nameof(_includeFoldout), _includeFoldout);
			info.AddValue(nameof(_includeFolders), _includeFolders);

			info.AddValue(nameof(_excludeFoldout), _excludeFoldout);
			info.AddValue(nameof(_excludeFolders), _excludeFolders);

			info.AddValue(nameof(ExcludePackages), ExcludePackages);
			info.AddValue(nameof(ExcludePreferences), ExcludePreferences);

			info.AddValue(nameof(_searchFilter), _searchFilter);
		}

		#endregion

		public void SetTemplateEnabled(string label, bool enable)
		{
			var template =_searchTemplates.First(t => t.Name == label);
			template.ApplyToSearchFilter(ref _searchFilter, enable);

			RefreshSearchTemplatesOrder();
		}

		public SearchAssetsFilter Clone()
		{
			var clone = new SearchAssetsFilter();

			clone._searchFilter = _searchFilter;
			clone._searchTemplates.Clear();
			clone._searchTemplates.AddRange(_searchTemplates.Select(t => t.Clone()));
			clone.RefreshSearchTemplatesOrder(); // Just in case.

			clone.ExcludePackages = ExcludePackages;
			clone.ExcludePreferences = new List<string>(ExcludePreferences);
			clone._includeFoldout = _includeFoldout;
			clone._includeFolders = _includeFolders;
			clone._excludeFoldout = _excludeFoldout;
			clone._excludeFolders = _excludeFolders;

			return clone;
		}

		public bool Equals(SearchAssetsFilter other)
		{
			if (ReferenceEquals(this, other))
				return true;

			if (ReferenceEquals(other, null))
				return false;

			return _searchFilter.Equals(other._searchFilter)
				&& ExcludePackages.Equals(other.ExcludePackages)
				&& ExcludePreferences.SequenceEqual(other.ExcludePreferences)
				&& _includeFoldout.Equals(other._includeFoldout)
				&& _includeFolders.Equals(other._includeFolders)
				&& _excludeFoldout.Equals(other._excludeFoldout)
				&& _excludeFolders.Equals(other._excludeFolders)
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
			DrawFoldersFilter("Include folders", ref _includeFoldout, ref _includeFolders, GetIncludeFolders());
			DrawFoldersFilter("Exclude folders", ref _excludeFoldout, ref _excludeFolders, GetExcludeFolders());
		}

		public void DrawTypeFilters(float totalWidth)
		{
			_searchFilter = EditorGUILayout.TextField(new GUIContent("Search Filter", "What assets to search for. Same as searching in the project window.\nUse type filters below or type in your own criteria."), _searchFilter);

			const float filterButtonWidth = 120f;
			int columnsCount = Mathf.Max(1, (int) (totalWidth / filterButtonWidth));

			for (int i = 0; i < _searchTemplates.Count; i++) {
				if (i % columnsCount == 0) {
					EditorGUILayout.BeginHorizontal();
				}

				GUISearchTemplate template = _searchTemplates[i];
				bool templateEnabled = template.IsEnabled(_searchFilter);

				Color prevBackgroundColor = GUI.backgroundColor;
				if (templateEnabled) {
					GUI.backgroundColor = Color.green;
				}

				if (GUILayout.Button(new GUIContent($"{template.Name} ({template.Count})", template.Hint), GUILayout.MaxWidth(filterButtonWidth))) {
					template.ApplyToSearchFilter(ref _searchFilter, !templateEnabled);
					RefreshSearchTemplatesOrder();
					GUI.FocusControl("");
				}

				GUI.backgroundColor = prevBackgroundColor;

				if (i % columnsCount == columnsCount - 1) {
					EditorGUILayout.EndHorizontal();
				}
			}

			if ((_searchTemplates.Count - 1) % columnsCount != columnsCount - 1) {
				EditorGUILayout.EndHorizontal();
			}
		}

		public string[] GetIncludeFolders()
		{
			return _includeFolders.Split(";\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
		}

		public string[] GetExcludeFolders()
		{
			return _excludeFolders.Split(";\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
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

		private void RefreshSearchTemplatesOrder()
		{
			var enabledTemplates = new List<GUISearchTemplate>();

			foreach(var template in _searchTemplates) {
				if (template.IsEnabled(_searchFilter)) {
					enabledTemplates.Add(template);
					template.ApplyToSearchFilter(ref _searchFilter, false);
				}
			}

			_searchFilter = _searchFilter.Trim();

			foreach(var template in enabledTemplates) {
				template.ApplyToSearchFilter(ref _searchFilter, true);
			}
		}

		public IEnumerable<string> GetFilteredPaths()
		{
			var includeFolders = GetIncludeFolders();
			var excludeFolders = GetExcludeFolders();

			var query = AssetDatabase.FindAssets(_searchFilter.Trim())
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
			foreach (var template in _searchTemplates) {
				template.Count = AssetDatabase.FindAssets(template.Filter).Length;
			}
		}
	}
}
