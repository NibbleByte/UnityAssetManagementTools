using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools.AssetManagement
{

	/// <summary>
	/// Control for selecting types of assets to search for.
	/// Used in other tools.
	/// </summary>
	[Serializable]
	public class SearchAssetsFilter
	{
		[Serializable]
		private class GUISearchTemplate
		{
			public string Name;
			public string Hint;
			public string SearchFilter;
			public bool Enable;
			public int Count;

			public GUISearchTemplate(string name, string searchFilter)
			{
				Name = name;
				SearchFilter = searchFilter;
				Enable = false;
			}

			public GUISearchTemplate Clone()
			{
				var clone = new GUISearchTemplate(Name, SearchFilter);
				clone.Count = Count;
				clone.Enable = Enable;

				return clone;
			}
		}

		private bool _includeFoldout = false;
		private string _includeFolders = "";

		private bool _excludeFoldout = false;
		private string _excludeFolders = "";

		private static GUIStyle DragDropZoneStyle = null;

		public bool ExcludePackages;
		public List<string> ExcludePreferences = new List<string>();

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

		public void SetTemplateEnabled(string label, bool enable)
		{
			var template =_searchTemplates.First(t => t.Name == label);
			template.Enable = enable;
		}

		public SearchAssetsFilter Clone()
		{
			var clone = new SearchAssetsFilter();

			clone._searchTemplates.Clear();
			clone._searchTemplates.AddRange(_searchTemplates.Select(t => t.Clone()));

			clone.ExcludePackages = ExcludePackages;
			clone.ExcludePreferences = new List<string>(ExcludePreferences);
			clone._includeFoldout = _includeFoldout;
			clone._includeFolders = _includeFolders;
			clone._excludeFoldout = _excludeFoldout;
			clone._excludeFolders = _excludeFolders;

			return clone;
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
			const float filterButtonWidth = 120f;
			int columnsCount = Mathf.Max(1, (int) (totalWidth / filterButtonWidth));

			for (int i = 0; i < _searchTemplates.Count; i++) {
				if (i % columnsCount == 0) {
					EditorGUILayout.BeginHorizontal();
				}

				GUISearchTemplate template = _searchTemplates[i];

				Color prevBackgroundColor = GUI.backgroundColor;
				if (template.Enable) {
					GUI.backgroundColor = Color.green;
				}

				if (GUILayout.Button(new GUIContent($"{template.Name} ({template.Count})", template.Hint), GUILayout.MaxWidth(filterButtonWidth))) {
					template.Enable = !template.Enable;
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

		public string GetTypesFilter()
		{
			string typeFilter = "";
			foreach (var template in _searchTemplates) {
				if (template.Enable) {
					typeFilter += template.SearchFilter + " ";
				}
			}

			return typeFilter;
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

		public IEnumerable<string> GetFilteredPaths()
		{
			var includeFolders = GetIncludeFolders();
			var excludeFolders = GetExcludeFolders();

			var query = AssetDatabase.FindAssets(GetTypesFilter().Trim())
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
				template.Count = AssetDatabase.FindAssets(template.SearchFilter).Length;
			}
		}
	}
}
