using System.IO;
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

namespace DevLocker.Tools.AssetManagement
{
	/// <summary>
	/// Window with a list of all available scenes in the project:
	/// + quick access to scenes
	/// + easily load scenes additively
	/// + pin favourites
	/// + colorize scenes based on file name or path
	///
	/// Find it at "Tools / Asset Management / Scenes In Project" menu.
	///
	/// Initial version of the script: http://wiki.unity3d.com/index.php/SceneViewWindow by Kevin Tarchenski.
	/// Advanced (this) version by Filip Slavov (a.k.a. NibbleByte) - NibbleByte3@gmail.com.
	/// </summary>
	public class ScenesInProject : EditorWindow
	{
		#region Types definitions

		private enum PinnedOptions
		{
			Unpin,
			MoveFirst,
			MoveLast,
			ShowInExplorer,
			ShowInProject,
		}

		private enum UnpinnedOptions
		{
			Pin,
			MoveFirst,
			MoveLast,
			ShowInExplorer,
			ShowInProject,
		}

		private enum SortType
		{
			MostRecent,
			ByFileName,
			ByPath,
			DontSort = 20,
		}

		private enum QuickSortType
		{
			ByPath,
			ByFileName,
			ByFileSize,
			ByDateModified,
		}

		private enum SceneDisplay
		{
			SceneNames,
			SceneNamesWithParents,
			SceneFullPathsOmitFolders
		}

		private enum PreferencesTab
		{
			Personal = 0,
			Project = 1,
			About = 2,
		}

		[Serializable]
		private class SceneEntry
		{
			public SceneEntry() { }
			public SceneEntry(string path)
			{
				Path = path;
				Name = System.IO.Path.GetFileNameWithoutExtension(Path);
				Folder = Path.Substring(0, Path.LastIndexOf('/'));
			}

			public string Path;
			public string Name;
			public string Folder;
			public string DisplayName;
			public bool FirstInGroup = false;

			public ColorizePattern ColorizePattern;

			public SceneEntry Clone()
			{
				var clone = (SceneEntry) MemberwiseClone();
				return clone;
			}

			public override string ToString()
			{
				return Path;
			}
		}

		[Serializable]
		private class ColorizePattern
		{
			[Tooltip("Relative path (contains '/') or name match. Can have multiple patterns separated by ';'.")]
			public string Patterns = string.Empty;
			public Color BackgroundColor = Color.black;
			public Color TextColor = Color.white;
		}

		[Serializable]
		private class PersonalPreferences
		{
			public SortType SortType = SortType.MostRecent;
			public SceneDisplay SceneDisplay = SceneDisplay.SceneNames;
			public int DisplayParentsCount = 1;
			public List<string> DisplayRemoveFolders = new List<string>() { "Assets" };
			public int ListCountLimit = 64;     // Round number... :D
			public int SpaceBetweenGroups = 6;  // Pixels between scenes with different folders.
			public int SpaceBetweenGroupsPinned = 0;  // Same but for pinned.

			public List<string> Exclude = new List<string>();   // Exclude paths OR filenames (per project preference)

			public List<ColorizePattern> ColorizePatterns = new List<ColorizePattern>();

			public float SplitterY = -1;			// Hidden preference.

			public PersonalPreferences Clone()
			{
				var clone = (PersonalPreferences)MemberwiseClone();
				clone.DisplayRemoveFolders = new List<string>(DisplayRemoveFolders);
				clone.Exclude = new List<string>(Exclude);
				clone.ColorizePatterns = new List<ColorizePattern>(ColorizePatterns);

				return clone;
			}
		}

		[Serializable]
		private class ProjectPreferences
		{
			public List<ColorizePattern> ColorizePatterns = new List<ColorizePattern>();
			public List<string> Exclude = new List<string>();   // Exclude paths OR filenames (per project preference)

			public ProjectPreferences Clone()
			{
				var clone = (ProjectPreferences)MemberwiseClone();

				clone.ColorizePatterns = new List<ColorizePattern>(this.ColorizePatterns);
				clone.Exclude = new List<string>(this.Exclude);

				return clone;
			}
		}

		#endregion

		private readonly char[] FilterWordsSeparator = new char[] { ' ', '\t' };

		private GUIStyle SearchLabelStyle;
		private GUIContent SearchLabelContent = new GUIContent("Search:", "Filter out scenes by name.\nPress Enter to open first unpinned result.");

		private GUIStyle SearchFieldStyle;
		private GUIStyle SearchFieldCancelStyle;
		private GUIStyle SearchFieldCancelEmptyStyle;

		private GUIStyle SceneButtonStyle;
		private GUIContent SceneButtonContentCache = new GUIContent();
		private GUIStyle SceneOptionsButtonStyle;
		private GUIContent SceneOptionsButtonContent = new GUIContent("\u2261", "Options...");	// \u2261 \u20AA

		private GUIContent ScenePlayButtonContent = new GUIContent("\u25BA", "Play directly");
		private GUIStyle ScenePlayButtonStyle;

		private GUIStyle SceneLoadedButtonStyle;
		private GUIContent SceneLoadedButtonAddContent = new GUIContent("+", "Load scene additively");
		private GUIContent SceneLoadedButtonActiveContent = new GUIContent("*", "Active scene (cannot unload)");
		private GUIContent SceneLoadedButtonRemoveContent = new GUIContent("-", "Unload scene");

		private float SceneButtonHeight;

		private GUIStyle SplitterStyle;
		private GUIStyle DragHandlerStyle;
		private GUIStyle FoldOutBoldStyle;

		private GUIStyle ToolbarButtonStyle;
		private GUIContent PreferencesButtonContent;
		private GUIContent QuickSortButtonContent;

		public static bool AssetsChanged = false;

		// Used to synchronize instances.
		private static List<ScenesInProject> m_Instances = new List<ScenesInProject>();


		private bool m_Initialized = false;

		// In big projects with 1k number of scenes, don't show everything.
		[NonSerialized]
		private bool m_ShowFullBigList = false;

		private Vector2 m_ScrollPos;
		private Vector2 m_ScrollPosPinned;
		private Rect m_SplitterRect = new Rect(-1, -1, -1, -1);
		private bool m_IsSplitterDragged;

		private string m_Filter = string.Empty;
		private bool m_FocusFilterField = false; // HACK!

		[SerializeField]
		private PersonalPreferences m_PersonalPrefs = new PersonalPreferences();

		[SerializeField]
		private ProjectPreferences m_ProjectPrefs = new ProjectPreferences();

		private List<SceneEntry> m_Scenes = new List<SceneEntry>();
		private List<SceneEntry> m_Pinned = new List<SceneEntry>();     // NOTE: m_Scenes & m_Pinned are not duplicated
		private int m_PinnedGroupsCount = 0;

		[NonSerialized]
		private SceneEntry m_DraggedEntity;

		private bool m_ShowPreferences = false;
		private const string PERSONAL_PREFERENCES_PATH = "Library/ScenesInProject.prefs";
		private const string PROJECT_PREFERENCES_PATH = "ProjectSettings/ScenesInProject.prefs";

		private const string SettingsPathScenes = "Library/ScenesInProject.Scenes.txt";
		private const string SettingsPathPinnedScenes = "Library/ScenesInProject.PinnedScenes.txt";

		[MenuItem("Tools/Asset Management/Scenes In Project", false, 68)]
		private static void Init()
		{
			var window = (ScenesInProject)GetWindow(typeof(ScenesInProject), false, "Scenes In Project");
			if (!window.m_Initialized) {
				window.position = new Rect(window.position.xMin + 100f, window.position.yMin + 100f, 400f, 600f);
				window.minSize = new Vector2(300f, 200f);
			}
		}

		// Hidden Unity function, used to draw lock and other buttons at the top of the window.
		private void ShowButton(Rect rect)
		{
			if (GUI.Button(rect, "+", GUI.skin.label)) {
				ScenesInProject window = CreateInstance<ScenesInProject>();
				window.titleContent = titleContent;
				window.Show();

				window.position = new Rect(window.position.xMin + 100f, window.position.yMin + 100f, 400f, 600f);
				window.minSize = new Vector2(300f, 200f);
			}
		}

		#region LEGACY SUPPORT
		private bool LEGACY_ReadPinnedListFromPrefs()
		{
			bool hasChanged = false;
			var setting = string.Format("SceneView_PinnedScenes_{0}", System.Text.RegularExpressions.Regex.Replace(Application.dataPath, @"[/\\:?]", ""));

			string storage = EditorPrefs.GetString(setting, string.Empty);

			if (!string.IsNullOrEmpty(storage)) {
				var preferencesList = new List<string>(storage.Split(';'));
				foreach (var path in preferencesList) {
					var entry = m_Scenes.Find(e => e.Path == path);

					if (entry != null) {
						m_Scenes.Remove(entry);

						if (m_Pinned.Any(e => e.Path == path)) {
							Debug.LogError($"ScenesInProject: scene {path} was present in m_Scenes and m_Pinned");
							continue;
						}

						m_Pinned.Add(entry);
						hasChanged = true;

					} else if (!m_Pinned.Any(e => e.Path == path)) {
						m_Pinned.Add(new SceneEntry(path));
						hasChanged = true;
					}
				}

				AutoSnapSplitter();
				EditorPrefs.DeleteKey(setting);
			}

			// Do Scenes as well.
			setting = string.Format("SceneView_Scenes_{0}", System.Text.RegularExpressions.Regex.Replace(Application.dataPath, @"[/\\:?]", ""));

			storage = EditorPrefs.GetString(setting, string.Empty);

			if (!string.IsNullOrEmpty(storage)) {
				var preferencesList = new List<string>(storage.Split(';'));
				m_Scenes.RemoveAll(e => preferencesList.Contains(e.Path));
				var toInsert = preferencesList
					.Where(p => m_Pinned.FindIndex(e => e.Path == p) == -1)
					.Select(p => new SceneEntry(p));
				m_Scenes.InsertRange(0, toInsert);
				hasChanged = true;

				EditorPrefs.DeleteKey(setting);
			}

			return hasChanged;
		}
		#endregion

		private void StoreAllScenes()
		{
			File.WriteAllLines(SettingsPathPinnedScenes, m_Pinned.Select(e => e.Path));
			File.WriteAllLines(SettingsPathScenes, m_Scenes.Select(e => e.Path));
		}

		private void StorePrefs()
		{
			File.WriteAllText(PERSONAL_PREFERENCES_PATH, JsonUtility.ToJson(m_PersonalPrefs, true));

			File.WriteAllText(PROJECT_PREFERENCES_PATH, JsonUtility.ToJson(m_ProjectPrefs, true));
		}

		private static bool RemoveRedundant(List<SceneEntry> list, List<string> scenesInDB)
		{
			bool removeSuccessful = false;

			for (int i = list.Count - 1; i >= 0; i--) {
				int sceneIndex = scenesInDB.IndexOf(list[i].Path);
				if (sceneIndex == -1) {
					list.RemoveAt(i);
					removeSuccessful = true;
				}
			}

			return removeSuccessful;
		}

		private static void SortScenes(List<SceneEntry> list, SortType sortType)
		{
			switch(sortType) {
				case SortType.MostRecent:
				case SortType.DontSort:
					break;

				case SortType.ByFileName:
					list.Sort((a, b) => Path.GetFileNameWithoutExtension(a.Path).CompareTo(Path.GetFileNameWithoutExtension(b.Path)));
					break;

				case SortType.ByPath:
					list.Sort((a, b) => a.Path.CompareTo(b.Path));
					break;

				default: throw new NotImplementedException();
			}
		}

		private int RegroupScenes(List<SceneEntry> list)
		{
			int groupsCount = 0;

			// Grouping scenes with little entries looks silly. Don't do grouping.
			if (list.Count < 6) {
				foreach(var sceneEntry in list) {
					sceneEntry.FirstInGroup = false;
				}

				return groupsCount;
			}

			// Consider the following example of grouping.
			// foo1    1
			// foo2    2
			// foo3    3
			//
			// bar1    1
			// bar2    2
			// bar3    3
			//
			// pepo    1
			// gogo    1
			// lili    1
			//
			// zzz1    1
			// zzz2    2
			// zzz3    3
			// zzz4    4
			//
			// Roro8   1


			list.First().FirstInGroup = false;
			int entriesInGroup = 1;
			string prevFolder, currFolder, nextFolder;


			for (int i = 1; i < list.Count - 1; ++i) {
				prevFolder = list[i - 1].Folder;
				currFolder = list[i].Folder;
				nextFolder = list[i + 1].Folder;

				list[i].FirstInGroup = false;

				if (prevFolder == currFolder) {
					entriesInGroup++;
					continue;
				}

				if (currFolder == nextFolder) {
					list[i].FirstInGroup = true;
					groupsCount++;
					entriesInGroup = 1;
					continue;
				}

				if (entriesInGroup > 1) {
					list[i].FirstInGroup = true;
					groupsCount++;
					entriesInGroup = 1;
					continue;
				}
			}

			// Do last element
			prevFolder = list[list.Count - 2].Folder;
			currFolder = list[list.Count - 1].Folder;

			bool lastStandAlone = entriesInGroup > 1 && prevFolder != currFolder;
			list.Last().FirstInGroup = lastStandAlone;	// Always set this to override old values!
			if (lastStandAlone) {
				groupsCount++;
			}

			return groupsCount;
		}

		private void RefreshDisplayNames(List<SceneEntry> list)
		{
			foreach(var sceneEntry in list) {
				switch(m_PersonalPrefs.SceneDisplay) {

					case SceneDisplay.SceneNames:

						sceneEntry.DisplayName = sceneEntry.Name;
						break;

					case SceneDisplay.SceneNamesWithParents:

						int displayNameStartIndex = sceneEntry.Path.LastIndexOf('/');
						for(int i = 0; i < m_PersonalPrefs.DisplayParentsCount; ++i) {
							displayNameStartIndex = sceneEntry.Path.LastIndexOf('/', displayNameStartIndex - 1);
							if (displayNameStartIndex == -1) {
								break;
							}
						}

						sceneEntry.DisplayName = sceneEntry.Path.Substring(displayNameStartIndex + 1);
						sceneEntry.DisplayName = sceneEntry.DisplayName.Remove(sceneEntry.DisplayName.LastIndexOf('.'));

						//sceneEntry.DisplayName = sceneEntry.DisplayName.Insert(sceneEntry.DisplayName.LastIndexOf('/') + 1, " ");

						break;

					case SceneDisplay.SceneFullPathsOmitFolders:

						IEnumerable<string> pathFolders = sceneEntry.Folder.Split('/');
						pathFolders = pathFolders.Where(f => !m_PersonalPrefs.DisplayRemoveFolders.Contains(f, StringComparer.OrdinalIgnoreCase));

						sceneEntry.DisplayName = string.Join("/", pathFolders);
						sceneEntry.DisplayName += "/" + sceneEntry.Name;	// Skip extension.
						break;
				}
			}
		}

		private void RefreshColorizePatterns(List<SceneEntry> list)
		{
			var splitters = new char[] { ';' };

			foreach(var sceneEntry in list) {

				sceneEntry.ColorizePattern = null;

				foreach (var colors in m_ProjectPrefs.ColorizePatterns.Concat(m_PersonalPrefs.ColorizePatterns)) {

					var patterns = colors.Patterns.Split(splitters, StringSplitOptions.RemoveEmptyEntries);

					foreach (var pattern in patterns) {

						bool isPath = pattern.Contains("/");

						// Find best match for this scene entry.
						if (isPath) {
							if (sceneEntry.Path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) {

								var prevPattern = sceneEntry.ColorizePattern?.Patterns ?? string.Empty;

								// Allow only better path match to override previous - only paths, no names.
								// Note: This doesn't work for multiple patterns.
								var betterPath = prevPattern.Contains("/") && prevPattern.Length <= pattern.Length;

								if (sceneEntry.ColorizePattern == null || betterPath) {
									sceneEntry.ColorizePattern = colors;
								}

								break;
							}

						} else {

							// This is always preferred to path.
							if (sceneEntry.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) != -1) {
								sceneEntry.ColorizePattern = colors;

								break;
							}
						}
					}
				}
			}
		}

		private void OnEnable()
		{
			m_Instances.Add(this);
		}

		private void OnDisable()
		{
			m_Instances.Remove(this);

			if (m_Initialized) {
				StoreAllScenes();
			}
		}

		//
		// Load save settings
		//
		private void LoadData()
		{
			if (File.Exists(PERSONAL_PREFERENCES_PATH)) {
				m_PersonalPrefs = JsonUtility.FromJson<PersonalPreferences>(File.ReadAllText(PERSONAL_PREFERENCES_PATH));
			} else {
				m_PersonalPrefs = new PersonalPreferences();
			}

			InitializeSplitter(m_PersonalPrefs.SplitterY);

			if (File.Exists(PROJECT_PREFERENCES_PATH)) {
				m_ProjectPrefs = JsonUtility.FromJson<ProjectPreferences>(File.ReadAllText(PROJECT_PREFERENCES_PATH));
			} else {
				m_ProjectPrefs = new ProjectPreferences();
			}

			if (File.Exists(SettingsPathPinnedScenes))
				m_Pinned = new List<SceneEntry>(File.ReadAllLines(SettingsPathPinnedScenes).Select(line => new SceneEntry(line)));

			if (File.Exists(SettingsPathScenes))
				m_Scenes = new List<SceneEntry>(File.ReadAllLines(SettingsPathScenes).Select(line => new SceneEntry(line)));
		}

		private void InitializeData()
		{
			if (!m_Initialized) {
				LoadData();
			}

			//
			// Cache available scenes
			//
			string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
			var scenesInDB = new List<string>(sceneGuids.Length);
			foreach (string guid in sceneGuids) {
				string scenePath = AssetDatabase.GUIDToAssetPath(guid);

				if (ShouldExclude(m_ProjectPrefs.Exclude.Concat(m_PersonalPrefs.Exclude), scenePath))
					continue;

				scenesInDB.Add(scenePath);
			}

			bool hasChanges = RemoveRedundant(m_Scenes, scenesInDB);
			hasChanges = RemoveRedundant(m_Pinned, scenesInDB) || hasChanges;

			foreach (string s in scenesInDB) {

				if (m_Scenes.Concat(m_Pinned).All(e => e.Path != s)) {
					m_Scenes.Add(new SceneEntry(s));

					hasChanges = true;
				}
			}

			if (!m_Initialized && LEGACY_ReadPinnedListFromPrefs())
				hasChanges = true;


			if (hasChanges || !m_Initialized) {
				SortScenes(m_Scenes, m_PersonalPrefs.SortType);

				RefreshDisplayNames(m_Scenes);
				RefreshDisplayNames(m_Pinned);

				RefreshColorizePatterns(m_Scenes);
				RefreshColorizePatterns(m_Pinned);

				StoreAllScenes();
			}

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);
		}

		private void InitializeStyles()
		{
			titleContent.image = EditorGUIUtility.FindTexture("Favorite");

			SceneButtonStyle = new GUIStyle(GUI.skin.button);
			SceneButtonStyle.alignment = TextAnchor.MiddleLeft;
			SceneButtonStyle.padding.left = 10;

			SceneButtonHeight = EditorGUIUtility.singleLineHeight + SceneButtonStyle.margin.top + SceneButtonStyle.margin.bottom - 1;

			SceneOptionsButtonStyle = new GUIStyle(GUI.skin.button);
			SceneOptionsButtonStyle.alignment = TextAnchor.MiddleCenter;
			SceneOptionsButtonStyle.padding.left += 2;

			ScenePlayButtonStyle = new GUIStyle(GUI.skin.GetStyle("ButtonLeft"));
			ScenePlayButtonStyle.alignment = TextAnchor.MiddleCenter;
			ScenePlayButtonStyle.padding.left += 2;
			ScenePlayButtonStyle.padding.right -= 2;

			SceneLoadedButtonStyle = new GUIStyle(GUI.skin.GetStyle("ButtonRight"));
			SceneLoadedButtonStyle.alignment = TextAnchor.MiddleCenter;
			SceneLoadedButtonStyle.padding.left = SceneLoadedButtonStyle.padding.right = 2;
			SceneLoadedButtonStyle.contentOffset = new Vector2(1f, 0f);

			SplitterStyle = new GUIStyle(GUI.skin.box);
			SplitterStyle.alignment = TextAnchor.MiddleCenter;
			SplitterStyle.clipping = TextClipping.Overflow;
			SplitterStyle.contentOffset = new Vector2(0f, -1f);

			DragHandlerStyle = new GUIStyle(GUI.skin.GetStyle("RL DragHandle"));
			//DRAGHANDLER_STYLE.contentOffset = new Vector2(0f, Mathf.FloorToInt(EditorGUIUtility.singleLineHeight / 2f) + 2);

			FoldOutBoldStyle = new GUIStyle(EditorStyles.foldout);
			FoldOutBoldStyle.fontStyle = FontStyle.Bold;

			SearchLabelStyle = new GUIStyle(EditorStyles.boldLabel);
			SearchLabelStyle.margin.top = 1;

			SearchFieldStyle = GUI.skin.GetStyle("ToolbarSeachTextField");
			SearchFieldCancelStyle = GUI.skin.GetStyle("ToolbarSeachCancelButton");
			SearchFieldCancelEmptyStyle = GUI.skin.GetStyle("ToolbarSeachCancelButtonEmpty");

			ToolbarButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
			ToolbarButtonStyle.padding = new RectOffset();

			PreferencesButtonContent = new GUIContent(EditorGUIUtility.FindTexture("Settings"), "Preferences...");
			QuickSortButtonContent = new GUIContent(EditorGUIUtility.FindTexture("CustomSorting"), "Quick sort scenes");
		}

		private void SynchronizeInstancesToMe()
		{
			foreach(var instance in m_Instances) {
				if (instance == this)
					continue;

				instance.m_Pinned = new List<SceneEntry>(m_Pinned.Select(e => e.Clone()));
				instance.m_Scenes = new List<SceneEntry>(m_Scenes.Select(e => e.Clone()));
				instance.m_PersonalPrefs = m_PersonalPrefs.Clone();
				instance.m_ProjectPrefs = m_ProjectPrefs.Clone();
				instance.m_PinnedGroupsCount = m_PinnedGroupsCount;

				instance.RefreshColorizePatterns(instance.m_Pinned);
				instance.RefreshColorizePatterns(instance.m_Scenes);

				instance.Repaint();
			}
		}

		internal static void RepaintAllInstances()
		{
			foreach(var instance in m_Instances) {
				instance.Repaint();
			}
		}

		private void OnGUI()
		{
			// Initialize on demand (not on OnEnable), to make sure everything is up and running.
			if (!m_Initialized || AssetsChanged) {
				if (!m_Initialized) {
					InitializeStyles();
				}

				InitializeData();

				// This instance will consume the AssetsChanged flag and synchronize the rest.
				if (AssetsChanged) {
					SynchronizeInstancesToMe();
				}

				m_Initialized = true;
				AssetsChanged = false;
			}

			if (m_ShowPreferences) {
				DrawPreferences();
				return;
			}

			bool openFirstResult;
			string[] filterWords;
			DrawControls(out openFirstResult, out filterWords);

			DrawSceneLists(openFirstResult, filterWords);

			HandleScenesDrag();
		}

		private void DrawControls(out bool openFirstResult, out string[] filterWords)
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			//
			// Draw Filter
			//
			GUILayout.Label(SearchLabelContent, SearchLabelStyle, GUILayout.ExpandWidth(false));


			GUI.SetNextControlName("FilterControl");
			m_Filter = EditorGUILayout.TextField(m_Filter, SearchFieldStyle, GUILayout.Height(20));

			// HACK: skip a frame to focus control, to avoid visual bugs. Bad bad Unity!
			if (m_FocusFilterField) {
				GUI.FocusControl("FilterControl");
				m_FocusFilterField = false;
			}

			// Clear on ESC
			if (Event.current.isKey && Event.current.keyCode == KeyCode.Escape && GUI.GetNameOfFocusedControl() == "FilterControl") {
				m_Filter = "";
				GUI.FocusControl("");
				Event.current.Use();
			}

			// Clear on button
			if (GUILayout.Button(" ", string.IsNullOrEmpty(m_Filter) ? SearchFieldCancelEmptyStyle : SearchFieldCancelStyle, GUILayout.Width(20.0f))) {
				m_Filter = "";
				GUI.FocusControl("");
				m_FocusFilterField = true;
				Repaint();
			}

			if (GUILayout.Button(QuickSortButtonContent, ToolbarButtonStyle, GUILayout.Width(25.0f))) {
				ShowQuickSortOptions();
				GUIUtility.ExitGUI();
			}

			if (GUILayout.Button(PreferencesButtonContent, ToolbarButtonStyle, GUILayout.Width(25.0f))) {
				m_ShowPreferences = true;
				Repaint();
				GUIUtility.ExitGUI();
			}

			// Unfocus on enter. Open first scene from results.
			openFirstResult = false;
			if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "FilterControl") {
				GUI.FocusControl("");
				Repaint();
				Event.current.Use();

				if (!string.IsNullOrEmpty(m_Filter)) {
					openFirstResult = true;
				}

			}

			EditorGUILayout.EndHorizontal();

			filterWords = string.IsNullOrEmpty(m_Filter) ? null : m_Filter.Split(FilterWordsSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (openFirstResult) {
				m_Filter = "";
			}
		}

		private void DrawSceneLists(bool openFirstResult, string[] filterWords)
		{
			//
			// Show pinned scenes
			//
			if (m_Pinned.Count > 0) {
				GUILayout.Label("Pinned:", EditorStyles.boldLabel);

				// -3f is for clicks on the top edge of the splitter. It clicked on the button underneath.
				float scrollViewHeight = m_SplitterRect.y - CalcPinnedViewStartY() - 3f;

				//GUI.Box(new Rect(0, CalcPinnedViewStartY(), position.width, scrollViewHeight), "");

				EditorGUILayout.BeginVertical();
				m_ScrollPosPinned = EditorGUILayout.BeginScrollView(m_ScrollPosPinned, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Height(scrollViewHeight));

				for (int i = 0; i < m_Pinned.Count; ++i) {
					var sceneEntry = m_Pinned[i];
					var sceneName = Path.GetFileNameWithoutExtension(sceneEntry.Path);
					if (!IsFilteredOut(sceneName, filterWords)) {

						if (sceneEntry.FirstInGroup && filterWords == null) {
							if (m_PersonalPrefs.SpaceBetweenGroupsPinned > 0) {
								GUILayout.Space(m_PersonalPrefs.SpaceBetweenGroupsPinned);
							}
						}

						DrawSceneButtons(sceneEntry, true, filterWords == null, false);
					}
				}

				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();

				DrawSplitter();
			}

			//
			// Show all scenes
			//
			GUILayout.Label("Scenes:", EditorStyles.boldLabel);

			EditorGUILayout.BeginVertical();
			m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);

			var filteredCount = 0;
			for (var i = 0; i < m_Scenes.Count; i++) {
				var sceneEntry = m_Scenes[i];
				var sceneName = Path.GetFileNameWithoutExtension(sceneEntry.Path);

				// Do filtering
				if (IsFilteredOut(sceneName, filterWords))
					continue;

				filteredCount++;


				if (sceneEntry.FirstInGroup && filterWords == null) {
					if (m_PersonalPrefs.SpaceBetweenGroups > 0) {
						GUILayout.Space(m_PersonalPrefs.SpaceBetweenGroups);
					}
				}

				bool allowDrag = filterWords == null;
				allowDrag &= m_PersonalPrefs.SortType == SortType.MostRecent || m_PersonalPrefs.SortType == SortType.DontSort;

				DrawSceneButtons(sceneEntry, false, allowDrag, openFirstResult);
				openFirstResult = false;

				if (!m_ShowFullBigList && filteredCount >= m_PersonalPrefs.ListCountLimit)
					break;
			}


			// Big lists support
			if (!m_ShowFullBigList && filteredCount >= m_PersonalPrefs.ListCountLimit && GUILayout.Button("... Show All ...", GUILayout.ExpandWidth(true))) {
				m_ShowFullBigList = true;
				GUIUtility.ExitGUI();
			}

			if (m_ShowFullBigList && filteredCount >= m_PersonalPrefs.ListCountLimit && GUILayout.Button("... Hide Some ...", GUILayout.ExpandWidth(true))) {
				m_ShowFullBigList = false;
				GUIUtility.ExitGUI();
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void HandleScenesDrag()
		{
			if (m_DraggedEntity == null)
				return;

			if (Event.current.type == EventType.MouseUp) {
				m_DraggedEntity = null;

				StoreAllScenes();
				SynchronizeInstancesToMe();

				return;
			}

			Repaint();

			if (Event.current.type != EventType.Repaint)
				return;



			if (m_Pinned.Contains(m_DraggedEntity)) {

				var pinnedStartY = CalcPinnedViewStartY() + 3f - m_ScrollPosPinned.y;

				bool changed = HandleScenesListDrag(m_Pinned, pinnedStartY, m_PersonalPrefs.SpaceBetweenGroupsPinned);
				if (changed) {
					m_PinnedGroupsCount = RegroupScenes(m_Pinned);
				}

			} else {

				var scenesStartY = m_Pinned.Count == 0
					? CalcPinnedViewStartY() + 3f
					: m_SplitterRect.y + m_SplitterRect.height + EditorGUIUtility.singleLineHeight + 5f;
				scenesStartY -= m_ScrollPos.y;

				bool changed = HandleScenesListDrag(m_Scenes, scenesStartY, m_PersonalPrefs.SpaceBetweenGroups);
				if (changed) {
					SortScenes(m_Scenes, m_PersonalPrefs.SortType);
					RegroupScenes(m_Scenes);
				}
			}
		}

		private bool HandleScenesListDrag(List<SceneEntry> scenes, float startY, float groupSpace)
		{
			var entryRect = new Rect(0, startY, position.width, EditorGUIUtility.singleLineHeight + 4f);

			int groupsCount = 0;
			for (int i = 0; i < scenes.Count; ++i) {
				var entry = scenes[i];

				if (entry.FirstInGroup) {
					groupsCount++;
				}
				entryRect.y = startY + entryRect.height * i + groupsCount * groupSpace;

				if (entryRect.Contains(Event.current.mousePosition)) {

					if (entry != m_DraggedEntity) {
						bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

						scenes.Remove(m_DraggedEntity);
						scenes.Insert(i, m_DraggedEntity);

						if (shouldAutoSnapSplitter) {
							AutoSnapSplitter();
						}

						return true;
					}

					return false;
				}
			}

			return false;
		}

		private void InitializeSplitter(float startY)
		{
			if (startY < 0f) {
				startY = CalcPinnedViewStartY();
			}

			const float splitterHeight = 5f;
			m_SplitterRect = new Rect(0, startY, position.width, splitterHeight);
		}

		private float CalcPinnedViewStartY()
		{
			// Calculate pinned scroll view layout.
			const float LINE_PADDING = 6;
			float LINE_HEIGHT = EditorGUIUtility.singleLineHeight + LINE_PADDING;

			return LINE_HEIGHT * 2;
		}

		private float CalcSplitterMinY()
		{
			return CalcPinnedViewStartY() + EditorGUIUtility.singleLineHeight + 8f;
		}

		private float CalcSplitterMaxY()
		{
			float BOTTOM_PADDING = SceneButtonHeight * 3 + 8f;
			float minY = CalcSplitterMinY();
			var pinnedGroupsSpace = m_PinnedGroupsCount * m_PersonalPrefs.SpaceBetweenGroupsPinned;

			return Mathf.Min(minY + SceneButtonHeight * (m_Pinned.Count - 1) + pinnedGroupsSpace, position.height - BOTTOM_PADDING);
		}

		private void DrawSplitter()
		{
			m_SplitterRect.width = 150f;
			m_SplitterRect.x = (position.width - m_SplitterRect.width) / 2f;

			GUI.Box(m_SplitterRect, "- - - - - - -", SplitterStyle);
			//GUI.DrawTexture(m_SplitterRect, EditorGUIUtility.whiteTexture);
			EditorGUIUtility.AddCursorRect(m_SplitterRect, MouseCursor.ResizeVertical);

			if (Event.current.type == EventType.MouseDown && m_SplitterRect.Contains(Event.current.mousePosition)) {
				m_IsSplitterDragged = true;
			}

			float minY = CalcSplitterMinY();
			float maxY = CalcSplitterMaxY();

			if (m_IsSplitterDragged) {
				float splitterY = Mathf.Clamp(Event.current.mousePosition.y - Mathf.Round(m_SplitterRect.height / 2f), minY, maxY);
				m_SplitterRect.Set(m_SplitterRect.x, splitterY, m_SplitterRect.width, m_SplitterRect.height);
				Repaint();
			} else {
				// Sync with window size every time.
				m_SplitterRect.y = Mathf.Max(minY, Mathf.Min(m_SplitterRect.y, maxY));
			}

			if (m_IsSplitterDragged && Event.current.type == EventType.MouseUp) {
				m_IsSplitterDragged = false;
				m_PersonalPrefs.SplitterY = m_SplitterRect.y;
				StorePrefs();
			}
		}

		private bool ShouldAutoSnapSplitter()
		{
			float maxY = CalcSplitterMaxY();

			return maxY - m_SplitterRect.y < 6f;
		}

		private void AutoSnapSplitter()
		{
			m_SplitterRect.y = CalcSplitterMaxY();

			m_PersonalPrefs.SplitterY = m_SplitterRect.y;
			StorePrefs();
		}

		private bool IsFilteredOut(string sceneName, string[] filterWords)
		{

			if (filterWords == null)
				return false;

			foreach (var filterWord in filterWords) {
				if (sceneName.IndexOf(filterWord, StringComparison.OrdinalIgnoreCase) == -1) {
					return true;
				}
			}

			return false;
		}

		private void MoveSceneAtTopOfList(SceneEntry sceneEntry)
		{
			int idx = m_Scenes.IndexOf(sceneEntry);
			if (idx >= 0) {
				m_Scenes.RemoveAt(idx);
				m_Scenes.Insert(0, sceneEntry);
			}

			RegroupScenes(m_Scenes);
		}

		private void DrawSceneButtons(SceneEntry sceneEntry, bool isPinned, bool allowDrag, bool forceOpen)
		{
			EditorGUILayout.BeginHorizontal();

			SceneButtonContentCache.text = sceneEntry.DisplayName;
			SceneButtonContentCache.tooltip = sceneEntry.Path;

			var prevBackgroundColor = GUI.backgroundColor;
			var prevColor = SceneButtonStyle.normal.textColor;
			if (sceneEntry.ColorizePattern != null && !string.IsNullOrEmpty(sceneEntry.ColorizePattern.Patterns)) {
				GUI.backgroundColor = sceneEntry.ColorizePattern.BackgroundColor;
				SceneButtonStyle.normal.textColor
					= SceneOptionsButtonStyle.normal.textColor
					= ScenePlayButtonStyle.normal.textColor
					= SceneLoadedButtonStyle.normal.textColor
					= sceneEntry.ColorizePattern.TextColor;
			}

			if (sceneEntry == m_DraggedEntity) {
				GUI.backgroundColor *= new Color(0.9f, 0.9f, 0.9f, 0.6f);
			}

			var scene = SceneManager.GetSceneByPath(sceneEntry.Path);
			bool isSceneLoaded = scene.IsValid();
			bool isActiveScene = isSceneLoaded && scene == SceneManager.GetActiveScene();
			var loadedButton = isSceneLoaded ? (isActiveScene ? SceneLoadedButtonActiveContent : SceneLoadedButtonRemoveContent) : SceneLoadedButtonAddContent;

			bool optionsPressed = GUILayout.Button(SceneOptionsButtonContent, SceneOptionsButtonStyle, GUILayout.Width(22));
			bool scenePressed = GUILayout.Button(SceneButtonContentCache, SceneButtonStyle) || forceOpen;
			var dragRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(5f), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f));
			bool playPressed = GUILayout.Button(ScenePlayButtonContent, ScenePlayButtonStyle, GUILayout.Width(20));
			bool loadPressed = GUILayout.Button(loadedButton, SceneLoadedButtonStyle, GUILayout.Width(21));

			if (allowDrag) {
				float paddingTop = Mathf.Floor(EditorGUIUtility.singleLineHeight / 2);
				dragRect.y += paddingTop;
				GUI.Box(dragRect, string.Empty, DragHandlerStyle);
				dragRect.y -= paddingTop;
				EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeVertical);

				if (Event.current.type == EventType.MouseDown && dragRect.Contains(Event.current.mousePosition)) {
					m_DraggedEntity = sceneEntry;
				}
			}

			GUI.backgroundColor = prevBackgroundColor;
			SceneButtonStyle.normal.textColor
				= SceneOptionsButtonStyle.normal.textColor
				= ScenePlayButtonStyle.normal.textColor
				= SceneLoadedButtonStyle.normal.textColor
				= prevColor;

			if (scenePressed || optionsPressed || playPressed || loadPressed) {
				// If scene was removed outside of Unity, the AssetModificationProcessor would not get notified.
				if (!File.Exists(sceneEntry.Path)) {
					AssetsChanged = true;
					return;
				}
			}

			if (scenePressed) {


				if (Event.current.shift) {
					EditorUtility.RevealInFinder(sceneEntry.Path);

				} else if (Application.isPlaying || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {

					if (Application.isPlaying) {
						// Note: to do this, the scene must me added to the build settings list.
						// Note2: Sometimes there are side effects with the lighting.
						SceneManager.LoadSceneAsync(sceneEntry.Path);
					} else {
						EditorSceneManager.OpenScene(sceneEntry.Path);
					}

					if (m_PersonalPrefs.SortType == SortType.MostRecent) {
						MoveSceneAtTopOfList(sceneEntry);
					}
					//m_Filter = "";	// It's a feature. Sometimes you need to press on multiple scenes in a row.
					GUI.FocusControl("");
				}
			}


			if (optionsPressed) {
				if (isPinned) {
					ShowPinnedOptions(sceneEntry);
				} else {
					ShowUnpinnedOptions(sceneEntry);
				}
			}

			if (playPressed) {
				PlaySceneDirectly(sceneEntry);
			}

			if (loadPressed) {
				if (Application.isPlaying) {
					if (!isSceneLoaded) {
						// Note: to do this, the scene must me added to the build settings list.
						SceneManager.LoadScene(sceneEntry.Path, LoadSceneMode.Additive);
					} else if (!isActiveScene) {
						SceneManager.UnloadSceneAsync(scene);
					}
				} else {
					if (!isSceneLoaded) {
						EditorSceneManager.OpenScene(sceneEntry.Path, OpenSceneMode.Additive);

						//if (m_PersonalPrefs.SortType == SortType.MostRecent) {
						//	MoveSceneAtTopOfList(sceneEntry);
						//}
					} else if (!isActiveScene) {
						EditorSceneManager.CloseScene(scene, true);
					}
				}
			}

			EditorGUILayout.EndHorizontal();
		}

		private void ShowPinnedOptions(SceneEntry sceneEntry)
		{
			var menu = new GenericMenu();
			int index = m_Pinned.IndexOf(sceneEntry);

			foreach (PinnedOptions value in Enum.GetValues(typeof(PinnedOptions))) {
				menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(value.ToString())), false, OnSelectPinnedOption, new KeyValuePair<PinnedOptions, int>(value, index));
			}

			menu.ShowAsContext();
		}

		private void ShowUnpinnedOptions(SceneEntry sceneEntry)
		{
			var menu = new GenericMenu();
			int index = m_Scenes.IndexOf(sceneEntry);

			foreach (UnpinnedOptions value in Enum.GetValues(typeof(UnpinnedOptions))) {
				menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(value.ToString())), false, OnSelectUnpinnedOption, new KeyValuePair<UnpinnedOptions, int>(value, index));
			}

			menu.ShowAsContext();
		}

		private void OnSelectPinnedOption(object data)
		{
			var pair = (KeyValuePair<PinnedOptions, int>)data;
			int index = pair.Value;

			bool shouldAutoSnapSplitter = false;

			switch (pair.Key) {

				case PinnedOptions.Unpin:
					shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

					var sceneEntry = m_Pinned[index];
					m_Pinned.RemoveAt(index);

					int unpinIndex = m_Scenes.FindIndex(s => s.Folder == sceneEntry.Folder);
					if (unpinIndex == -1) {
						m_Scenes.Insert(0, sceneEntry);
					} else {
						m_Scenes.Insert(unpinIndex, sceneEntry);
					}

					break;

				case PinnedOptions.MoveFirst:
					m_Pinned.Insert(0, m_Pinned[index]);
					m_Pinned.RemoveAt(index + 1);
					break;

				case PinnedOptions.MoveLast:
					m_Pinned.Add(m_Pinned[index]);
					m_Pinned.RemoveAt(index);
					break;

				case PinnedOptions.ShowInExplorer:
					EditorUtility.RevealInFinder(m_Pinned[index].Path);
					return;

				case PinnedOptions.ShowInProject:
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(m_Pinned[index].Path));
					return;
			}

			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private void OnSelectUnpinnedOption(object data)
		{
			var pair = (KeyValuePair<UnpinnedOptions, int>)data;
			int index = pair.Value;

			bool shouldAutoSnapSplitter = false;

			switch (pair.Key) {

				case UnpinnedOptions.Pin:
					shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

					var sceneEntry = m_Scenes[index];
					m_Scenes.RemoveAt(index);

					int pinIndex = m_Pinned.FindLastIndex(s => s.Folder == sceneEntry.Folder);
					if (pinIndex == -1) {
						m_Pinned.Add(sceneEntry);
					} else {
						m_Pinned.Insert(pinIndex + 1, sceneEntry);
					}

					break;

				case UnpinnedOptions.MoveFirst:
					m_Scenes.Insert(0, m_Scenes[index]);
					m_Scenes.RemoveAt(index + 1);
					break;

				case UnpinnedOptions.MoveLast:
					m_Scenes.Add(m_Scenes[index]);
					m_Scenes.RemoveAt(index);
					break;

				case UnpinnedOptions.ShowInExplorer:
					EditorUtility.RevealInFinder(m_Scenes[index].Path);
					return;

				case UnpinnedOptions.ShowInProject:
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(m_Scenes[index].Path));
					return;
			}

			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private void PlaySceneDirectly(SceneEntry sceneEntry)
		{
			if (EditorApplication.isPlaying) {
				SceneManager.LoadSceneAsync(sceneEntry.Path);
				return;
			}

			// YES! This exists from Unity 2017!!!! Bless you!!!
			EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(sceneEntry.Path);
			EditorApplication.isPlaying = true;
		}

		private void ShowQuickSortOptions()
		{
			var menu = new GenericMenu();

			Action<string, bool, List<SceneEntry>, QuickSortType> addItem = (menuPath, enabled, scenes, sortType) => {
				if (enabled) {
					menu.AddItem(new GUIContent(menuPath), false, () => OnSelectQuickSortOption(scenes, sortType));
				} else {
#if UNITY_2017
					menu.AddItem(new GUIContent(menuPath), false, null);
#else
					menu.AddDisabledItem(new GUIContent(menuPath), false);
#endif
				}
			};

			addItem("Sort Pinned/By Path", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByPath);
			addItem("Sort Pinned/By Filename", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByFileName);
			addItem("Sort Pinned/By File Size", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByFileSize);
			addItem("Sort Pinned/By Last Modified", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByDateModified);

			var canSort = m_PersonalPrefs.SortType == SortType.MostRecent || m_PersonalPrefs.SortType == SortType.DontSort;
			addItem("Sort Unpinned/By Path", canSort, m_Scenes, QuickSortType.ByPath);
			addItem("Sort Unpinned/By Filename", canSort, m_Scenes, QuickSortType.ByFileName);
			addItem("Sort Unpinned/By File Size", canSort, m_Scenes, QuickSortType.ByFileSize);
			addItem("Sort Unpinned/By Last Modified", canSort, m_Scenes, QuickSortType.ByDateModified);

			if (m_Pinned.Count > 0) {
				menu.AddItem(new GUIContent("Clear All Pinned Scenes"), false, () => {
					if (EditorUtility.DisplayDialog("Clear Pinned Scenes", "Are you sure you want to clear all pinned scenes?", "Yes!", "No")) {
						ClearPinned();
					}
				});
			} else {
#if UNITY_2017
				menu.AddItem(new GUIContent("Clear All Pinned Scenes"), false, null);
#else
				menu.AddDisabledItem(new GUIContent("Clear All Pinned Scenes"), false);
#endif
			}

			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Cancel"), false, () => { });

			menu.ShowAsContext();
		}

		private void OnSelectQuickSortOption(List<SceneEntry> scenes, QuickSortType sortType)
		{
			bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

			switch(sortType) {
				case QuickSortType.ByPath:
					SortScenes(scenes, SortType.ByPath);
					break;

				case QuickSortType.ByFileName:
					SortScenes(scenes, SortType.ByFileName);
					break;

				case QuickSortType.ByFileSize:
					scenes.Sort((a, b) => new FileInfo(b.Path).Length.CompareTo(new FileInfo(a.Path).Length));
					break;

				case QuickSortType.ByDateModified:
					scenes.Sort((a, b) => File.GetLastWriteTime(b.Path).CompareTo(File.GetLastWriteTime(a.Path)));
					break;
			}

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private void ClearPinned()
		{
			while (m_Pinned.Count > 0) {
				var sceneEntry = m_Pinned[0];
				m_Pinned.RemoveAt(0);

				int unpinIndex = m_Scenes.FindIndex(s => s.Folder == sceneEntry.Folder);
				if (unpinIndex == -1) {
					m_Scenes.Insert(0, sceneEntry);
				} else {
					m_Scenes.Insert(unpinIndex, sceneEntry);
				}
			}

			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			AutoSnapSplitter();

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private readonly GUIContent PreferencesColorizePatternsLabelContent = new GUIContent("Colorize Entries", "Set colors of scenes based on a folder or name patterns.");
		private readonly GUIContent PreferencesExcludePatternsLabelContent = new GUIContent("Exclude Scenes", "Relative path (contains '/') or asset name to be ignored.");
		private Vector2 m_PreferencesScroll;

		private PreferencesTab m_SelectedTab = PreferencesTab.Personal;
		private static readonly string[] m_PreferencesTabsNames = Enum.GetNames(typeof(PreferencesTab));

		private void DrawPreferences()
		{
			EditorGUILayout.Space();

			//
			// Save / Close Buttons
			//
			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField("Save changes:", EditorStyles.boldLabel);

				GUILayout.FlexibleSpace();

				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = Color.green / 1.2f;
				if (GUILayout.Button("Save And Close", GUILayout.MaxWidth(150f))) {
					SaveAndClosePreferences();

					GUI.FocusControl("");
					EditorGUIUtility.ExitGUI();
				}
				GUI.backgroundColor = prevColor;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			m_SelectedTab = (PreferencesTab) GUILayout.Toolbar((int) m_SelectedTab, m_PreferencesTabsNames);

			m_PreferencesScroll = EditorGUILayout.BeginScrollView(m_PreferencesScroll);

			switch (m_SelectedTab) {
				case PreferencesTab.Personal:
					DrawPersonalPreferences();
					break;
				case PreferencesTab.Project:
					DrawProjectPreferences();
					break;
				case PreferencesTab.About:
					DrawAboutPreferences();
					break;
			}

			EditorGUILayout.EndScrollView();
		}

		private void SaveAndClosePreferences()
		{
			var splitterChars = new char[] { ';' };

			m_PersonalPrefs.DisplayRemoveFolders.RemoveAll(string.IsNullOrWhiteSpace);
			m_PersonalPrefs.Exclude.RemoveAll(string.IsNullOrWhiteSpace);
			m_PersonalPrefs.ColorizePatterns.RemoveAll(c => c.Patterns.Split(splitterChars, StringSplitOptions.RemoveEmptyEntries).Length == 0);

			m_ProjectPrefs.Exclude.RemoveAll(string.IsNullOrWhiteSpace);
			m_ProjectPrefs.ColorizePatterns.RemoveAll(c => c.Patterns.Split(splitterChars, StringSplitOptions.RemoveEmptyEntries).Length == 0);

			// Sort explicitly, so assets will change on reload.
			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RefreshDisplayNames(m_Scenes);
			RefreshDisplayNames(m_Pinned);

			RefreshColorizePatterns(m_Scenes);
			RefreshColorizePatterns(m_Pinned);

			StorePrefs();

			m_ShowPreferences = false;
			m_SelectedTab = PreferencesTab.Personal;
			AssetsChanged = true;
		}

		private void DrawPersonalPreferences()
		{
			EditorGUILayout.HelpBox("These are personal preferences, stored in the Library folder.\nHint: check the the tooltips.", MessageType.Info);

			m_PersonalPrefs.SortType = (SortType)EditorGUILayout.EnumPopup(new GUIContent("Sort by", "How to automatically sort the list of scenes (not the pinned ones).\nNOTE: Changing this will loose the \"Most Recent\" sort done by now."), m_PersonalPrefs.SortType);
			m_PersonalPrefs.SceneDisplay = (SceneDisplay)EditorGUILayout.EnumPopup(new GUIContent("Display entries", "How scenes should be displayed."), m_PersonalPrefs.SceneDisplay);

			if (m_PersonalPrefs.SceneDisplay == SceneDisplay.SceneNamesWithParents) {
				EditorGUI.indentLevel++;
				m_PersonalPrefs.DisplayParentsCount = EditorGUILayout.IntField(new GUIContent("Parents depth", "How many parent folders to show.\nExample: Assets/Scenes/GUI/Foo.unity\n> 1 displays \"GUI/Foo\"\n> 2 displays \"Scenes/GUI/Foo\"\netc..."), m_PersonalPrefs.DisplayParentsCount);
				m_PersonalPrefs.DisplayParentsCount = Mathf.Clamp(m_PersonalPrefs.DisplayParentsCount, 1, 1024); // More round.
				EditorGUI.indentLevel--;
			}

			if (m_PersonalPrefs.SceneDisplay == SceneDisplay.SceneFullPathsOmitFolders) {
				EditorGUI.indentLevel++;

				var so = new SerializedObject(this);
				var sp = so.FindProperty("m_PersonalPrefs").FindPropertyRelative("DisplayRemoveFolders");

				EditorGUILayout.PropertyField(sp, new GUIContent("Omit folders", "List of folders that will be removed from the displayed path. Example: Remove \"Assets\" folder from the path."), true);

				so.ApplyModifiedProperties();

				EditorGUI.indentLevel--;
			}

			m_PersonalPrefs.ListCountLimit = EditorGUILayout.IntField(new GUIContent("Shown scenes limit", "If the scenes in the list are more than this value, they will be truncated (button \"Show All\" is shown).\nTruncated scenes still participate in the search.\n\nThis is very useful in a project with lots of scenes, where drawing large scrollable lists is expensive."), m_PersonalPrefs.ListCountLimit);
			m_PersonalPrefs.ListCountLimit = Mathf.Clamp(m_PersonalPrefs.ListCountLimit, 0, 1024); // More round.

			const string spaceBetweenGroupsHint = "Space in pixels added before every group of scenes.\nScenes in the same folder are considered as a group.";
			m_PersonalPrefs.SpaceBetweenGroups = EditorGUILayout.IntField(new GUIContent("Padding for groups", spaceBetweenGroupsHint), m_PersonalPrefs.SpaceBetweenGroups);
			m_PersonalPrefs.SpaceBetweenGroups = Mathf.Clamp(m_PersonalPrefs.SpaceBetweenGroups, 0, (int)EditorGUIUtility.singleLineHeight);

			m_PersonalPrefs.SpaceBetweenGroupsPinned = EditorGUILayout.IntField(new GUIContent("Padding for pinned groups", spaceBetweenGroupsHint), m_PersonalPrefs.SpaceBetweenGroupsPinned);
			m_PersonalPrefs.SpaceBetweenGroupsPinned = Mathf.Clamp(m_PersonalPrefs.SpaceBetweenGroupsPinned, 0, 16); // More round.

			// Colorize Patterns
			{
				var so = new SerializedObject(this);
				var sp = so.FindProperty("m_PersonalPrefs");

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("ColorizePatterns"), PreferencesColorizePatternsLabelContent, true);
				EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), PreferencesExcludePatternsLabelContent, true);

				so.ApplyModifiedProperties();


				foreach (var cf in m_PersonalPrefs.ColorizePatterns) {
					if (string.IsNullOrEmpty(cf.Patterns)) {
						cf.BackgroundColor = Color.white;
						cf.TextColor = Color.black;
					}
				}
			}
		}

		private void DrawProjectPreferences()
		{
			EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder.\nFeel free to add them to your version control system.\nCoordinate any changes here with your team.", MessageType.Warning);

			var so = new SerializedObject(this);
			var sp = so.FindProperty("m_ProjectPrefs");

			EditorGUILayout.PropertyField(sp.FindPropertyRelative("ColorizePatterns"), PreferencesColorizePatternsLabelContent, true);
			EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), PreferencesExcludePatternsLabelContent, true);

			so.ApplyModifiedProperties();


			foreach (var cf in m_ProjectPrefs.ColorizePatterns) {
				if (string.IsNullOrEmpty(cf.Patterns)) {
					cf.BackgroundColor = Color.white;
					cf.TextColor = Color.black;
				}
			}
		}

		private void DrawAboutPreferences()
		{
			EditorGUILayout.LabelField("About:", EditorStyles.boldLabel);

			var urlStyle = new GUIStyle(EditorStyles.label);
			urlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
			urlStyle.active.textColor = Color.red;

			const string mail = "NibbleByte3@gmail.com";

			GUILayout.Label("Created by Filip Slavov", GUILayout.ExpandWidth(false));
			if (GUILayout.Button(mail, urlStyle, GUILayout.ExpandWidth(false))) {
				Application.OpenURL("mailto:" + mail);
			}

			if (GUILayout.Button("Help / Documentation", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var assets = AssetDatabase.FindAssets("ScenesInProject-Documentation");
				if (assets.Length == 0) {
					EditorUtility.DisplayDialog("Documentation missing!", "The documentation you requested is missing.", "Ok");
				} else {
					Application.OpenURL(System.Environment.CurrentDirectory + "/"+ AssetDatabase.GUIDToAssetPath(assets[0]));
				}
			}

			if (GUILayout.Button("Plugin at Asset Store", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var githubURL = "https://assetstore.unity.com/packages/tools/utilities/scenes-in-project-169933";
				Application.OpenURL(githubURL);
			}

			if (GUILayout.Button("Source at GitHub", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var githubURL = "https://github.com/NibbleByte/UnityAssetManagementTools";
				Application.OpenURL(githubURL);
			}
		}

		// NOTE: Copy pasted from SearchAssetsFilter.
		private static bool ShouldExclude(IEnumerable<string> excludes, string path)
		{
			foreach(var exclude in excludes) {

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
	}

	//
	// Monitors scene assets for any modifications and signals to the ScenesViewWindow.
	//
	internal class ScenesInProjectChangeProcessor : UnityEditor.AssetModificationProcessor
	{

		// NOTE: This won't be called when duplicating a scene. Unity says so!
		// More info: https://issuetracker.unity3d.com/issues/assetmodificationprocessor-is-not-notified-when-an-asset-is-duplicated
		// The only way to get notified is via AssetPostprocessor, but that gets called for everything (saving scenes including).
		// Check the implementation below.
		private static void OnWillCreateAsset(string path)
		{
			CheckAndNotifyForPath(path, false);
		}

		private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			CheckAndNotifyForPath(path);

			return AssetDeleteResult.DidNotDelete;
		}

		private static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
		{
			CheckAndNotifyForPath(oldPath);

			return AssetMoveResult.DidNotMove;
		}

		private static void CheckAndNotifyForPath(string path, bool checkForFolders = true)
		{
			if (path.EndsWith(".unity.meta") || path.EndsWith(".unity")) {
				ScenesInProject.AssetsChanged = true;
				ScenesInProject.RepaintAllInstances();
			} else if (checkForFolders) {
				// Check if this is folder. Folders can contain scenes.
				// This is not accurate, but fast?
				for (int i = path.Length - 1; i >= 0; --i) {
					char ch = path[i];
					if (ch == '.')  // It's a file (hopefully)
						break;

					if (ch == '/') { // It's a folder
						ScenesInProject.AssetsChanged = true;
						ScenesInProject.RepaintAllInstances();
						break;
					}
				}
			}
		}
	}

	// NOTE: This gets called for every asset change including saving scenes.
	// This might have small performance hit for big projects so don't use it.
	//internal class ScenesInProjectAssetPostprocessor : AssetPostprocessor
	//{
	//	private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
	//	{
	//		if (importedAssets.Any(path => path.EndsWith(".unity"))) {
	//			ScenesInProject.AssetsChanged = true;
	//		}
	//	}
	//}

}
