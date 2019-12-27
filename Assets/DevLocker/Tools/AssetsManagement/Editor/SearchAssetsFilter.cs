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

		private bool _excludePackages;
		private string _includeFolders = "";
		private string _excludeFolders = "";
		private string[] _excludeFilenames = new string[0];

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

		public SearchAssetsFilter(bool excludePackages)
		{
			_excludePackages = excludePackages;
		}

		public void SetTemplateEnabled(string label, bool enable)
		{
			var template =_searchTemplates.First(t => t.Name == label);
			template.Enable = enable;
		}

		public void SetExcludedFilenames(string[] filenames)
		{
			_excludeFilenames = filenames;
		}

		public SearchAssetsFilter Clone()
		{
			var clone = new SearchAssetsFilter(_excludePackages);

			clone._searchTemplates.Clear();
			clone._searchTemplates.AddRange(_searchTemplates.Select(t => t.Clone()));

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

		private bool IsExcludedFilename(string path)
		{
			if (_excludeFilenames.Length == 0)
				return false;
			
			var filename = Path.GetFileName(path);
			return _excludeFilenames.Any(exclude => filename.IndexOf(exclude, StringComparison.OrdinalIgnoreCase) != -1);
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

			if (_excludePackages) {
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
