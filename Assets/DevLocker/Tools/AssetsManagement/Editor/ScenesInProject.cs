using System.IO;
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

namespace DevLocker.Tools.AssetsManagement
{
	/// <summary>
	/// Window with a list of all available scenes in the project:
	/// + quick access to scenes
	/// + easily load scenes additively
	/// + pin favourites
	///
	/// Initial version of the script: http://wiki.unity3d.com/index.php/SceneViewWindow by Kevin Tarchenski.
	/// Advanced (this) version by Filip Slavov (a.k.a. NibbleByte) - NibbleByte3@gmail.com.
	/// </summary>
	public class ScenesInProject : EditorWindow
	{
		[MenuItem("Tools/Assets Management/Scenes In Project")]
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

		#region Types definitions

		private enum PinnedOptions
		{
			Unpin,
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
		}

		private enum SceneDisplay
		{
			SceneNames,
			SceneNamesWithParents,
			SceneFullPathsOmitFolders
		}

		[Serializable]
		private class SceneEntry
		{
			public SceneEntry() { }
			public SceneEntry(string path)
			{
				Path = path;
				Name = System.IO.Path.GetFileNameWithoutExtension(Path);
				Folder = Path.Substring(0, Path.LastIndexOf('/') - 1);
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

			public List<ColorizePattern> ColorizePatterns = new List<ColorizePattern>();

			public float SplitterY = -1;			// Hidden preference.

			public PersonalPreferences Clone()
			{
				var clone = (PersonalPreferences)MemberwiseClone();
				clone.DisplayRemoveFolders = new List<string>(DisplayRemoveFolders);
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

		private readonly char[] FILTER_WORD_SEPARATORS = new char[] { ' ', '\t' };
		private GUIStyle SCENE_BUTTON;
		private GUIStyle SCENE_OPTIONS_BUTTON;
		private GUIStyle SCENE_LOADED_BUTTON;
		private float SCENE_BUTTON_HEIGHT;

		private GUIStyle SPLITTER_STYLE;
		private GUIStyle DRAGHANDLER_STYLE;
		private GUIStyle FOLD_OUT_BOLD;
		private GUIContent m_PreferencesButtonTextCache = new GUIContent("P", "Preferences...");
		private GUIContent m_SceneButtonTextCache = new GUIContent();
		private GUIContent m_AddSceneButtonTextCache = new GUIContent("+", "Load scene additively");
		private GUIContent m_ActiveSceneButtonTextCache = new GUIContent("*", "Active scene (cannot unload)");
		private GUIContent m_RemoveSceneButtonTextCache = new GUIContent("-", "Unload scene");

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
		private const string PERSONAL_PREFERENCES_KEY = "ScenesInProject";
		private const string PROJECT_PREFERENCES_PATH = "ProjectSettings/ScenesInProject.prefs";

		private const string SettingsPathScenes = "Library/ScenesInProject.Scenes.txt";
		private const string SettingsPathPinnedScenes = "Library/ScenesInProject.PinnedScenes.txt";

		private void StoreAllScenes()
		{
			File.WriteAllLines(SettingsPathPinnedScenes, m_Pinned.Select(e => e.Path));
			File.WriteAllLines(SettingsPathScenes, m_Scenes.Select(e => e.Path));
		}

		private void StorePrefs()
		{
			EditorPrefs.SetString(PERSONAL_PREFERENCES_KEY, JsonUtility.ToJson(m_PersonalPrefs));

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

		private void SortScenes(List<SceneEntry> list)
		{
			switch(m_PersonalPrefs.SortType) {
				case SortType.MostRecent:
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

		private void OnDisable()
		{
			if (m_Initialized) {
				StoreAllScenes();
				m_Instances.Remove(this);
			}
		}

		//
		// Load save settings
		//
		private void LoadData()
		{
			var personalPrefsData = EditorPrefs.GetString(PERSONAL_PREFERENCES_KEY, string.Empty);
			if (!string.IsNullOrEmpty(personalPrefsData)) {
				m_PersonalPrefs = JsonUtility.FromJson<PersonalPreferences>(personalPrefsData);
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

				if (ShouldExclude(m_ProjectPrefs.Exclude, scenePath))
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


			if (hasChanges || !m_Initialized) {
				SortScenes(m_Scenes);

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
			SCENE_BUTTON = new GUIStyle(GUI.skin.button);
			SCENE_BUTTON.alignment = TextAnchor.MiddleLeft;
			SCENE_BUTTON.padding.left = 10;

			SCENE_BUTTON_HEIGHT = EditorGUIUtility.singleLineHeight + SCENE_BUTTON.margin.top + SCENE_BUTTON.margin.bottom - 1;

			SCENE_OPTIONS_BUTTON = new GUIStyle(GUI.skin.button);
			SCENE_OPTIONS_BUTTON.alignment = TextAnchor.MiddleCenter;

			SCENE_LOADED_BUTTON = new GUIStyle(GUI.skin.button);
			SCENE_LOADED_BUTTON.alignment = TextAnchor.MiddleCenter;
			SCENE_LOADED_BUTTON.padding.left = SCENE_LOADED_BUTTON.padding.right = 2;
			SCENE_LOADED_BUTTON.contentOffset = new Vector2(1f, 0f);

			SPLITTER_STYLE = new GUIStyle(GUI.skin.box);
			SPLITTER_STYLE.alignment = TextAnchor.MiddleCenter;
			SPLITTER_STYLE.clipping = TextClipping.Overflow;
			SPLITTER_STYLE.contentOffset = new Vector2(0f, -1f);

			DRAGHANDLER_STYLE = new GUIStyle(GUI.skin.GetStyle("RL DragHandle"));
			//DRAGHANDLER_STYLE.contentOffset = new Vector2(0f, Mathf.FloorToInt(EditorGUIUtility.singleLineHeight / 2f) + 2);

			FOLD_OUT_BOLD = new GUIStyle(EditorStyles.foldout);
			FOLD_OUT_BOLD.fontStyle = FontStyle.Bold;
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

		private void OnGUI()
		{
			// Initialize on demand (not on OnEnable), to make sure everything is up and running.
			if (!m_Initialized || AssetsChanged) {
				if (!m_Initialized) {
					m_Instances.Add(this);
				}

				InitializeData();
				InitializeStyles();

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

			HandleScenesDrag();

			bool openFirstResult;
			string[] filterWords;
			DrawControls(out openFirstResult, out filterWords);

			DrawSceneLists(openFirstResult, filterWords);
		}

		private void DrawControls(out bool openFirstResult, out string[] filterWords)
		{
			EditorGUILayout.BeginHorizontal();

			//
			// Draw Filter
			//
			GUILayout.Label("Search:", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));


			GUI.SetNextControlName("FilterControl");
			m_Filter = EditorGUILayout.TextField(m_Filter, GUILayout.Height(20));

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
			if (GUILayout.Button("X", GUILayout.Width(20.0f))) {
				m_Filter = "";
				GUI.FocusControl("");
				m_FocusFilterField = true;
				Repaint();
			}

			if (GUILayout.Button(m_PreferencesButtonTextCache, GUILayout.Width(20.0f))) {
				m_ShowPreferences = true;
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

			filterWords = string.IsNullOrEmpty(m_Filter) ? null : m_Filter.Split(FILTER_WORD_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
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

				DrawSceneButtons(sceneEntry, false, filterWords == null && m_PersonalPrefs.SortType == SortType.MostRecent, openFirstResult);
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

					GUIUtility.ExitGUI();
				}

			} else {

				var scenesStartY = m_Pinned.Count == 0
					? CalcPinnedViewStartY() + 3f
					: m_SplitterRect.y + m_SplitterRect.height + EditorGUIUtility.singleLineHeight + 5f;
				scenesStartY -= m_ScrollPos.y;

				bool changed = HandleScenesListDrag(m_Scenes, scenesStartY, m_PersonalPrefs.SpaceBetweenGroups);
				if (changed) {
					SortScenes(m_Scenes);
					RegroupScenes(m_Scenes);

					GUIUtility.ExitGUI();
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
			float BOTTOM_PADDING = SCENE_BUTTON_HEIGHT * 3 + 8f;
			float minY = CalcSplitterMinY();
			var pinnedGroupsSpace = m_PinnedGroupsCount * m_PersonalPrefs.SpaceBetweenGroupsPinned;

			return Mathf.Min(minY + SCENE_BUTTON_HEIGHT * (m_Pinned.Count - 1) + pinnedGroupsSpace, position.height - BOTTOM_PADDING);
		}

		private void DrawSplitter()
		{
			m_SplitterRect.width = 150f;
			m_SplitterRect.x = (position.width - m_SplitterRect.width) / 2f;

			GUI.Box(m_SplitterRect, "- - - - - - -", SPLITTER_STYLE);
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

			m_SceneButtonTextCache.text = sceneEntry.DisplayName;
			m_SceneButtonTextCache.tooltip = sceneEntry.Path;

			var prevBackgroundColor = GUI.backgroundColor;
			var prevColor = SCENE_BUTTON.normal.textColor;
			if (sceneEntry.ColorizePattern != null && !string.IsNullOrEmpty(sceneEntry.ColorizePattern.Patterns)) {
				GUI.backgroundColor = sceneEntry.ColorizePattern.BackgroundColor;
				SCENE_BUTTON.normal.textColor
					= SCENE_OPTIONS_BUTTON.normal.textColor
					= SCENE_LOADED_BUTTON.normal.textColor
					= sceneEntry.ColorizePattern.TextColor;
			}

			if (sceneEntry == m_DraggedEntity) {
				GUI.backgroundColor *= new Color(0.9f, 0.9f, 0.9f, 0.6f);
			}

			var scene = SceneManager.GetSceneByPath(sceneEntry.Path);
			bool isSceneLoaded = scene.IsValid();
			bool isActiveScene = isSceneLoaded && scene == SceneManager.GetActiveScene();
			var loadedButton = isSceneLoaded ? (isActiveScene ? m_ActiveSceneButtonTextCache : m_RemoveSceneButtonTextCache) : m_AddSceneButtonTextCache;

			bool optionsPressed = GUILayout.Button(isPinned ? "O" : "@", SCENE_OPTIONS_BUTTON, GUILayout.Width(22));
			bool scenePressed = GUILayout.Button(m_SceneButtonTextCache, SCENE_BUTTON) || forceOpen;
			var dragRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(5f), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f));
			bool loadPressed = GUILayout.Button(loadedButton, SCENE_LOADED_BUTTON, GUILayout.Width(20));

			if (allowDrag) {
				float paddingTop = Mathf.Floor(EditorGUIUtility.singleLineHeight / 2);
				dragRect.y += paddingTop;
				GUI.Box(dragRect, string.Empty, DRAGHANDLER_STYLE);
				dragRect.y -= paddingTop;
				EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeVertical);

				if (Event.current.type == EventType.MouseDown && dragRect.Contains(Event.current.mousePosition)) {
					m_DraggedEntity = sceneEntry;
				}
			}

			GUI.backgroundColor = prevBackgroundColor;
			SCENE_BUTTON.normal.textColor
				= SCENE_OPTIONS_BUTTON.normal.textColor
				= SCENE_LOADED_BUTTON.normal.textColor
				= prevColor;

			if (scenePressed || optionsPressed || loadPressed) {
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
					PinScene(sceneEntry);
				}
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


		private void PinScene(SceneEntry sceneEntry)
		{
			bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();
			m_Scenes.Remove(sceneEntry);

			int pinIndex = m_Pinned.FindLastIndex(s => s.Folder == sceneEntry.Folder);
			if (pinIndex == -1) {
				m_Pinned.Add(sceneEntry);
			} else {
				m_Pinned.Insert(pinIndex + 1, sceneEntry);
			}

			// Don't sort m_Scenes or m_Pinned.
			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();
		}

		// Show context menu with options.
		private void ShowPinnedOptions(SceneEntry sceneEntry)
		{
			var menu = new GenericMenu();
			int index = m_Pinned.IndexOf(sceneEntry);

			foreach (PinnedOptions value in Enum.GetValues(typeof(PinnedOptions))) {
				menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(value.ToString())), false, OnSelectPinnedOption, new KeyValuePair<PinnedOptions, int>(value, index));
			}

			menu.ShowAsContext();
		}

		private void OnSelectPinnedOption(object data)
		{
			var pair = (KeyValuePair<PinnedOptions, int>)data;
			int index = pair.Value;

			switch (pair.Key) {

				case PinnedOptions.Unpin:
					bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

					m_Scenes.Insert(0, m_Pinned[index]);
					m_Pinned.RemoveAt(index);

					if (shouldAutoSnapSplitter) {
						AutoSnapSplitter();
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
					break;

				case PinnedOptions.ShowInProject:
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(m_Pinned[index].Path));
					break;
			}

			SortScenes(m_Scenes);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}


		private readonly GUIContent m_PreferencesColorizePatternsLabelCache = new GUIContent("Colorize Entries", "Set colors of scenes based on a folder or name patterns.");
		private Vector2 m_PreferencesScroll;
		private bool m_PreferencesPersonalFold = true;
		private bool m_PreferencesProjectFold = true;
		private bool m_PreferencesAboutFold = true;

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

			m_PreferencesScroll = EditorGUILayout.BeginScrollView(m_PreferencesScroll);

			//
			// Personal Preferences
			//
			m_PreferencesPersonalFold = EditorGUILayout.Foldout(m_PreferencesPersonalFold, "Personal Preferences:", FOLD_OUT_BOLD);
			if (m_PreferencesPersonalFold) {
				EditorGUI.indentLevel++;

				EditorGUILayout.HelpBox("Hint: check the the tooltips.", MessageType.Info);

				m_PersonalPrefs.SortType = (SortType) EditorGUILayout.EnumPopup(new GUIContent("Sort by", "How to sort the list of scenes (not the pinned ones).\nNOTE: Changing this might override the \"Most Recent\" sort done by now."), m_PersonalPrefs.SortType);
				m_PersonalPrefs.SceneDisplay = (SceneDisplay) EditorGUILayout.EnumPopup(new GUIContent("Display entries", "How scenes should be displayed."), m_PersonalPrefs.SceneDisplay);

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

					EditorGUILayout.PropertyField(sp.FindPropertyRelative("ColorizePatterns"), m_PreferencesColorizePatternsLabelCache, true);

					so.ApplyModifiedProperties();


					foreach(var cf in m_PersonalPrefs.ColorizePatterns) {
						if (string.IsNullOrEmpty(cf.Patterns)) {
							cf.BackgroundColor = Color.white;
							cf.TextColor = Color.black;
						}
					}
				}

				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space();

			//
			// Project Preferences
			//
			m_PreferencesProjectFold = EditorGUILayout.Foldout(m_PreferencesProjectFold, "Project Preferences:", FOLD_OUT_BOLD);
			if (m_PreferencesProjectFold) {

				EditorGUI.indentLevel++;

				EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder.\nFeel free to add them to your version control system.\nCoordinate any changes here with your team.", MessageType.Warning);

				var so = new SerializedObject(this);
				var sp = so.FindProperty("m_ProjectPrefs");

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("ColorizePatterns"), m_PreferencesColorizePatternsLabelCache, true);
				EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), new GUIContent("Exclude paths", "Asset paths that will be ignored."), true);

				so.ApplyModifiedProperties();


				foreach (var cf in m_ProjectPrefs.ColorizePatterns) {
					if (string.IsNullOrEmpty(cf.Patterns)) {
						cf.BackgroundColor = Color.white;
						cf.TextColor = Color.black;
					}
				}

				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space();

			//
			// About
			//
			m_PreferencesAboutFold = EditorGUILayout.Foldout(m_PreferencesAboutFold, "About:", FOLD_OUT_BOLD);
			if (m_PreferencesAboutFold) {
				EditorGUI.indentLevel++;

				EditorGUILayout.LabelField("Created by Filip Slavov (NibbleByte)");

				EditorGUILayout.BeginHorizontal();

				if (GUILayout.Button("Plugin at Asset Store", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					EditorUtility.DisplayDialog("Under construction", "Asset Store plugin is coming really soon...", "Fine");
				}

				if (GUILayout.Button("Source at GitHub", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var githubURL = "https://github.com/NibbleByte/AssetsManagementTools";
					Application.OpenURL(githubURL);
				}

				EditorGUILayout.EndHorizontal();

				EditorGUI.indentLevel--;
			}

			GUILayout.Space(16f);

			EditorGUILayout.EndScrollView();
		}

		private void SaveAndClosePreferences()
		{
			var splitterChars = new char[] { ';' };

			m_PersonalPrefs.DisplayRemoveFolders.RemoveAll(string.IsNullOrWhiteSpace);
			m_PersonalPrefs.ColorizePatterns.RemoveAll(c => c.Patterns.Split(splitterChars, StringSplitOptions.RemoveEmptyEntries).Length == 0);

			m_ProjectPrefs.Exclude.RemoveAll(string.IsNullOrWhiteSpace);
			m_ProjectPrefs.ColorizePatterns.RemoveAll(c => c.Patterns.Split(splitterChars, StringSplitOptions.RemoveEmptyEntries).Length == 0);

			// Sort explicitly, so assets will change on reload.
			SortScenes(m_Scenes);

			RefreshDisplayNames(m_Scenes);
			RefreshDisplayNames(m_Pinned);

			RefreshColorizePatterns(m_Scenes);
			RefreshColorizePatterns(m_Pinned);

			StorePrefs();

			m_ShowPreferences = false;
			AssetsChanged = true;
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
		public static void OnWillCreateAsset(string path)
		{
			if (path.EndsWith(".unity.meta") || path.EndsWith(".unity"))
				ScenesInProject.AssetsChanged = true;
		}

		public static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			if (path.EndsWith(".unity.meta") || path.EndsWith(".unity"))
				ScenesInProject.AssetsChanged = true;

			return AssetDeleteResult.DidNotDelete;
		}

		public static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
		{
			if (oldPath.EndsWith(".unity.meta") || oldPath.EndsWith(".unity")) {
				ScenesInProject.AssetsChanged = true;
			} else {
				// Check if this is folder. Folders can contain scenes.
				// This is not accurate, but fast?
				for(int i = oldPath.Length - 1; i >= 0; --i) {
					char ch = oldPath[i];
					if (ch == '.')	// It's a file (hopefully)
						break;

					if (ch == '/') { // It's a folder
						ScenesInProject.AssetsChanged = true;
						break;
					}
				}
			}



			return AssetMoveResult.DidNotMove;
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
