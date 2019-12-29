using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools.AssetsManagement
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

		private string _includeFolders = "";
		private string _excludeFolders = "";
		public bool ExcludePackages;
		public List<string> ExcludePreferences = new List<string>();

		private List<GUISearchTemplate> _searchTemplates = new List<GUISearchTemplate>()
		{
			new GUISearchTemplate("Scenes", "t:Scene"),
			new GUISearchTemplate("Prefabs", "t:Prefab"),
			new GUISearchTemplate("Models", "t:Model"),
			new GUISearchTemplate("Materials", "t:Material"),
			new GUISearchTemplate("Textures", "t:Texture"),
			new GUISearchTemplate("Animations", "t:Animation"),
			new GUISearchTemplate("Animators", "t:AnimatorController t:AnimatorOverrideController"),
			new GUISearchTemplate("Scriptable Objects", "t:ScriptableObject"),
			new GUISearchTemplate("Physics material", "t:PhysicMaterial"),
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
			clone._includeFolders = _includeFolders;
			clone._excludeFolders = _excludeFolders;

			return clone;
		}

		public void DrawIncludeExcludeFolders()
		{
			EditorGUILayout.BeginHorizontal();

			var draggedFolder = EditorGUILayout.ObjectField("Include folders", null, typeof(DefaultAsset), false, GUILayout.Width(220.0f));
			if (draggedFolder) {
				var folder = AssetDatabase.GetAssetPath(draggedFolder);
				if (string.IsNullOrEmpty(Path.GetExtension(folder))) {
					_includeFolders += folder + ";";
				}
			}

			_includeFolders = EditorGUILayout.TextField(_includeFolders);

			EditorGUILayout.EndHorizontal();



			EditorGUILayout.BeginHorizontal();

			draggedFolder = EditorGUILayout.ObjectField("Exclude folders", null, typeof(DefaultAsset), false, GUILayout.Width(220.0f));
			if (draggedFolder) {
				var folder = AssetDatabase.GetAssetPath(draggedFolder);
				if (string.IsNullOrEmpty(Path.GetExtension(folder))) {
					_excludeFolders += AssetDatabase.GetAssetPath(draggedFolder) + ";";
				}
			}

			_excludeFolders = EditorGUILayout.TextField(_excludeFolders);

			EditorGUILayout.EndHorizontal();
		}

		public void DrawTypeFilters()
		{
			foreach (var template in _searchTemplates) {
				EditorGUILayout.BeginHorizontal();
				template.Enable = EditorGUILayout.Toggle(template.Name, template.Enable, GUILayout.ExpandWidth(false));
				GUILayout.Label(template.Count.ToString(), GUILayout.ExpandWidth(false));
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
			return _includeFolders.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
		}

		public string[] GetExcludeFolders()
		{
			return _excludeFolders.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
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
